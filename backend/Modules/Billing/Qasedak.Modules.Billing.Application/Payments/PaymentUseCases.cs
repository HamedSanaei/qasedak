using Qasedak.BuildingBlocks.Application.Auditing;
using Qasedak.Modules.Billing.Domain;
using Qasedak.Modules.Billing.Domain.Payments;

namespace Qasedak.Modules.Billing.Application.Payments;

/// <summary>Raised when two writers race on the same payment attempt row (persistence-translated).</summary>
public sealed class PaymentConcurrencyException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>Persistence port for durable checkout attempts.</summary>
public interface IPaymentAttemptRepository
{
    Task<PaymentAttempt?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Resolves an attempt by its provider authority (unique index); callback replay lands here.</summary>
    Task<PaymentAttempt?> FindByAuthorityAsync(string authority, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentAttempt>> ListByWorkspaceAsync(Guid workspaceId, int limit = 50, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(PaymentAttempt attempt, CancellationToken cancellationToken = default);
}

/// <summary>
/// Chooses among the enabled payment gateways registered by the composition root.
/// Unknown or disabled providers fail closed with stable codes.
/// </summary>
public interface IPaymentGatewayResolver
{
    IPaymentGateway Resolve(string providerId);

    IReadOnlyList<string> EnabledProviderIds { get; }
}

public sealed record CheckoutResult(Guid AttemptId, string ProviderId, string RedirectUrl);

public sealed record PaymentStatusResult(
    Guid AttemptId,
    Guid WorkspaceId,
    Guid PlanId,
    string ProviderId,
    long AmountIrr,
    string Status,
    string? FailureCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? VerifiedAtUtc);

public sealed record FinalizePaymentOutcome(
    PaymentAttempt Attempt,
    bool AlreadyApplied);

/// <summary>
/// Creates a server-owned checkout: resolves the plan (price is server-authoritative),
/// opens a Pending attempt, asks the selected gateway for an authority and returns the
/// browser redirect URL. The client never supplies amounts.
/// </summary>
public sealed class CreateCheckoutUseCase(
    IPlanRepository plans,
    IPaymentAttemptRepository attempts,
    IPaymentGatewayResolver resolver,
    IAuditTrail? audit = null)
{
    public async Task<CheckoutResult> ExecuteAsync(
        Guid workspaceId,
        string planCode,
        string providerId,
        string callbackUrlTemplate,
        CancellationToken cancellationToken = default)
    {
        var plan = await plans.FindByCodeAsync(planCode, cancellationToken)
            ?? throw new BillingDomainException(PaymentFailures.PlanNotFound, $"Plan '{planCode}' does not exist.");

        if (!plan.IsPurchasable)
        {
            throw new BillingDomainException(PaymentFailures.PlanNotPurchasable, $"Plan '{planCode}' cannot be purchased.");
        }

        PaymentInitialization initialization;
        PaymentAttempt attempt;
        try
        {
            // Resolution is inside the guarded region: unknown/disabled providers map to
            // stable failure codes instead of leaking adapter exceptions.
            var gateway = resolver.Resolve(providerId);
            attempt = PaymentAttempt.Create(
                Guid.CreateVersion7(), workspaceId, plan.Id, gateway.ProviderId, plan.AmountIrr, DateTimeOffset.UtcNow);

            // The callback carries only the public attempt id; no secrets in URLs.
            var callbackUrl = callbackUrlTemplate.Replace("{attemptId}", attempt.Id.ToString(), StringComparison.Ordinal);
            initialization = await gateway.CreatePaymentAsync(
                new CreatePaymentRequest(attempt.Id, attempt.AmountIrr, $"Qasedak subscription: {plan.Code}", callbackUrl),
                cancellationToken);
        }
        catch (PaymentProviderDisabledException)
        {
            throw new BillingDomainException(PaymentFailures.ProviderDisabled, $"Provider '{providerId}' is not enabled.");
        }
        catch (PaymentProviderUnknownException)
        {
            throw new BillingDomainException(PaymentFailures.ProviderUnknown, $"Provider '{providerId}' is not supported.");
        }
        catch (PaymentRequestRejectedException exception)
        {
            throw new BillingDomainException(PaymentFailures.RequestRejected, exception.Message);
        }
        catch (PaymentGatewayUnavailableException exception)
        {
            throw new BillingDomainException(PaymentFailures.ProviderUnavailable, exception.Message);
        }

        attempt.AttachAuthority(initialization.Authority);
        await attempts.SaveChangesAsync(attempt, cancellationToken);

        if (audit is not null)
        {
            await audit.RecordAsync(AuditEntry.New(
                "billing.payment.checkout",
                DateTimeOffset.UtcNow,
                workspaceId: workspaceId,
                targetType: "payment_attempt",
                targetId: attempt.Id.ToString(),
                detailsJson: System.Text.Json.JsonSerializer.Serialize(new
                {
                    provider = attempt.ProviderId,
                    amountIrr = attempt.AmountIrr,
                    planCode,
                    // Authority is a public token but is still omitted from audit detail.
                })), cancellationToken);
        }

        return new CheckoutResult(attempt.Id, attempt.ProviderId, initialization.RedirectUrl);
    }
}

/// <summary>
/// Completes a payment after the provider redirects back. The callback query alone NEVER
/// activates anything: with Status=OK this use case verifies server-to-server; with NOK
/// it records a user cancellation. Every path is idempotent — duplicate callbacks,
/// replays and refreshes resolve to the same terminal state without double-applying
/// entitlements (DB-level concurrency + state-machine guards).
/// </summary>
public sealed class FinalizePaymentUseCase(
    IPaymentAttemptRepository attempts,
    ISubscriptionRepository subscriptions,
    IPaymentGatewayResolver resolver,
    IAuditTrail? audit = null)
{
    /// <summary>Length of one paid billing period.</summary>
    private static readonly TimeSpan BillingPeriod = TimeSpan.FromDays(30);

    public async Task<FinalizePaymentOutcome> ExecuteCallbackAsync(
        string authority,
        string callbackStatus,
        CancellationToken cancellationToken = default)
    {
        var attempt = await attempts.FindByAuthorityAsync(authority, cancellationToken)
            ?? throw new BillingDomainException(PaymentFailures.NotFound, "The payment attempt does not exist.");

        return await FinalizeAsync(attempt, callbackStatus, cancellationToken);
    }

    public async Task<FinalizePaymentOutcome> ExecuteByIdAsync(
        Guid attemptId,
        string callbackStatus,
        CancellationToken cancellationToken = default)
    {
        var attempt = await attempts.FindByIdAsync(attemptId, cancellationToken)
            ?? throw new BillingDomainException(PaymentFailures.NotFound, "The payment attempt does not exist.");

        return await FinalizeAsync(attempt, callbackStatus, cancellationToken);
    }

    private async Task<FinalizePaymentOutcome> FinalizeAsync(
        PaymentAttempt attempt, string callbackStatus, CancellationToken cancellationToken)
    {
        if (attempt.IsTerminal)
        {
            // Idempotent replay: already finalized elsewhere (refresh, duplicate callback,
            // retry). Report the existing truth without re-applying entitlements.
            return new FinalizePaymentOutcome(attempt, AlreadyApplied: true);
        }

        if (!string.Equals(callbackStatus, "OK", StringComparison.OrdinalIgnoreCase))
        {
            attempt.MarkFailed(PaymentFailures.CanceledByUser, DateTimeOffset.UtcNow);
            await attempts.SaveChangesAsync(attempt, cancellationToken);
            await RecordAuditAsync("billing.payment.canceled", attempt, cancellationToken);
            return new FinalizePaymentOutcome(attempt, AlreadyApplied: false);
        }

        var gateway = resolver.Resolve(attempt.ProviderId);
        PaymentVerificationResult verification;
        try
        {
            verification = await gateway.VerifyAsync(
                new VerifyPaymentRequest(attempt.Authority!, attempt.AmountIrr), cancellationToken);
        }
        catch (PaymentGatewayUnavailableException)
        {
            // Transient outage: the attempt stays Pending so a later retry can verify.
            throw new BillingDomainException(PaymentFailures.ProviderUnavailable, "The payment provider is temporarily unavailable.");
        }
        catch (PaymentRequestRejectedException exception)
        {
            attempt.MarkFailed(PaymentFailures.VerifyRejected, DateTimeOffset.UtcNow);
            await attempts.SaveChangesAsync(attempt, cancellationToken);
            await RecordAuditAsync("billing.payment.failed", attempt, cancellationToken);
            throw new BillingDomainException(PaymentFailures.VerifyRejected, exception.Message);
        }

        switch (verification.Outcome)
        {
            case PaymentVerificationOutcome.Verified:
                await ApplyEntitlementOnceAsync(attempt, verification, cancellationToken);
                break;

            case PaymentVerificationOutcome.AlreadyVerified:
                // The provider verified this transaction earlier while our side stayed
                // Pending (e.g. our response to the first callback was lost). Apply once.
                await ApplyEntitlementOnceAsync(attempt, verification with { Outcome = PaymentVerificationOutcome.Verified }, cancellationToken);
                break;

            case PaymentVerificationOutcome.Failed:
            default:
                attempt.MarkFailed(PaymentFailures.VerifyRejected, DateTimeOffset.UtcNow);
                await attempts.SaveChangesAsync(attempt, cancellationToken);
                await RecordAuditAsync("billing.payment.failed", attempt, cancellationToken);
                break;
        }

        return new FinalizePaymentOutcome(attempt, AlreadyApplied: false);
    }

    /// <summary>
    /// Single atomic transition: attempt → Verified and subscription period applied in one
    /// SaveChanges. Optimistic concurrency (xmin token) makes concurrent callbacks safe —
    /// the loser reloads, sees Verified, and reports idempotent success.
    /// </summary>
    private async Task ApplyEntitlementOnceAsync(
        PaymentAttempt attempt, PaymentVerificationResult verification, CancellationToken cancellationToken)
    {
        try
        {
            attempt.MarkVerified(verification.ProviderReferenceId ?? verification.CardHash ?? "verified", verification.MaskedCardPan, DateTimeOffset.UtcNow);
            await ApplyToSubscriptionAsync(attempt, cancellationToken);
            await attempts.SaveChangesAsync(attempt, cancellationToken);
            await RecordAuditAsync("billing.payment.verified", attempt, cancellationToken);
        }
        catch (PaymentConcurrencyException)
        {
            var winner = await attempts.FindByIdAsync(attempt.Id, cancellationToken)
                ?? throw new BillingDomainException(PaymentFailures.NotFound, "The payment attempt does not exist.");
            if (winner.Status != PaymentAttemptStatus.Verified)
            {
                throw new BillingDomainException(PaymentFailures.WrongState, "Concurrent payment finalization produced an unexpected state.");
            }

            // Concurrent callback won the race; entitlement is already applied exactly once.
        }
    }

    /// <summary>Opens/extends the workspace's subscription exactly once per verified attempt.</summary>
    private async Task ApplyToSubscriptionAsync(PaymentAttempt attempt, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await subscriptions.FindByWorkspaceAsync(attempt.WorkspaceId, cancellationToken);
        if (existing is null)
        {
            var activated = Subscription.Activate(
                Guid.CreateVersion7(), attempt.WorkspaceId, attempt.PlanId, now, now.Add(BillingPeriod));
            await subscriptions.SaveChangesAsync(activated, cancellationToken);
            return;
        }

        if (existing.Status == SubscriptionStatus.Trial)
        {
            existing.ConvertTrialToActive(attempt.PlanId, now, now.Add(BillingPeriod));
        }
        else if (existing.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue)
        {
            // Extend from the current period end when still live, else from now.
            var nextEnd = (existing.CurrentPeriodEndUtc is { } end && end > now) ? end.Add(BillingPeriod) : now.Add(BillingPeriod);
            existing.Renew(now, nextEnd);
        }
        else
        {
            // Canceled/Expired subscriptions reopen as a fresh activation row-state.
            existing.ChangePlan(attempt.PlanId);
            existing.Renew(now, now.Add(BillingPeriod));
        }

        await subscriptions.SaveChangesAsync(existing, cancellationToken);
    }

    private async Task RecordAuditAsync(string action, PaymentAttempt attempt, CancellationToken cancellationToken)
    {
        if (audit is null)
        {
            return;
        }

        await audit.RecordAsync(AuditEntry.New(
            action,
            DateTimeOffset.UtcNow,
            workspaceId: attempt.WorkspaceId,
            targetType: "payment_attempt",
            targetId: attempt.Id.ToString(),
            detailsJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                provider = attempt.ProviderId,
                status = attempt.Status.ToString(),
                failureCode = attempt.FailureCode,
                // No authorities, references beyond presence, or card values here.
            })), cancellationToken);
    }
}

