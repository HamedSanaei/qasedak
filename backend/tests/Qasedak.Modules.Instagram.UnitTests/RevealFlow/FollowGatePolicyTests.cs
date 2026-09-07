using Qasedak.Modules.Instagram.Application.RevealFlow;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests.RevealFlow;

public sealed class FollowGatePolicyTests
{
    [Fact]
    public void DisabledModeAlwaysProceedsWithoutRelationshipData()
    {
        Assert.Equal(FollowGatePolicy.Verdict.Proceed, FollowGatePolicy.Evaluate(FollowGateMode.Disabled, null));
    }

    [Fact]
    public void EnabledModeProceedsOnlyOnConfirmedFollow()
    {
        Assert.Equal(FollowGatePolicy.Verdict.Proceed, FollowGatePolicy.Evaluate(FollowGateMode.EnabledWhenSupported, FollowStateResult.Follows()));
        Assert.Equal(FollowGatePolicy.Verdict.Blocked, FollowGatePolicy.Evaluate(FollowGateMode.EnabledWhenSupported, FollowStateResult.DoesNotFollow()));
    }

    [Theory]
    [InlineData(FollowStateUnavailableReason.ConsentUnavailable)]
    [InlineData(FollowStateUnavailableReason.PermissionUnavailable)]
    [InlineData(FollowStateUnavailableReason.Transient)]
    [InlineData(FollowStateUnavailableReason.Malformed)]
    public void EnabledModeNeverFabricatesFalseFromMissingData(FollowStateUnavailableReason reason)
    {
        // UnknownUnavailable must NOT become DoesNotFollow: no reveal, no new prompt, hold.
        Assert.Equal(FollowGatePolicy.Verdict.CapabilityUnavailable,
            FollowGatePolicy.Evaluate(FollowGateMode.EnabledWhenSupported, FollowStateResult.Unavailable(reason)));
    }
}
