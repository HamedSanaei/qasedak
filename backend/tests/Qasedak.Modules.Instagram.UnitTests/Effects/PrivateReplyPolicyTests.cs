using Qasedak.Modules.Instagram.Application.Effects;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests.Effects;

/// <summary>
/// Deterministic Private Reply eligibility matrix (M13-009 §78). The window is measured
/// from COMMENT CREATION TIME; the webhook notification time is only a one-sided guard.
/// Live comments never use the 7-day rule. Boundary interpretation: exactly 7 days is
/// still eligible (window is "within 7 days"; strictly older is expired).
/// </summary>
public sealed class PrivateReplyPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static PrivateReplyPolicyVerdict Evaluate(
        string? commentId = "comment-1",
        bool isLive = false,
        DateTimeOffset? exactCreatedAt = null,
        DateTimeOffset? notificationAt = null)
        => PrivateReplyPolicy.Evaluate(new PrivateReplyPolicyInput(
            commentId,
            isLive,
            exactCreatedAt,
            notificationAt ?? Now,
            Now));

    [Fact]
    public void OrdinaryCommentWithinSevenDaysIsEligible()
    {
        var verdict = Evaluate(exactCreatedAt: Now.AddDays(-1));

        Assert.Equal(PrivateReplyPolicyOutcome.Eligible, verdict.Outcome);
    }

    [Fact]
    public void CommentCreatedJustUnderSevenDaysAgoIsEligible()
    {
        var verdict = Evaluate(exactCreatedAt: Now.AddDays(-7).AddSeconds(1));

        Assert.Equal(PrivateReplyPolicyOutcome.Eligible, verdict.Outcome);
    }

    [Fact]
    public void CommentCreatedExactlySevenDaysAgoIsStillEligible()
    {
        // "Within 7 days": the boundary instant itself is not yet outside the window.
        var verdict = Evaluate(exactCreatedAt: Now.AddDays(-7));

        Assert.Equal(PrivateReplyPolicyOutcome.Eligible, verdict.Outcome);
    }

    [Fact]
    public void CommentOlderThanSevenDaysIsDefinitelyExpired()
    {
        var verdict = Evaluate(exactCreatedAt: Now.AddDays(-7).AddSeconds(-1));

        Assert.Equal(PrivateReplyPolicyOutcome.DefinitelyExpired, verdict.Outcome);
    }

    [Fact]
    public void FutureCommentTimestampCannotProveExpiryAndIsEligible()
    {
        var verdict = Evaluate(exactCreatedAt: Now.AddHours(2));

        Assert.Equal(PrivateReplyPolicyOutcome.Eligible, verdict.Outcome);
    }

    [Fact]
    public void FutureNotificationTimeCannotProveExpiryAndIsEligible()
    {
        var verdict = Evaluate(exactCreatedAt: null, notificationAt: Now.AddHours(1));

        Assert.Equal(PrivateReplyPolicyOutcome.Eligible, verdict.Outcome);
    }

    [Fact]
    public void NotificationOlderThanSevenDaysIsDefinitelyExpiredWhenExactTimeUnknown()
    {
        // One-sided guard: the notification is strictly older than the window, so the
        // comment (created no later than the notification) is definitely expired too.
        var verdict = Evaluate(exactCreatedAt: null, notificationAt: Now.AddDays(-8));

        Assert.Equal(PrivateReplyPolicyOutcome.DefinitelyExpired, verdict.Outcome);
    }

    [Fact]
    public void RecentNotificationWithUnknownCommentAgeIsEligibleByGuardOnly()
    {
        // A recent notification does NOT prove the comment is younger than 7 days —
        // Meta stays the final authority; the local verdict is only "not provably expired".
        var verdict = Evaluate(exactCreatedAt: null, notificationAt: Now.AddDays(-2));

        Assert.Equal(PrivateReplyPolicyOutcome.Eligible, verdict.Outcome);
    }

    [Fact]
    public void LiveCommentNeverUsesTheSevenDayRule()
    {
        // A live comment whose creation time is inside 7 days must NOT be treated as a
        // regular 7-day comment — eligibility is the broadcast, decided by Meta.
        var verdict = Evaluate(isLive: true, exactCreatedAt: Now.AddDays(-6));

        Assert.Equal(PrivateReplyPolicyOutcome.Eligible, verdict.Outcome);
    }

    [Fact]
    public void LiveCommentWithUnknownCreationTimeIsEligibleByGuard()
    {
        // Unverifiable live state (e.g. the reference read is unavailable): the immediate
        // durable-processing attempt is allowed; Meta's broadcast check is the final
        // authority. Never retried afterwards.
        var verdict = Evaluate(isLive: true, exactCreatedAt: null, notificationAt: Now.AddHours(-3));

        Assert.Equal(PrivateReplyPolicyOutcome.Eligible, verdict.Outcome);
    }

    [Fact]
    public void LiveNotificationOlderThanTheWindowIsDefinitelyNotAnActiveBroadcast()
    {
        var verdict = Evaluate(isLive: true, exactCreatedAt: Now.AddDays(-10), notificationAt: Now.AddDays(-10));

        Assert.Equal(PrivateReplyPolicyOutcome.DefinitelyExpired, verdict.Outcome);
    }

    [Fact]
    public void MissingCommentIdIsRejected()
    {
        var verdict = Evaluate(commentId: null);

        Assert.Equal(PrivateReplyPolicyOutcome.MissingRequiredOrigin, verdict.Outcome);
    }

    [Fact]
    public void BlankCommentIdIsRejected()
    {
        var verdict = Evaluate(commentId: "   ");

        Assert.Equal(PrivateReplyPolicyOutcome.MissingRequiredOrigin, verdict.Outcome);
    }

    [Fact]
    public void NotificationTimeIsNeverTreatedAsExactCreationTime()
    {
        // The webhook notification time (entry.time) is the time Meta sent the
        // notification, NOT comment creation time. The exact-time branch is only taken
        // when the official IG Comment read supplied the creation timestamp.
        var verdict = Evaluate(exactCreatedAt: Now.AddDays(-6), notificationAt: Now.AddHours(-1));

        Assert.Equal(PrivateReplyPolicyOutcome.Eligible, verdict.Outcome);
    }
}