/// <summary>Read-side projections for the billing UI. No secrets leave the server.</summary>
public sealed class PaymentQueries(IPaymentAttemptRepository attempts, ISubscriptionRepository subscriptions, IPlanRepository plans)
{
    public async Task<PaymentStatusResult?> GetStatusAsync(Guid attemptId, CancellationToken cancellationToken = default)
    {
        var attempt = await attempts.FindByIdAsync(attemptId, cancellationToken);
        return attempt is null ? null : ToStatus(attempt);
    }

    public async Task<IReadOnlyList<PaymentStatusResult>> ListAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        (await attempts.ListByWorkspaceAsync(workspaceId, cancellationToken: cancellationToken)).Select(ToStatus).ToList();

    public async Task<object?> GetSubscriptionOverviewAsync(Guid workspaceId, DateTimeOffset atUtc, CancellationToken cancellationToken = default)
    {
        var subscription = await subscriptions.FindByWorkspaceAsync(workspaceId, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        var plan = await plans.FindByIdAsync(subscription.PlanId, cancellationToken);
        return new
        {
            status = subscription.Status.ToString(),
            startedAtUtc = subscription.StartedAtUtc,
            currentPeriodEndUtc = subscription.CurrentPeriodEndUtc,
            entitled = subscription.IsEntitledAt(atUtc),
            plan = plan is null ? null : new { code = plan.Code, name = plan.Name, amountIrr = plan.AmountIrr },
        };
    }

    private static PaymentStatusResult ToStatus(PaymentAttempt attempt) => new(
        attempt.Id,
        attempt.WorkspaceId,
        attempt.PlanId,
        attempt.ProviderId,
        attempt.AmountIrr,
        attempt.Status.ToString(),
        attempt.FailureCode,
        attempt.CreatedAtUtc,
        attempt.VerifiedAtUtc);
}
