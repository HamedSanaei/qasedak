namespace Qasedak.Modules.Instagram.Application.Effects;

/// <summary>Structured reasons for a rejected/failed public comment reply send.</summary>
public enum PublicReplyFailureReason
{
    /// <summary>Network-level failure; the request never reached Meta or no answer arrived.</summary>
    TransportFailure,

    /// <summary>Meta answered with an error payload (hidden comment, live video, permission, …).</summary>
    RejectedByMeta,

    /// <summary>Meta answered 2xx but the payload did not match the documented success shape.</summary>
    MalformedResponse,
}

public sealed record PublicReplyFailure(PublicReplyFailureReason Reason, string Detail);

/// <summary>
/// Result of one public comment reply provider call. <see cref="ReplyCommentId"/> is the
/// official success identity (the created reply's IG Comment id) and is persisted to the
/// effect ledger. Token material never appears here.
/// </summary>
public sealed record PublicReplySendResult(bool Succeeded, PublicReplyFailure? Failure, string? ReplyCommentId)
{
    public static PublicReplySendResult Ok(string replyCommentId) => new(true, null, replyCommentId);

    public static PublicReplySendResult Fail(PublicReplyFailureReason reason, string detail) => new(false, new PublicReplyFailure(reason, detail), null);
}

/// <summary>
/// Instagram Login public comment reply adapter contract (current official shape):
/// POST {graph}/{version}/{comment_id}/replies with parameter message={text}, Bearer IG
/// User token, instagram_business_basic + instagram_business_manage_comments.
/// Distinct from Private Reply — no messaging recipient object is ever sent.
/// </summary>
public interface ICommentPublicReplyClient
{
    Task<PublicReplySendResult> SendPublicReplyAsync(
        string accessToken,
        string commentId,
        string text,
        CancellationToken cancellationToken = default);
}
