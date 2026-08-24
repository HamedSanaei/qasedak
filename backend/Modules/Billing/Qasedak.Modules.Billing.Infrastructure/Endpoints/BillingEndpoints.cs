using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Billing.Application;
using Qasedak.Modules.Billing.Application.Payments;
using Qasedak.Modules.Billing.Domain;
using Qasedak.Modules.Billing.Domain.Payments;
using Qasedak.Modules.Billing.Infrastructure.Payments;

namespace Qasedak.Modules.Billing.Infrastructure.Endpoints;

/// <summary>
/// Smallest viable billing HTTP surface (ADR-008):
///   - plan catalog + workspace subscription overview (member-guarded);
///   - checkout → server-owned PaymentAttempt + provider redirect URL (member-guarded);
///   - payment status/history (member-guarded, no secrets);
///   - PUBLIC provider callback: resolves the attempt server-side and verifies
///     server-to-server — callback query values alone never activate anything.
/// The browser is 302-redirected to a frontend result page after finalization.
/// </summary>
public static class BillingEndpoints
{
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var plans = endpoints.MapGroup("/api/v1/billing")
            .WithTags("Billing");

        // Plan catalog: authenticated users may browse; prices are display-only truth.
        plans.MapGet("/plans", async (
            IPlanRepository repository,
            IPaymentGatewayResolver resolver,
            CancellationToken cancellationToken) =>
        {
            var catalog = await repository.ListAsync(cancellationToken);
            return Results.Ok(new
            {
                providers = resolver.EnabledProviderIds,
                items = catalog.Select(p => new
                {
                    code = p.Code,
                    name = p.Name,
                    amountIrr = p.AmountIrr, // canonical IRR; UI formats, never mutates
                    purchasable = p.IsPurchasable,
                    features = p.Entitlements.Select(e => new { key = e.FeatureKey, limit = e.Limit }),
                }),
            });
        }).RequireAuthorization();

        var workspaces = endpoints.MapGroup("/api/v1/workspaces/{workspaceId:guid}/billing")
            .WithTags("Billing")
            .RequireAuthorization("workspace-member");

        workspaces.MapGet("/subscription", async (
            Guid workspaceId,
            PaymentQueries queries,
            CancellationToken cancellationToken) =>
        {
            var overview = await queries.GetSubscriptionOverviewAsync(workspaceId, DateTimeOffset.UtcNow, cancellationToken);
            return overview is null
                ? Results.NotFound(new { code = "billing.subscriptionNotFound" })
                : Results.Ok(overview);
        });

        workspaces.MapPost("/checkout", async (
            Guid workspaceId,
            CheckoutRequest request,
            CreateCheckoutUseCase useCase,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.PlanCode))
            {
                return Results.Json(new { code = "billing.planCodeRequired" }, statusCode: StatusCodes.Status400BadRequest);
            }

