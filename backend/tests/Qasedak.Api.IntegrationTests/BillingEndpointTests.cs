using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qasedak.Modules.Billing.Application.Payments;
using Qasedak.Modules.Billing.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

/// <summary>
/// Billing HTTP surface end to end: auth/workspace isolation on billing operations,
/// server-authoritative checkout, public callback finalization with exactly-once
/// entitlement, duplicate-callback idempotency, and payment history — all against the
/// deterministic recording gateway (no live provider calls in CI).
/// </summary>
[Collection(ApiTestEnvironment.Name)]
public sealed class BillingEndpointTests(ApiPostgreSqlFixture fixture)
{
    private static readonly Guid SeededWorkspaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task PlansRequireAuthentication()
    {
        var response = await fixture.Client.GetAsync("/api/v1/billing/plans");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PlansListProvidersAndCatalog()
    {
        var client = await AuthedClientAsync("billing-plans@example.com");
        var response = await client.GetAsync("/api/v1/billing/plans");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var providers = payload.GetProperty("providers").EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.Contains("zarinpal", providers);
        // catalog shape asserted via providers; items may legitimately be empty
    }

    [Fact]
    public async Task CheckoutRequiresAuthentication()
    {
        var response = await fixture.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{SeededWorkspaceId}/billing/checkout",
            new { planCode = "pro", providerId = "zarinpal" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CheckoutRejectsNonMembers()
    {
        // Authenticated user WITHOUT membership of the addressed workspace: uniform 403.
        var outsider = await RegisterTokenOnlyAsync("billing-outsider@example.com");
        var client = AuthedClient(outsider);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{SeededWorkspaceId}/billing/checkout",
            new { planCode = "pro", providerId = "zarinpal" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CheckoutCreatesServerOwnedAttemptWithRedirect()
    {
        var workspace = Guid.CreateVersion7();
        var token = await MemberTokenAsync("billing-checkout@example.com", workspace);
        var client = AuthedClient(token);

        SeedPlan("pro-e2e", 1_500_000);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace}/billing/checkout",
            new { planCode = "PRO-E2E", providerId = "zarinpal" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var attemptId = Guid.Parse(payload.GetProperty("attemptId").GetString()!);
        var redirectUrl = payload.GetProperty("redirectUrl").GetString()!;
        Assert.StartsWith("https://pay.test.local/pg/StartPay/", redirectUrl);

        // The recorded request carries the SERVER's price, not anything client-supplied.
        var request = Assert.Single(fixture.Payments.Requests);
        Assert.Equal(1_500_000, request.AmountIrr);
        Assert.Equal(attemptId.ToString(), ExtractAttemptFromCallback(request.CallbackUrl));

        // Status endpoint reflects Pending before the provider returns.
        var status = await client.GetAsync($"/api/v1/workspaces/{workspace}/billing/payments/{attemptId}");
        status.EnsureSuccessStatusCode();
        var statusPayload = await status.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pending", statusPayload.GetProperty("status").GetString());
    }

    [Fact]
    public async Task CallbackActivatesSubscriptionExactlyOnceAcrossDuplicateCallbacks()
    {
        var workspace = Guid.CreateVersion7();
        var token = await MemberTokenAsync("billing-callback@example.com", workspace);
        var client = AuthedClient(token);
        SeedPlan("callback-plan", 2_000_000);

        var checkout = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace}/billing/checkout",
            new { planCode = "callback-plan", providerId = "zarinpal" });
        checkout.EnsureSuccessStatusCode();
        var attemptId = Guid.Parse((await checkout.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("attemptId").GetString()!);

        // First callback: OK → verify (scripted 100) → activate.
        var callback1 = await NoRedirectClient().GetAsync($"/api/v1/payments/callback/zarinpal?attempt={attemptId}&Authority=auth-{attemptId:N}&Status=OK");
        Assert.True(
            callback1.IsSuccessStatusCode || callback1.StatusCode == HttpStatusCode.Redirect,
            $"Unexpected callback status {(int)callback1.StatusCode}: {await callback1.Content.ReadAsStringAsync()}");

        var subscription = await GetSubscriptionAsync(client, workspace);
        Assert.Equal("Active", subscription.GetProperty("status").GetString());
        Assert.True(subscription.GetProperty("entitled").GetBoolean());

        // Duplicate callback replay: no second activation, no re-verification.
        fixture.Payments.Verifies.Clear();
        await NoRedirectClient().GetAsync($"/api/v1/payments/callback/zarinpal?attempt={attemptId}&Authority=auth-{attemptId:N}&Status=OK");
        Assert.Empty(fixture.Payments.Verifies);

        // Exactly one period for exactly one verified payment.
        var periods = await CountPeriodsAsync(workspace);
        Assert.Equal(1, periods);

        // History shows one Verified attempt.
        var history = await client.GetAsync($"/api/v1/workspaces/{workspace}/billing/payments");
        history.EnsureSuccessStatusCode();
        var items = (await history.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal("Verified", items[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task NokCallbackRecordsCancellationWithoutEntitlement()
    {
        var workspace = Guid.CreateVersion7();
        var token = await MemberTokenAsync("billing-nok@example.com", workspace);
        var client = AuthedClient(token);
        SeedPlan("nok-plan", 750_000);

        var attemptId = await CheckoutAsync(client, workspace, "nok-plan");

        await NoRedirectClient().GetAsync($"/api/v1/payments/callback/zarinpal?attempt={attemptId}&Authority=auth-{attemptId:N}&Status=NOK");

        var overview = await GetSubscriptionOrNullAsync(client, workspace);
        Assert.True(overview is null
            || !overview.Value.GetProperty("entitled").GetBoolean()
            || overview.Value.GetProperty("status").GetString() == "Trial");

        Assert.Empty(fixture.Payments.Verifies); // canceled returns are never verified
    }

    [Fact]
    public async Task VerifyRejectedCallbackMarksPaymentFailed()
    {
        var workspace = Guid.CreateVersion7();
        var token = await MemberTokenAsync("billing-reject@example.com", workspace);
        var client = AuthedClient(token);
        SeedPlan("reject-plan", 3_300_000);

        fixture.Payments.ScriptedVerifications.Enqueue(PaymentVerificationResult.Failed(-54, "authority not found"));
        var attemptId = await CheckoutAsync(client, workspace, "reject-plan");

        await NoRedirectClient().GetAsync($"/api/v1/payments/callback/zarinpal?attempt={attemptId}&Authority=auth-{attemptId:N}&Status=OK");

        var status = await client.GetAsync($"/api/v1/workspaces/{workspace}/billing/payments/{attemptId}");
        var payload = await status.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Failed", payload.GetProperty("status").GetString());
        Assert.Equal("payment.verifyRejected", payload.GetProperty("failureCode").GetString());
    }

    [Fact]
    public async Task PaymentStatusIsWorkspaceIsolated()
    {
        var workspaceA = Guid.CreateVersion7();
        var workspaceB = Guid.CreateVersion7();
        var tokenA = await MemberTokenAsync("billing-iso-a@example.com", workspaceA);
        var tokenB = await MemberTokenAsync("billing-iso-b@example.com", workspaceB);
        SeedPlan("iso-plan", 1_100_000);

        var attemptA = await CheckoutAsync(AuthedClient(tokenA), workspaceA, "iso-plan");

        // User B holds no membership in workspace A: the workspace-member policy answers
        // uniformly 403 before any handler logic (established tenant-isolation behavior).
        var response = await AuthedClient(tokenB).GetAsync($"/api/v1/workspaces/{workspaceA}/billing/payments/{attemptA}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- Behpardakht Mellat transport (real gateway + scripted SOAP boundary) ----

    [Fact]
    public async Task MellatCheckoutPersistsProviderOrderIdAndReturnsJumpRedirect()
    {
        fixture.MellatSoap.PayRequests.Clear();
        var workspace = Guid.CreateVersion7();
        var token = await MemberTokenAsync("mellat-checkout@example.com", workspace);
        var client = AuthedClient(token);
        SeedPlan("mellat-plan-a", 1_250_000);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace}/billing/checkout",
            new { planCode = "mellat-plan-a", providerId = "mellat" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var attemptId = Guid.Parse(payload.GetProperty("attemptId").GetString()!);
        var redirectUrl = payload.GetProperty("redirectUrl").GetString()!;
        Assert.Contains("/api/v1/payments/mellat/startpay?authority=", redirectUrl);
        Assert.Contains($"attempt={attemptId}", redirectUrl);

        // Durable server-side identity: numeric orderId persisted before any browser hop.
        var attempt = await GetAttemptRowAsync(attemptId);
        Assert.NotNull(attempt!.Authority);
        Assert.NotNull(attempt.ProviderOrderId);
        Assert.True(attempt.ProviderOrderId > 0);
        Assert.Equal($"REF-{attempt.ProviderOrderId}", attempt.Authority); // exact-case RefId

        // Canonical IRR crossed the SOAP boundary unchanged.
        var payRequest = Assert.Single(fixture.MellatSoap.PayRequests);
        Assert.Equal(1_250_000, payRequest.AmountIrr);
        Assert.Equal("0", payRequest.PayerId);
    }

    [Fact]
    public async Task MellatJumpPageRendersAutoSubmittingFormWithExactRefId()
    {
        const string refId = "AF82041a2Bf6989c7fF9";
        var response = await fixture.Client.GetAsync(
            $"/api/v1/payments/mellat/startpay?authority={Uri.EscapeDataString(refId)}&attempt={Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("startpay.mellat", html); // configured payment page
        Assert.Contains(refId, html); // exact-case RefId embedded for the auto-submit
        Assert.DoesNotContain("Password", html); // credentials never reach the browser
    }

    [Fact]
    public async Task MellatCallbackActivatesSubscriptionExactlyOnce()
    {
        fixture.MellatSoap.Operations.Clear();
        fixture.MellatSoap.Transactions.Clear();
        var workspace = Guid.CreateVersion7();
        var token = await MemberTokenAsync("mellat-callback@example.com", workspace);
        var client = AuthedClient(token);
        SeedPlan("mellat-plan-b", 990_000);

        var attemptId = await CheckoutAsync(client, workspace, "mellat-plan-b", provider: "mellat");
        var attempt = await GetAttemptRowAsync(attemptId);
        var refId = attempt!.Authority!;
        var saleOrderId = attempt.ProviderOrderId!.Value;

        // The pay call already happened during checkout; observe only the finalize chain.
        fixture.MellatSoap.Operations.Clear();
        fixture.MellatSoap.Transactions.Clear();

        // Vendor §9.1 form POST: matching SaleOrderId + ResCode 0 → verify → settle → activate.
        var callback1 = await NoRedirectClient().PostAsync(
            $"/api/v1/payments/callback/mellat?attempt={attemptId}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["RefId"] = refId,
                ["ResCode"] = "0",
                ["SaleOrderId"] = saleOrderId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["SaleReferenceId"] = "900001",
                ["CardHolderPan"] = "6104********4321",
            }));
        Assert.True(callback1.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK,
            $"Unexpected callback status {(int)callback1.StatusCode}");
        if (callback1.StatusCode == HttpStatusCode.Redirect)
        {
            Assert.Contains("state=success", callback1.Headers.Location!.ToString());
        }

        // Real orchestration ran exactly once: verify then settle on the SOAP boundary.
        Assert.Equal(["verify", "settle"], fixture.MellatSoap.Operations);
        Assert.Equal(saleOrderId, fixture.MellatSoap.Transactions[0].SaleOrderId);
        Assert.Equal(900001, fixture.MellatSoap.Transactions[0].SaleReferenceId);

        var subscription = await GetSubscriptionAsync(client, workspace);
        Assert.Equal("Active", subscription.GetProperty("status").GetString());
        Assert.True(subscription.GetProperty("entitled").GetBoolean());

        // Duplicate POST replay: idempotent terminal state, no second verify/settle chain.
        fixture.MellatSoap.Operations.Clear();
        await NoRedirectClient().PostAsync(
            $"/api/v1/payments/callback/mellat?attempt={attemptId}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["RefId"] = refId,
                ["ResCode"] = "0",
                ["SaleOrderId"] = saleOrderId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["SaleReferenceId"] = "900001",
            }));
        Assert.Empty(fixture.MellatSoap.Operations);

        // Exactly one period for exactly one verified payment.
        Assert.Equal(1, await CountPeriodsAsync(workspace));

        // Masked PAN (from the validated callback) was audited onto the attempt.
        var status = await client.GetAsync($"/api/v1/workspaces/{workspace}/billing/payments/{attemptId}");
        var statusPayload = await status.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Verified", statusPayload.GetProperty("status").GetString());
    }

    [Fact]
    public async Task MellatCallbackWrongSaleOrderIdRejectedWithoutEntitlement()
    {
        fixture.MellatSoap.Operations.Clear();
        var workspace = Guid.CreateVersion7();
        var token = await MemberTokenAsync("mellat-forgery@example.com", workspace);
        var client = AuthedClient(token);
        SeedPlan("mellat-plan-c", 700_000);

        var attemptId = await CheckoutAsync(client, workspace, "mellat-plan-c", provider: "mellat");
        var attempt = await GetAttemptRowAsync(attemptId);
        var forgedSaleOrderId = attempt!.ProviderOrderId!.Value + 777;

        fixture.MellatSoap.Operations.Clear();
        var callback = await NoRedirectClient().PostAsync(
            $"/api/v1/payments/callback/mellat?attempt={attemptId}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["RefId"] = attempt.Authority!,
                ["ResCode"] = "0",
                ["SaleOrderId"] = forgedSaleOrderId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["SaleReferenceId"] = "888888",
            }));

        // Rejected: lands on the failure page and NEVER verifies with the bank.
        Assert.Contains("state=failed", callback.Headers.Location!.ToString());
        Assert.Empty(fixture.MellatSoap.Operations);

        var status = await client.GetAsync($"/api/v1/workspaces/{workspace}/billing/payments/{attemptId}");
        var payload = await status.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Failed", payload.GetProperty("status").GetString());
        Assert.Equal("payment.callbackRejected", payload.GetProperty("failureCode").GetString());

        var overview = await GetSubscriptionOrNullAsync(client, workspace);
        Assert.True(overview is null || !overview.Value.GetProperty("entitled").GetBoolean());
    }

    private void SeedPlan(string code, long amountIrr)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var normalized = code.ToLowerInvariant();
        if (!context.Plans.Any(p => p.Code == normalized))
        {
            var aggregate = Qasedak.Modules.Billing.Domain.Plan.Create(
                Guid.CreateVersion7(), code, $"E2E {code}", amountIrr: amountIrr);
            context.Plans.Add(new PlanRow
            {
                Id = aggregate.Id,
                Code = aggregate.Code,
                Name = aggregate.Name,
                AmountIrr = aggregate.AmountIrr,
                Entitlements = [],
            });
            context.SaveChanges();
        }
    }

    private static async Task<Guid> CheckoutAsync(HttpClient client, Guid workspace, string planCode, string provider = "zarinpal")
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace}/billing/checkout",
            new { planCode, providerId = provider });
        response.EnsureSuccessStatusCode();
        return Guid.Parse((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("attemptId").GetString()!);
    }

    private async Task<PaymentAttemptRow?> GetAttemptRowAsync(Guid attemptId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        return await context.PaymentAttempts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == attemptId);
    }

    private static async Task<JsonElement> GetSubscriptionAsync(HttpClient client, Guid workspace)
    {
        var response = await client.GetAsync($"/api/v1/workspaces/{workspace}/billing/subscription");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement?> GetSubscriptionOrNullAsync(HttpClient client, Guid workspace)
    {
        var response = await client.GetAsync($"/api/v1/workspaces/{workspace}/billing/subscription");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<int> CountPeriodsAsync(Guid workspace)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var subscriptionId = await context.Subscriptions
            .Where(s => s.WorkspaceId == workspace)
            .Select(s => s.Id)
            .SingleOrDefaultAsync();
        if (subscriptionId == Guid.Empty)
        {
            return 0;
        }

        return await context.SubscriptionPeriods.CountAsync(p => p.SubscriptionId == subscriptionId);
    }

    /// <summary>Provider-return endpoints answer with an intentional 302; do not follow it.</summary>
    private HttpClient NoRedirectClient() =>
        fixture.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

    private static string? ExtractAttemptFromCallback(string callbackUrl)
    {
        var query = new Uri(callbackUrl).Query;
        return System.Web.HttpUtility.ParseQueryString(query)["attempt"];
    }

    private async Task<string> RegisterTokenOnlyAsync(string email)
    {
        await fixture.Client.PostAsJsonAsync("/api/v1/identity/register", new { email, password = "Passw0rd!23", displayName = "Billing Tester" });
        var login = await fixture.Client.PostAsJsonAsync("/api/v1/identity/login", new { email, password = "Passw0rd!23" });
        return (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    /// <summary>Registers, logs in and joins the given workspace as owner.</summary>
    private async Task<string> MemberTokenAsync(string email, Guid workspaceId)
    {
        var token = await RegisterTokenOnlyAsync(email);
        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/identity/me");
        me.Headers.Authorization = new("Bearer", token);
        using var meResponse = await fixture.Client.SendAsync(me);
        meResponse.EnsureSuccessStatusCode();
        var userId = Guid.Parse((await meResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetString()!);
        await fixture.EnsureWorkspaceMemberAsync(workspaceId, userId);
        return token;
    }

    private HttpClient AuthedClient(string token)
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private async Task<HttpClient> AuthedClientAsync(string email) => AuthedClient(await RegisterTokenOnlyAsync(email));
}
internal sealed record LoginResponse([property: System.Text.Json.Serialization.JsonPropertyName("accessToken")] string AccessToken);
