namespace Qasedak.Modules.Instagram.Application.RevealFlow;

/// <summary>Provider relationship read result (tri-state; errors never collapse into false).</summary>
public sealed record FollowStateResult(FollowState State, FollowStateUnavailableReason? UnavailableReason = null)
{
    public static FollowStateResult Follows() => new(FollowState.Follows);

    public static FollowStateResult DoesNotFollow() => new(FollowState.DoesNotFollow);

    public static FollowStateResult Unavailable(FollowStateUnavailableReason reason) =>
        new(FollowState.UnknownUnavailable, reason);
}

/// <summary>
/// Focused port for the official User Profile relationship field
/// GET {graph}/{version}/{IGSID}?fields=is_user_follow_business (Instagram API with
/// Instagram Login; permissions instagram_business_basic + instagram_business_manage_messages).
/// The provider boolean is authoritative: a missing/error payload yields
/// UnknownUnavailable, never false. Never called without a proven messaging-consent basis
/// (M13-011 fail-closed rule).
/// </summary>
public interface IInstagramRelationshipClient
{
    Task<FollowStateResult> GetFollowStateAsync(
        string accessToken,
        string participantIGSId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Follow-gate capability policy (M13-011). The capability is decided by current contract
/// and configuration — never by probing the provider. <see cref="FollowGateMode.Disabled"/>
/// keeps the provider-independent postback/reveal core working; when enabled, only a
/// Follows verdict allows the reveal to proceed — DoesNotFollow and UnknownUnavailable
/// both hold the flow (fail closed), never fabricating false.
/// </summary>
public static class FollowGatePolicy
{
    public enum Verdict
    {
        /// <summary>Gate disabled or participant confirmed following — reveal may proceed.</summary>
        Proceed,

        /// <summary>Participant confirmed not following — hold; the same button may be tapped again later.</summary>
        Blocked,

        /// <summary>No authoritative data (consent/permission/transient/malformed) — hold; future postback re-checks.</summary>
        CapabilityUnavailable,
    }

    public static Verdict Evaluate(FollowGateMode mode, FollowStateResult? relationship)
    {
        if (mode == FollowGateMode.Disabled)
        {
            return Verdict.Proceed;
        }

        return relationship?.State switch
        {
            FollowState.Follows => Verdict.Proceed,
            FollowState.DoesNotFollow => Verdict.Blocked,
            _ => Verdict.CapabilityUnavailable,
        };
    }
}
