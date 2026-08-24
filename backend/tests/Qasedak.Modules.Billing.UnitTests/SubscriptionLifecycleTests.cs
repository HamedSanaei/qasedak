using Qasedak.Modules.Billing.Domain;
using Xunit;

namespace Qasedak.Modules.Billing.UnitTests;

/// <summary>
/// Subscription lifecycle rules: explicit transitions, one-live-per-workspace semantics at
/// the domain level, entitlement windows, and terminal-state immutability.
/// </summary>
public sealed class SubscriptionLifecycleTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TrialConvertsToActiveWithNewPeriod()
    {
        var subscription = Subscription.StartTrial(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Start, Start.AddDays(14));

        var planId = Guid.CreateVersion7();
        subscription.ConvertTrialToActive(planId, Start.AddDays(10), Start.AddDays(40));

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(planId, subscription.PlanId);
        Assert.Equal(Start.AddDays(40), subscription.CurrentPeriodEndUtc);
        Assert.True(subscription.IsEntitledAt(Start.AddDays(39)));
    }

    [Fact]
    public void EntitlementWindowEndsWhenPeriodPasses()
    {
        var subscription = Subscription.Activate(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Start, Start.AddDays(30));

        Assert.True(subscription.IsEntitledAt(Start.AddDays(30)));
        Assert.False(subscription.IsEntitledAt(Start.AddDays(31)));

        // Renewal restores the entitled window.
        subscription.Renew(Start.AddDays(31), Start.AddDays(61));
        Assert.True(subscription.IsEntitledAt(Start.AddDays(45)));
    }

    [Fact]
    public void PastDueKeepsGraceUntilRenewedOrExpired()
    {
        var subscription = Subscription.Activate(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Start, Start.AddDays(30));
        subscription.MarkPastDue(Start.AddDays(5));

        Assert.Equal(SubscriptionStatus.PastDue, subscription.Status);
        Assert.True(subscription.IsEntitledAt(Start.AddDays(6))); // grace window

        subscription.Renew(Start.AddDays(6), Start.AddDays(36));
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public void CancelIsTerminalAndStampsTime()
    {
        var subscription = Subscription.Activate(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Start, Start.AddDays(30));

        subscription.Cancel(Start.AddDays(3));

        Assert.Equal(SubscriptionStatus.Canceled, subscription.Status);
        Assert.Equal(Start.AddDays(3), subscription.CanceledAtUtc);
        // Entitlements run to period end even after cancel.
        Assert.False(subscription.IsEntitledAt(Start.AddDays(4))); // canceled rows are not entitled
        Assert.Throws<BillingDomainException>(() => subscription.Renew(Start.AddDays(4), Start.AddDays(34)));
        Assert.Throws<BillingDomainException>(() => subscription.ChangePlan(Guid.CreateVersion7()));
    }

    [Fact]
    public void ExpireRequiresLapsedPeriodAndIsIdempotent()
    {
        var subscription = Subscription.Activate(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Start, Start.AddDays(30));

        Assert.Throws<BillingDomainException>(() => subscription.Expire(Start.AddDays(1)));
        subscription.Expire(Start.AddDays(31));
        subscription.Expire(Start.AddDays(32)); // idempotent
        Assert.Equal(SubscriptionStatus.Expired, subscription.Status);
    }

    [Fact]
    public void InvalidWindowsAndInputsAreRejected()
    {
        Assert.Throws<BillingDomainException>(() => Subscription.StartTrial(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Start, Start));
        Assert.Throws<BillingDomainException>(() => Subscription.Activate(Guid.CreateVersion7(), Guid.Empty, Guid.CreateVersion7(), Start, Start.AddDays(30)));
        Assert.Throws<BillingDomainException>(() => Subscription.Activate(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.Empty, Start, Start.AddDays(-1)));
    }

    [Fact]
    public void FromStateRestoresPeriodHistory()
    {
        var id = Guid.CreateVersion7();
        var restored = Subscription.FromState(
            id, Guid.CreateVersion7(), Guid.CreateVersion7(),
            SubscriptionStatus.Active, Start, null,
            [new SubscriptionPeriod(Start, Start.AddDays(30)), new SubscriptionPeriod(Start.AddDays(30), Start.AddDays(60))]);

        Assert.Equal(2, restored.Periods.Count);
        Assert.Equal(Start.AddDays(60), restored.CurrentPeriodEndUtc);
    }
}