            var callbackBase = configuration["Billing:Payments:CallbackBaseUrl"];
            if (string.IsNullOrWhiteSpace(callbackBase))
            {
                return Results.Json(new { code = "payment.callbackNotConfigured" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            try
            {
                var checkout = await useCase.ExecuteAsync(
                    workspaceId,
                    request.PlanCode,
                    request.ProviderId ?? string.Empty,
                    $"{callbackBase.TrimEnd('/')}/api/v1/payments/callback/{{provider}}?attempt={{attemptId}}",
                    cancellationToken);
                return Results.Accepted($"/api/v1/workspaces/{workspaceId}/billing/payments/{checkout.AttemptId}", new
                {
                    attemptId = checkout.AttemptId,
                    provider = checkout.ProviderId,
                    redirectUrl = checkout.RedirectUrl,
                });
            }
            catch (BillingDomainException exception)
            {
                return BillingFailureMapper.ToResult(exception.RuleCode);
            }
        });

        workspaces.MapGet("/payments/{attemptId:guid}", async (
            Guid workspaceId,
            Guid attemptId,
            PaymentQueries queries,
            CancellationToken cancellationToken) =>
        {
            var status = await queries.GetStatusAsync(attemptId, cancellationToken);
            return status is null || status.WorkspaceId != workspaceId
                ? Results.NotFound(new { code = PaymentFailures.NotFound })
                : Results.Ok(new
                {
                    attemptId = status.AttemptId,
                    status = status.Status,
                    failureCode = status.FailureCode,
                    amountIrr = status.AmountIrr,
                    provider = status.ProviderId,
                    createdAtUtc = status.CreatedAtUtc,
                    verifiedAtUtc = status.VerifiedAtUtc,
                });
        });

        workspaces.MapGet("/payments", async (
            Guid workspaceId,
            PaymentQueries queries,
            CancellationToken cancellationToken) =>
        {
            var history = await queries.ListAsync(workspaceId, cancellationToken);
            return Results.Ok(new
            {
                items = history.Select(p => new
                {
                    attemptId = p.AttemptId,
                    status = p.Status,
                    failureCode = p.FailureCode,
                    amountIrr = p.AmountIrr,
                    provider = p.ProviderId,
                    createdAtUtc = p.CreatedAtUtc,
                    verifiedAtUtc = p.VerifiedAtUtc,
                }),
            });
        });

        // PUBLIC provider return endpoints. No user identity is trusted here: the attempt
        // is resolved server-side by authority and the provider is verified S2S before
        // any state change; the browser then lands on a frontend result page.
        var callbacks = endpoints.MapGroup("/api/v1/payments/callback")
            .WithTags("Billing Callbacks");

        callbacks.MapGet("/{provider}", async (
            string provider,
            Guid? attempt,
            string? Authority,
            string? Status,
            FinalizePaymentUseCase useCase,
            PaymentQueries queries,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            if (!string.IsNullOrWhiteSpace(Authority))
            {
                try
                {
                    _ = await useCase.ExecuteCallbackAsync(Authority, Status ?? string.Empty, cancellationToken);
                }
                catch (BillingDomainException)
                {
                    // Unknown/replayed/outaged authorities still land on a safe result page;
                    // the workspace-scoped status endpoint remains the source of truth.
                }
            }

            return await RedirectToResultPageAsync(attempt, queries, configuration, cancellationToken);
        });

        // Behpardakht posts its documented callback fields as an HTTP form (§9.1). Only
        // the fields needed for validation are read; the raw form is never logged and the
        // callback amount is ignored entirely — verification is server-to-server.
        callbacks.MapPost("/{provider}", async (
            string provider,
            Guid? attempt,
            HttpRequest httpRequest,
            FinalizePaymentUseCase useCase,
            PaymentQueries queries,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            var form = await httpRequest.ReadFormAsync(cancellationToken);
            string? FormValue(string key) => form[key].FirstOrDefault();

            var refId = FormValue("RefId")?.Trim();
            var resCode = FormValue("ResCode")?.Trim();
            if (!string.IsNullOrWhiteSpace(refId) && !string.IsNullOrWhiteSpace(resCode))
            {
                // Vendor §9.2: ResCode 0 = sale completed at the gateway; 17 = user cancel;
                // anything else is a failed sale. None of these are proof of payment.
                var statusHint = resCode switch
                {
                    "0" => "OK",
                    "17" => "CANCEL",
                    _ => "FAILED",
                };
                try
                {
                    _ = await useCase.ExecuteCallbackAsync(new PaymentCallbackContext(
                        refId,
                        statusHint,
                        long.TryParse(FormValue("SaleOrderId"), out var saleOrderId) ? saleOrderId : null,
                        FormValue("SaleReferenceId")?.Trim(),
                        // Masked PAN only (vendor masks it); full PAN is never sent or stored.
                        FormValue("CardHolderPan")?.Trim() is { Length: > 0 } pan && pan.Contains('*') ? pan : null),
                        cancellationToken);
                }
                catch (BillingDomainException)
                {
                    // Same safe-landing policy as the GET path above.
                }
            }

            return await RedirectToResultPageAsync(attempt, queries, configuration, cancellationToken);
        });

        // Behpardakht redirect contract (§8.2): after bpPayRequest the customer must be
        // POSTed to startpay.mellat carrying only the RefId. This jump endpoint serves a
        // minimal auto-submitting form so credentials never reach the browser; hosting it
        // on the registered merchant domain satisfies the Referer requirement.
        var startpay = endpoints.MapGroup("/api/v1/payments/mellat")
            .WithTags("Billing Callbacks");

        startpay.MapGet("/startpay", async (
            string? authority,
            IOptions<BehpardakhtOptions> optionsAccessor) =>
        {
            if (string.IsNullOrWhiteSpace(authority))
            {
                return Results.Json(new { code = "payment.notFound" }, statusCode: StatusCodes.Status404NotFound);
            }

            var options = optionsAccessor.Value;
            var pageUrl = string.IsNullOrWhiteSpace(options.PaymentPageUrl)
                ? "https://bpm.shaparak.ir/pgwchannel/startpay.mellat"
                : options.PaymentPageUrl;

            var html =
                "<!doctype html><html lang=\"fa\" dir=\"rtl\"><head><meta charset=\"utf-8\">" +
                "<title>انتقال به درگاه پرداخت</title></head>" +
                "<body><p>در حال انتقال به درگاه پرداخت…</p>" +
                $"<form method=\"POST\" action=\"{System.Security.SecurityElement.Escape(pageUrl)}\">" +
                $"<input type=\"hidden\" name=\"RefId\" value=\"{System.Security.SecurityElement.Escape(authority)}\" />" +
                "<noscript><button type=\"submit\">ادامه</button></noscript>" +
                "</form>" +
                "<script>document.forms[0].submit();</script>" +
                "</body></html>";

            return Results.Content(html, "text/html", System.Text.Encoding.UTF8);
        }).DisableAntiforgery();

        return endpoints;
    }

    private static async Task<IResult> RedirectToResultPageAsync(
        Guid? attempt,
        PaymentQueries queries,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var resolved = await queries.GetStatusAsync(attempt ?? Guid.Empty, cancellationToken);
        var state = resolved?.Status switch
        {
            nameof(PaymentAttemptStatus.Verified) => "success",
            nameof(PaymentAttemptStatus.Failed) => "failed",
            _ => "pending",
        };
        var frontendBase = configuration["Billing:Payments:FrontendBaseUrl"]?.TrimEnd('/') ?? string.Empty;
        return Results.Redirect(
            $"{frontendBase}/dashboard/billing/result?state={Uri.EscapeDataString(state)}&attempt={(resolved is null ? string.Empty : resolved.AttemptId.ToString())}",
            permanent: false,
            preserveMethod: false);
    }
}

public sealed record CheckoutRequest(string? PlanCode, string? ProviderId);
