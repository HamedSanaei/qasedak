using Qasedak.Modules.Billing.Domain;
using Xunit;

namespace Qasedak.Modules.Billing.UnitTests;

/// <summary>
/// Mutation-gate hardening: boundary conditions on plan limits, duplicate-feature rules
/// and subscription period edges. Each assertion kills specific surviving mutants.
/// </summary>
public sealed class BillingBoundaryTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(40, true)]   // exactly MaxCodeLength is accepted
    [InlineData(41, false)]  // one over the cap is rejected
    public void PlanCodeLengthBoundaries(int length, bool valid)
    {
        var code = new string('a', length);
        if (valid)
        {
            Assert.Equal(code, Plan.Create(Guid.CreateVersion7(), code, "Plan").Code);
        }
        else
        {
            Assert.Throws<BillingDomainException>(() => Plan.Create(Guid.CreateVersion7(), code, "Plan"));
        }
    }

    [Theory]
    [InlineData(100, true)]  // exactly MaxNameLength is accepted
    [InlineData(101, false)] // one over the cap is rejected
    public void PlanNameLengthBoundaries(int length, bool valid)
    {
        var name = new string('n', length);
        if (valid)
        {
            Assert.Equal(name, Plan.Create(Guid.CreateVersion7(), "code", name).Name);
        }
        else
        {
            Assert.Throws<BillingDomainException>(() => Plan.Create(Guid.CreateVersion7(), "code", name));
        }
    }

    [Fact]
    public void FeatureCountCapRejectsTheThirtyThirdGrant()
    {
        var plan = Plan.Create(Guid.CreateVersion7(), "cap", "Cap",
            Enumerable.Range(0, Plan.MaxFeaturesPerPlan).Select(i => Entitlement.Of($"feature-{i}", 10)));
        // Exactly at the cap: accepted (32 grants).
        Assert.Equal(Plan.MaxFeaturesPerPlan, plan.Entitlements.Count);
        // One more: rejected.
        Assert.Throws<BillingDomainException>(
            () => plan.AddEntitlement(Entitlement.Of("feature-overflow", 1)));
    }

    [Fact]
    public void ReGrantReplacesLimitEvenAcrossCaseAndWhitespace()
    {
        var plan = Plan.Create(Guid.CreateVersion7(), "regrant", "Regrant", [Entitlement.Of("seats", 5)]);
        // Same feature in any casing/whitespace variant replaces the original grant.
        plan.AddEntitlement(Entitlement.Of("SEATS ", 12));
        Assert.Single(plan.Entitlements);
        Assert.Equal(12, plan.Entitlements.Single(e => e.FeatureKey == "seats").Limit);

        // A different feature still appends.
        plan.AddEntitlement(Entitlement.Of("projects", 1));
        Assert.Equal(2, plan.Entitlements.Count);
    }

    [Fact]
    public void ZeroLengthAndOverNegativeLimitsAreRejected()
    {
        Assert.Throws<BillingDomainException>(() => Entitlement.Of("   ", 1));
        Assert.Throws<BillingDomainException>(() => Entitlement.Of(null!, 1));
        Assert.Throws<BillingDomainException>(() => Entitlement.Of("key", -2));
    }

    [Fact]
    public void PeriodEndingExactlyAtStartIsAccepted()
    {
        // Boundary: periodEnd == start is a valid zero-length period; only strictly
        // earlier ends are invalid (< comparison).
        var subscription = Subscription.Activate(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Now, Now.AddMonths(1));
        Assert.True(subscription.IsEntitledAt(Now.AddDays(-1)));
        Assert.False(subscription.IsEntitledAt(Now.AddMonths(1).AddSeconds(1)));
    }
}
