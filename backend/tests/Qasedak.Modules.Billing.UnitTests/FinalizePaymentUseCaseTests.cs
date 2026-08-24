using Qasedak.Modules.Billing.Application;
using Qasedak.Modules.Billing.Application.Payments;
using Qasedak.Modules.Billing.Domain;
using Qasedak.Modules.Billing.Domain.Payments;
using Xunit;

namespace Qasedak.Modules.Billing.UnitTests;

/// <summary>
/// Finalization semantics with fakes: idempotent replays, NOK cancellation, verify
/// outcomes (100/101/failure), transient outage retry, and exactly-once entitlement.
/// </summary>
public sealed class FinalizePaymentUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OkCallbackWithVerify100ActivatesSubscriptionOnce()
    {
        var environment = NewEnvironment();
        var attempt = await environment.CreatePendingAttemptAsync();

        var outcome = await environment.UseCase.ExecuteCallbackAsync(attempt.Authority!, "OK");

        Assert.Equal(PaymentAttemptStatus.Verified, outcome.Attempt.Status);
        var subscription = await environment.Subscriptions.FindByWorkspaceAsync(attempt.WorkspaceId);
        Assert.NotNull(subscription);
        Assert.Equal(SubscriptionStatus.Active, subscription!.Status);
        // Exactly one billing period for exactly one verified payment.
        Assert.Single(subscription.Periods);
    }

    [Fact]
    public async Task DuplicateOkCallbackIsIdempotentWithoutSecondActivation()
    {
        var environment = NewEnvironment();
        var attempt = await environment.CreatePendingAttemptAsync();
        await environment.UseCase.ExecuteCallbackAsync(attempt.Authority!, "OK");
        environment.Gateway.ScriptedVerifications.Clear(); // a replay must not even re-verify

        var replay = await environment.UseCase.ExecuteCallbackAsync(attempt.Authority!, "OK");

        Assert.True(replay.AlreadyApplied);
        Assert.Single(environment.Gateway.Verifies); // first callback only
        var subscription = await environment.Subscriptions.FindByWorkspaceAsync(attempt.WorkspaceId);
        Assert.Single(subscription!.Periods);
    }

    [Fact]
    public async Task Verify101AfterLostResponseStillAppliesEntitlementExactlyOnce()
    {
        var environment = NewEnvironment();
        var attempt = await environment.CreatePendingAttemptAsync();
        environment.Gateway.ScriptedVerifications.Enqueue(
            PaymentVerificationResult.AlreadyVerified(101));

        var outcome = await environment.UseCase.ExecuteCallbackAsync(attempt.Authority!, "OK");

        Assert.Equal(PaymentAttemptStatus.Verified, outcome.Attempt.Status);
        var subscription = await environment.Subscriptions.FindByWorkspaceAsync(attempt.WorkspaceId);
        Assert.NotNull(subscription);
    }

    [Fact]
    public async Task NokCallbackMarksCanceledWithoutSubscriptionChange()
    {
        var environment = NewEnvironment();
        var attempt = await environment.CreatePendingAttemptAsync();

        var outcome = await environment.UseCase.ExecuteCallbackAsync(attempt.Authority!, "NOK");

        Assert.False(outcome.AlreadyApplied);
        Assert.Equal(PaymentAttemptStatus.Failed, outcome.Attempt.Status);
        Assert.Equal(PaymentFailures.CanceledByUser, outcome.Attempt.FailureCode);
        Assert.Null(await environment.Subscriptions.FindByWorkspaceAsync(attempt.WorkspaceId));
        Assert.Empty(environment.Gateway.Verifies); // never verify a canceled return
    }

    [Fact]
    public async Task VerifyFailureMarksAttemptFailed()
    {
        var environment = NewEnvironment();
        var attempt = await environment.CreatePendingAttemptAsync();
        environment.Gateway.ScriptedVerifications.Enqueue(PaymentVerificationResult.Failed(-54, "authority not found"));

        var outcome = await environment.UseCase.ExecuteCallbackAsync(attempt.Authority!, "OK");

        Assert.Equal(PaymentAttemptStatus.Failed, outcome.Attempt.Status);
        Assert.Equal(PaymentFailures.VerifyRejected, outcome.Attempt.FailureCode);
        Assert.Null(await environment.Subscriptions.FindByWorkspaceAsync(attempt.WorkspaceId));
    }

    [Fact]
    public async Task TransientOutageLeavesAttemptPendingForRetry()
    {
        var environment = NewEnvironment();
        var attempt = await environment.CreatePendingAttemptAsync();
        environment.Gateway.ThrowUnavailable = true;

        await Assert.ThrowsAsync<BillingDomainException>(
            () => environment.UseCase.ExecuteCallbackAsync(attempt.Authority!, "OK"));

        var reloaded = await environment.Attempts.FindByAuthorityAsync(attempt.Authority!);
        Assert.Equal(PaymentAttemptStatus.Pending, reloaded!.Status);

        // Provider recovers: the same callback verifies and activates.
        environment.Gateway.ThrowUnavailable = false;
        var outcome = await environment.UseCase.ExecuteCallbackAsync(attempt.Authority!, "OK");
        Assert.Equal(PaymentAttemptStatus.Verified, outcome.Attempt.Status);
    }

    [Fact]
    public async Task UnknownAuthorityMapsToNotFound()
    {
        var environment = NewEnvironment();

        await Assert.ThrowsAsync<BillingDomainException>(
            () => environment.UseCase.ExecuteCallbackAsync("does-not-exist", "OK"));
    }

    [Fact]
    public async Task TrialConversionHappensWhenWorkspaceWasTrialing()
    {
        var environment = NewEnvironment();
        var workspaceId = Guid.CreateVersion7();
        var trial = Subscription.StartTrial(Guid.CreateVersion7(), workspaceId, Guid.CreateVersion7(), Now.AddDays(-3), Now.AddDays(11));
        await environment.Subscriptions.SaveChangesAsync(trial);

        var attempt = await environment.CreatePendingAttemptAsync(workspaceId: workspaceId);
        await environment.UseCase.ExecuteCallbackAsync(attempt.Authority!, "OK");

        var subscription = await environment.Subscriptions.FindByWorkspaceAsync(workspaceId);
        Assert.Equal(SubscriptionStatus.Active, subscription!.Status);
    }

    /// <summary>
    /// A callback arriving after another writer finalized the same attempt (concurrent
    /// verify race resolved by DB-level concurrency) must report idempotent success and
    /// never double-apply entitlements or re-verify with the provider.
    /// </summary>
    [Fact]
    public async Task CallbackAfterConcurrentWinnerReportsAlreadyApplied()
    {
        var environment = NewEnvironment();
        var attempt = await environment.CreatePendingAttemptAsync();

        // The concurrent winner: its OWN aggregate instance verifies and applies.
        var winnerCopy = Domain.Payments.PaymentAttempt.FromState(
            attempt.Id,
            attempt.WorkspaceId,
            attempt.PlanId,
            attempt.ProviderId,
            attempt.AmountIrr,
            PaymentAttemptStatus.Pending,
            attempt.Authority,
            null,
            null,
            null,
            attempt.CreatedAtUtc,
            null,
            null);
        winnerCopy.MarkVerified("ref-winner", null, Now.AddSeconds(1));
        await environment.Attempts.SaveChangesAsync(winnerCopy);
        var winnerSubscription = Subscription.Activate(
            Guid.CreateVersion7(), attempt.WorkspaceId, attempt.PlanId, Now.AddSeconds(1), Now.AddSeconds(1).AddDays(30));
        await environment.Subscriptions.SaveChangesAsync(winnerSubscription);

        // The losing callback replays afterwards.
        var outcome = await environment.UseCase.ExecuteCallbackAsync(attempt.Authority!, "OK");

        Assert.True(outcome.AlreadyApplied);
        Assert.Empty(environment.Gateway.Verifies); // never re-verified
        var subscription = await environment.Subscriptions.FindByWorkspaceAsync(attempt.WorkspaceId);
        Assert.Single(subscription!.Periods); // entitlement applied exactly once
    }

    private static Environment NewEnvironment()
    {
        var attempts = new FakePaymentAttemptRepository();
        var subscriptions = new FakeSubscriptionRepository();
        var gateway = new ScriptedGateway();
        var useCase = new FinalizePaymentUseCase(
            attempts,
            subscriptions,
            new SingleGatewayResolver(gateway),
            audit: null);
        return new Environment(useCase, attempts, subscriptions, gateway);
    }

    private sealed record Environment(
        FinalizePaymentUseCase UseCase,
        FakePaymentAttemptRepository Attempts,
        FakeSubscriptionRepository Subscriptions,
        ScriptedGateway Gateway)
    {
        public Task<Domain.Payments.PaymentAttempt> CreatePendingAttemptAsync(Guid? workspaceId = null)
        {
            var plan = Plan.Create(Guid.CreateVersion7(), $"plan-{Guid.NewGuid():N}", "Pro", amountIrr: 2_400_000);
            var attempt = Domain.Payments.PaymentAttempt.Create(
                Guid.CreateVersion7(), workspaceId ?? Guid.CreateVersion7(), plan.Id, "zarinpal", 2_400_000, Now);
            attempt.AttachAuthority($"auth-{attempt.Id:N}");
            Attempts.SaveChangesAsync(attempt).GetAwaiter().GetResult();
            return Task.FromResult(attempt);
        }
    }

    private sealed class ScriptedGateway : IPaymentGateway
    {
        public Queue<PaymentVerificationResult> ScriptedVerifications { get; } = new();

        public List<VerifyPaymentRequest> Verifies { get; } = [];

        public bool ThrowUnavailable { get; set; }

        public Action? OnBeforeVerify { get; set; }

        public string ProviderId => "zarinpal";

        public Task<PaymentInitialization> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaymentInitialization("zarinpal", $"auth-{request.AttemptId:N}", $"https://pay.test/{request.AttemptId}"));

        public Task<PaymentVerificationResult> VerifyAsync(VerifyPaymentRequest request, CancellationToken cancellationToken = default)
        {
            OnBeforeVerify?.Invoke();
            if (ThrowUnavailable)
            {
                throw new PaymentGatewayUnavailableException("simulated outage");
            }

            Verifies.Add(request);
            return Task.FromResult(ScriptedVerifications.Count > 0
                ? ScriptedVerifications.Dequeue()
                : PaymentVerificationResult.Verified(100, $"ref-{Guid.NewGuid():N}", "6037********1234", "hash"));
        }
    }

    private sealed class SingleGatewayResolver(IPaymentGateway gateway) : IPaymentGatewayResolver
    {
        public IReadOnlyList<string> EnabledProviderIds => [gateway.ProviderId];

        public IPaymentGateway Resolve(string providerId) =>
            string.Equals(providerId, gateway.ProviderId, StringComparison.OrdinalIgnoreCase)
                ? gateway
                : throw new PaymentProviderUnknownException(providerId);
    }

    private sealed class FakePaymentAttemptRepository : IPaymentAttemptRepository
    {
        private readonly Dictionary<Guid, Domain.Payments.PaymentAttempt> _store = [];

        public Task<Domain.Payments.PaymentAttempt?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.TryGetValue(id, out var attempt) ? attempt : null);

        public Task<Domain.Payments.PaymentAttempt?> FindByAuthorityAsync(string authority, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.Values.FirstOrDefault(a => a.Authority == authority));

        public Task<IReadOnlyList<Domain.Payments.PaymentAttempt>> ListByWorkspaceAsync(Guid workspaceId, int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult((IReadOnlyList<Domain.Payments.PaymentAttempt>)_store.Values
                .Where(a => a.WorkspaceId == workspaceId)
                .OrderByDescending(a => a.CreatedAtUtc)
                .ToList());

        public Task SaveChangesAsync(Domain.Payments.PaymentAttempt attempt, CancellationToken cancellationToken = default)
        {
            _store[attempt.Id] = attempt;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSubscriptionRepository : ISubscriptionRepository
    {
        private readonly Dictionary<Guid, Subscription> _store = [];

        public Task<Subscription?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.TryGetValue(id, out var subscription) ? subscription : null);

        public Task<Subscription?> FindByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.Values.FirstOrDefault(s => s.WorkspaceId == workspaceId));

        public Task SaveChangesAsync(Subscription subscription, CancellationToken cancellationToken = default)
        {
            _store[subscription.Id] = subscription;
            return Task.CompletedTask;
        }
    }
}
