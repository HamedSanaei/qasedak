using Qasedak.Modules.Billing.Application;
using Qasedak.Modules.Billing.Domain;
using Xunit;

namespace Qasedak.Modules.Billing.UnitTests;

/// <summary>
/// Server-side entitlement gate semantics over fakes: fail-closed by default, unlimited
/// passes, count caps deny at the limit, disabled features deny everything.
/// </summary>
public sealed class EntitlementGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 0, 0, 0, TimeSpan.Zero);

    private sealed class FakeSubscriptions(Guid workspaceId, Guid planId, DateTimeOffset periodEnd) : ISubscriptionRepository
    {
        private readonly Subscription? _live =
            Subscription.Activate(Guid.CreateVersion7(), workspaceId, planId, periodEnd.AddDays(-31), periodEnd);

        public Task<Subscription?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Subscription?>(_live is not null && _live.Id == id ? _live : null);

        public Task<Subscription?> FindByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_live is not null && _live.WorkspaceId == workspaceId ? _live : null);

        public Task SaveChangesAsync(Subscription subscription, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public static ISubscriptionRepository None() => new NoneRepo();

        private sealed class NoneRepo : ISubscriptionRepository
        {
            public Task<Subscription?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Subscription?>(null);

            public Task<Subscription?> FindByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult<Subscription?>(null);

            public Task SaveChangesAsync(Subscription subscription, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }
    }

    private sealed class FakePlans(params Plan[] plans) : IPlanRepository
    {
        public Task<Plan?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(plans.FirstOrDefault(p => p.Id == id));

        public Task<Plan?> FindByCodeAsync(string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(plans.FirstOrDefault(p => p.Code == code));

        public Task<IReadOnlyList<Plan>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Plan>>(plans);

        public Task SaveChangesAsync(Plan plan, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task WithoutSubscriptionEverythingIsDenied()
    {
        var gate = new EntitlementGate(FakeSubscriptions.None(), new FakePlans());

        var decision = await gate.RequireEntitledAsync(Guid.CreateVersion7(), Now);
        Assert.False(decision.Allowed);
        Assert.Equal(EntitlementDecision.SubscriptionRequiredCode, decision.DenialCode);
    }

    [Fact]
    public async Task CountLimitsDenyAtTheCapAndUnlimitedPasses()
    {
        var planId = Guid.CreateVersion7();
        var workspaceId = Guid.CreateVersion7();
        var plan = Plan.Create(planId, "pro", "Pro",
        [
            Entitlement.Of("automations.active", 2),
            Entitlement.Of("contacts.total", -1),
            Entitlement.Of("inbox.ai-replies", 0),
        ]);
        var gate = new EntitlementGate(new FakeSubscriptions(workspaceId, planId, Now.AddDays(30)), new FakePlans(plan));

        Assert.True((await gate.CheckCountLimitAsync(workspaceId, "automations.active", 1, Now)).Allowed);
        var denied = await gate.CheckCountLimitAsync(workspaceId, "automations.active", 2, Now);
        Assert.False(denied.Allowed);
        Assert.Equal(EntitlementDecision.LimitExceededCode, denied.DenialCode);
        Assert.Equal(2, denied.Limit);

        Assert.True((await gate.CheckCountLimitAsync(workspaceId, "contacts.total", 999_999, Now)).Allowed);

        var disabled = await gate.CheckCountLimitAsync(workspaceId, "inbox.ai-replies", 0, Now);
        Assert.False(disabled.Allowed);
        Assert.Equal(0, disabled.Limit);
    }

    [Fact]
    public async Task ExpiredPeriodsFailClosed()
    {
        var planId = Guid.CreateVersion7();
        var workspaceId = Guid.CreateVersion7();
        var plan = Plan.Create(planId, "starter", "Starter", [Entitlement.Of("automations.active", -1)]);
        var subscription = Subscription.Activate(Guid.CreateVersion7(), workspaceId, planId, Now.AddDays(-10), Now);
        var gate = new EntitlementGate(new LiveAfter(subscription), new FakePlans(plan));

        Assert.False((await gate.RequireEntitledAsync(workspaceId, Now.AddDays(1))).Allowed);
    }

    private sealed class LiveAfter(Subscription subscription) : ISubscriptionRepository
    {
        public Task<Subscription?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Subscription?>(subscription.Id == id ? subscription : null);

        public Task<Subscription?> FindByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Subscription?>(subscription.WorkspaceId == workspaceId ? subscription : null);

        public Task SaveChangesAsync(Subscription subscription, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
