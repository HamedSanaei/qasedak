using Qasedak.Modules.Billing.Domain;
using Xunit;

namespace Qasedak.Modules.Billing.UnitTests;

/// <summary>Plan catalog rules: codes normalize, entitlements converge, lookups fail closed.</summary>
public sealed class PlanTests
{
    [Fact]
    public void CodesNormalizeToLowercase()
    {
        var plan = Plan.Create(Guid.CreateVersion7(), "  Pro-Yearly  ", "Pro Yearly");
        Assert.Equal("pro-yearly", plan.Code);
    }

    [Fact]
    public void ReGrantingAFeatureReplacesItsLimit()
    {
        var plan = Plan.Create(Guid.CreateVersion7(), "pro", "Pro", [Entitlement.Of("automations.active", 3)]);
        plan.AddEntitlement(Entitlement.Of("automations.active", 10));

        Assert.Single(plan.Entitlements);
        Assert.Equal(10, plan.EntitlementFor("AUTOMATIONS.ACTIVE").Limit);
    }

    [Fact]
    public void UnknownFeaturesFailClosedAsDisabled()
    {
        var plan = Plan.Create(Guid.CreateVersion7(), "free", "Free");
        var entitlement = plan.EntitlementFor("inbox.ai-replies");

        Assert.False(entitlement.IsEnabled);
        Assert.False(entitlement.IsUnlimited);
    }

    [Fact]
    public void UnlimitedAndInvalidLimits()
    {
        Assert.True(Entitlement.Of("contacts.total", -1).IsUnlimited);
        Assert.Throws<BillingDomainException>(() => Entitlement.Of("contacts.total", -2));
        Assert.Throws<BillingDomainException>(() => Entitlement.Of(" ", 5));
    }
}
