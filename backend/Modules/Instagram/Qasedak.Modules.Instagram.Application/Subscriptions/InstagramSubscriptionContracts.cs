namespace Qasedak.Modules.Instagram.Application.Subscriptions;

/// <summary>
/// Single source of truth for Qasedak's required Instagram webhook fields
/// (Instagram Login path, current official contract). M13 scope needs exactly:
/// comment automation (comments/live_comments), inbox (messages), interactive
/// messaging (postbacks/seen). Handover/optins/referral/standby have no M13
/// consumer and stay unsubscribed; later tasks extend this one list.
/// </summary>
public static class InstagramSubscriptionFields
{
    public static readonly string[] Required =
    [
        "comments",
        "live_comments",
        "messages",
        "messaging_postbacks",
        "messaging_seen",
    ];
}

/// <summary>Outcome of one subscribe call: which fields Meta confirmed.</summary>
public sealed record SubscriptionResult(
    bool Success,
    IReadOnlyList<string> ConfirmedFields,
    string? FailureCode,
    bool Transient)
{
    public static SubscriptionResult Subscribed(IReadOnlyList<string> confirmedFields) =>
        new(true, confirmedFields, null, false);

    public static SubscriptionResult Failed(string failureCode, bool transient) =>
        new(false, [], failureCode, transient);
}

/// <summary>
/// Port to the professional-account webhook subscription edge. Uses the M13-003
/// transport; exposes Qasedak-owned concepts only. The edge is addressed by the
/// explicit professional account id per the verified contract
/// (<c>POST /{IG_ID}/subscribed_apps</c>); no read endpoint is assumed: the
/// official IG-Login contract documents subscribe (POST) but no field
/// inspection read, so verification means comparing confirmed outcomes against
/// <see cref="InstagramSubscriptionFields.Required"/>.
/// </summary>
public interface ISubscriptionClient
{
    /// <summary>Subscribes one professional account to the given fields.</summary>
    Task<SubscriptionResult> SubscribeAsync(
        string accessToken,
        string professionalAccountId,
        IReadOnlyList<string> fields,
        CancellationToken cancellationToken = default);
}

/// <summary>Stable failure codes for subscription operations.</summary>
public static class SubscriptionFailures
{
    public const string Unavailable = "subscription.unavailable";

    public const string PermissionDenied = "subscription.permissionDenied";
}
