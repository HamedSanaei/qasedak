using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Billing.Application;
using Qasedak.Modules.Billing.Domain;
using Qasedak.Modules.Billing.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Modules.Billing.IntegrationTests;

/// <summary>
/// Billing persistence over real PostgreSQL: plan catalog round-trips, the
/// one-live-subscription-per-workspace unique backstop, period-history append semantics,
/// and server-owned entitlement resolution (fail-closed).
/// </summary>
[Collection(PostgresTestEnvironment.Name)]
public sealed class BillingPersistenceTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 3, 5, 0, 0, 0, TimeSpan.Zero);

    private (BillingDbContext Context, EfPlanRepository Plans, EfSubscriptionRepository Subscriptions) NewScope()
    {
        var context = new BillingDbContext(new DbContextOptionsBuilder<BillingDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options);
        return (context, new EfPlanRepository(context), new EfSubscriptionRepository(context));
    }

    [Fact]
    public async Task PlanRoundTripsWithEntitlementsAndUniqueCode()
    {
        var (_, plans, _) = NewScope();
        var plan = Plan.Create(Guid.CreateVersion7(), "Pro", "Pro Plan",
        [
            Entitlement.Of("automations.active", 5),
            Entitlement.Of("contacts.total", -1),
            Entitlement.Of("inbox.ai-replies", 0),
        ]);
        await plans.SaveChangesAsync(plan);

        var reloaded = await NewScope().Plans.FindByCodeAsync("PRO");
        Assert.NotNull(reloaded);
        Assert.Equal("pro", reloaded!.Code);
        Assert.Equal(-1, reloaded.EntitlementFor("contacts.total").Limit);
        Assert.False(reloaded.EntitlementFor("INBOX.AI-REPLIES").IsEnabled);

        // Duplicate codes violate the unique index.
        var impostor = Plan.Create(Guid.CreateVersion7(), "pro", "Impostor Pro");
        await Assert.ThrowsAnyAsync<Exception>(() => NewScope().Plans.SaveChangesAsync(impostor));
    }

    [Fact]
    public async Task WorkspaceHasAtMostOneSubscriptionRow()
    {
        var workspaceId = Guid.CreateVersion7();
        var planId = Guid.CreateVersion7();
        var plan = Plan.Create(planId, "starter", "Starter");
        await NewScope().Plans.SaveChangesAsync(plan);

        var subscription = Subscription.Activate(Guid.CreateVersion7(), workspaceId, planId, Now, Now.AddDays(30));
        await NewScope().Subscriptions.SaveChangesAsync(subscription);

        // The unique index on WorkspaceId rejects a second row outright.
        var second = Subscription.Activate(Guid.CreateVersion7(), workspaceId, planId, Now.AddDays(1), Now.AddDays(31));
        await Assert.ThrowsAnyAsync<Exception>(() => NewScope().Subscriptions.SaveChangesAsync(second));

        // Terminating the existing row keeps its identity; no resurrection via new rows.
        var found = await NewScope().Subscriptions.FindByWorkspaceAsync(workspaceId);
        Assert.NotNull(found);
        Assert.Equal(subscription.Id, found!.Id);
    }

    [Fact]
    public async Task PeriodHistoryAppendsAcrossReloads()
    {
        var workspaceId = Guid.CreateVersion7();
        var subscription = Subscription.Activate(Guid.CreateVersion7(), workspaceId, Guid.CreateVersion7(), Now, Now.AddDays(30));
        await NewScope().Subscriptions.SaveChangesAsync(subscription);

        var (context2, _, subscriptions2) = NewScope();
        var loaded = (await subscriptions2.FindByWorkspaceAsync(workspaceId))!;
        loaded.Renew(Now.AddDays(30), Now.AddDays(60));
        await subscriptions2.SaveChangesAsync(loaded);
        await context2.DisposeAsync();

        var final = (await NewScope().Subscriptions.FindByWorkspaceAsync(workspaceId))!;
        Assert.Equal(2, final.Periods.Count);
        Assert.Equal(Now.AddDays(60), final.CurrentPeriodEndUtc);
    }

    [Fact]
    public async Task EntitlementResolutionFailsClosedWithoutSubscriptionOrPlan()
    {
        var planId = Guid.CreateVersion7();
        await NewScope().Plans.SaveChangesAsync(Plan.Create(planId, "team", "Team",
        [
            Entitlement.Of("automations.active", 3),
            Entitlement.Of("contacts.total", -1),
        ]));

        var resolver = new ResolveWorkspaceEntitlementsUseCase(NewScope().Subscriptions, NewScope().Plans);

        // No subscription → not entitled.
        var none = await resolver.ExecuteAsync(Guid.CreateVersion7(), Now);
        Assert.False(none.Entitled);

        // Live subscription → plan limits surface verbatim.
        var workspaceId = Guid.CreateVersion7();
        await NewScope().Subscriptions.SaveChangesAsync(
            Subscription.Activate(Guid.CreateVersion7(), workspaceId, planId, Now, Now.AddDays(30)));
        var entitled = await resolver.ExecuteAsync(workspaceId, Now.AddDays(1));
        Assert.True(entitled.Entitled);
        Assert.Equal(3, entitled.Limits["automations.active"]);
        Assert.Equal(-1, entitled.Limits["contacts.total"]);

        // After expiry → not entitled.
        var expired = await resolver.ExecuteAsync(workspaceId, Now.AddDays(31));
        Assert.False(expired.Entitled);

        // A live subscription pointing at a deleted plan fails CLOSED.
        var orphanWorkspace = Guid.CreateVersion7();
        await NewScope().Subscriptions.SaveChangesAsync(
            Subscription.Activate(Guid.CreateVersion7(), orphanWorkspace, Guid.CreateVersion7(), Now, Now.AddDays(30)));
        var orphan = await resolver.ExecuteAsync(orphanWorkspace, Now.AddDays(1));
        Assert.False(orphan.Entitled);
    }
}
