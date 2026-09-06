namespace Qasedak.Modules.Instagram.Application.Effects;

/// <summary>Structured reasons for a rejected/failed comment Private Reply send.</summary>
public enum PrivateReplyFailureReason
{
    /// <summary>Network-level failure; the request never reached Meta or no answer arrived.</summary>
    TransportFailure,

    /// <summary>Meta answered with an error payload (permission, expired, one-reply rule, …).</summary>
    RejectedByMeta,

    /// <summary>Meta answered 2xx but the payload did not match the documented success shape.</summary>
    MalformedResponse,
}

public sealed record PrivateReplyFailure(PrivateReplyFailureReason Reason, string Detail);

/// <summary>
/// Result of one Private Reply provider call. <see cref="RecipientId"/> and
/// <see cref="MessageId"/> are the official success identity (recipient_id / message_id)
/// and are persisted to the effect ledger so a crash after provider success can be
/// reconciled without a second mutation. Token material never appears here.
/// </summary>
public sealed record PrivateReplySendResult(
    bool Succeeded,
    PrivateReplyFailure? Failure,
    string? RecipientId,
    string? MessageId)
{
    public static PrivateReplySendResult Ok(string recipientId, string messageId) => new(true, null, recipientId, messageId);

    public static PrivateReplySendResult Fail(PrivateReplyFailureReason reason, string detail) => new(false, new PrivateReplyFailure(reason, detail), null, null);
}

/// <summary>
/// Instagram Login comment Private Reply adapter contract (current official shape):
/// POST {graph}/{version}/{IG_ID}/messages with Bearer IG User token and body
/// {"recipient":{"comment_id":"..."},"message":{"text":"..."}}. Addressing is comment-ID
/// based — the commenter IGSID is NOT required by the current provider contract.
/// </summary>
public interface ICommentPrivateReplyClient
{
    Task<PrivateReplySendResult> SendPrivateReplyAsync(
        string accessToken,
        string providerAccountId,
        string commentId,
        string text,
        CancellationToken cancellationToken = default);
}
