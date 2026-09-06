namespace Qasedak.Modules.Instagram.Application.Effects;

/// <summary>Stable, bounded policy verdicts — never exceptions for normal provider-policy outcomes.</summary>
public enum PrivateReplyPolicyOutcome
{
    /// <summary>Locally provable as eligible; Meta remains the final enforcement authority.</summary>
    Eligible,

    /// <summary>
    /// Locally provable as outside the window: for non-Live comments, comment creation
    /// time (when authoritatively known) or the notification time is older than 7 days;
    /// for Live comments, the notification is older than any plausible active broadcast.
    /// </summary>
    DefinitelyExpired,

    /// <summary>The origin comment id is missing/empty — nothing to address the reply to.</summary>
    MissingRequiredOrigin,

    /// <summary>Meta answered that the comment reference no longer exists.</summary>
    CommentNotFound,
}

public sealed record PrivateReplyPolicyVerdict(PrivateReplyPolicyOutcome Outcome, string Reason)
{
    public static PrivateReplyPolicyVerdict Eligible(string reason) => new(PrivateReplyPolicyOutcome.Eligible, reason);

    public static PrivateReplyPolicyVerdict Reject(PrivateReplyPolicyOutcome outcome, string reason) => new(outcome, reason);
}

public sealed record PrivateReplyPolicyInput(
    string? CommentId,
    bool IsLiveComment,
    DateTimeOffset? ExactCommentCreatedAtUtc,
    DateTimeOffset NotificationOccurredAtUtc,
    DateTimeOffset NowUtc);

/// <summary>
/// Deterministic Private Reply eligibility (M13-009). The 7-day window is measured from
/// COMMENT CREATION TIME per the current official contract — NOT from the webhook
/// notification time (entry.time only proves Meta sent the notification when it did).
///
/// Enforcement is only what is locally provable:
/// - exact comment creation time (official IG Comment read) → exact 7-day math;
/// - otherwise the notification time is used as a one-sided guard: notification older
///   than 7 days ⇒ the comment is definitely older too ⇒ reject; a younger notification
///   does NOT prove the comment is younger — Meta stays the final authority there.
/// - Live comments NEVER use the 7-day rule (replies are only valid during the live
///   broadcast). Live is attempted once immediately and Meta decides; a notification
///   older than 7 days cannot belong to an active broadcast and is rejected.
/// - future provider timestamps (clock skew) cannot prove expiry and are treated as
///   eligible — Meta remains the enforcement authority.
/// </summary>
public static class PrivateReplyPolicy
{
    /// <summary>Official Private Reply window for posts, reels, stories and ads.</summary>
    public static readonly TimeSpan CommentWindow = TimeSpan.FromDays(7);

    public static PrivateReplyPolicyVerdict Evaluate(PrivateReplyPolicyInput input)
    {
        if (string.IsNullOrWhiteSpace(input.CommentId))
        {
            return PrivateReplyPolicyVerdict.Reject(PrivateReplyPolicyOutcome.MissingRequiredOrigin, "comment id required for comment-id addressing");
        }

        var notificationAge = input.NowUtc - input.NotificationOccurredAtUtc;

        if (input.IsLiveComment)
        {
            // Live policy: only during the broadcast; the 7-day comment rule NEVER
            // applies. A notification older than the window cannot be an active broadcast.
            return notificationAge > CommentWindow
                ? PrivateReplyPolicyVerdict.Reject(PrivateReplyPolicyOutcome.DefinitelyExpired, "live notification older than the broadcast window")
                : PrivateReplyPolicyVerdict.Eligible("live comment; provider broadcast state is the final authority");
        }

        if (input.ExactCommentCreatedAtUtc is { } createdAt)
        {
            // Authoritative comment creation time from the official IG Comment read.
            return input.NowUtc - createdAt > CommentWindow
                ? PrivateReplyPolicyVerdict.Reject(PrivateReplyPolicyOutcome.DefinitelyExpired, "comment creation time older than 7 days")
                : PrivateReplyPolicyVerdict.Eligible("comment creation time within 7 days");
        }

        // One-sided notification guard: proves expiry when old, proves nothing when young.
        return notificationAge > CommentWindow
            ? PrivateReplyPolicyVerdict.Reject(PrivateReplyPolicyOutcome.DefinitelyExpired, "notification older than 7 days (one-sided guard)")
            : PrivateReplyPolicyVerdict.Eligible("notification within 7 days; exact comment age unknown — provider is the final authority");
    }
}
