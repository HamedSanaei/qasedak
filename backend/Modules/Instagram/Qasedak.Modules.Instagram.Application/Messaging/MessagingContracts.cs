namespace Qasedak.Modules.Instagram.Application.Messaging;

/// <summary>Structured reasons for a rejected/failed Instagram message send.</summary>
public enum MessagingFailureReason
{
    /// <summary>Local validation/policy rejection before any provider call.</summary>
    LocalValidation,

    /// <summary>Content kind is not supported for this operation; no provider call is issued.</summary>
    UnsupportedContent,

    /// <summary>Network-level failure; the request never reached Meta or no answer arrived.</summary>
    TransportFailure,

    /// <summary>Meta answered with an error payload outside the known special cases.</summary>
    RejectedByMeta,

    /// <summary>
    /// Meta refused because the recipient is outside the 24-hour customer service window
    /// (official Graph code 10 + subcode 2534022). Distinct so callers can schedule
    /// instead of retrying blindly.
    /// </summary>
    MessagingWindowExpired,

    /// <summary>Meta answered successfully but the payload did not match the contract.</summary>
    MalformedResponse,
}

public sealed record MessagingSendResult
{
    public bool Succeeded { get; }

    public MessagingFailure? Failure { get; }

    /// <summary>Official success identity — recipient_id (IGSID). Never fabricated.</summary>
    public string? ProviderRecipientId { get; }

    /// <summary>Official success identity — message_id (mid). Never fabricated.</summary>
    public string? ProviderMessageId { get; }

    private MessagingSendResult(bool succeeded, MessagingFailure? failure, string? providerRecipientId, string? providerMessageId)
    {
        Succeeded = succeeded;
        Failure = failure;
        ProviderRecipientId = providerRecipientId;
        ProviderMessageId = providerMessageId;
    }

    public static MessagingSendResult Ok(string providerRecipientId, string providerMessageId) =>
        new(true, null, providerRecipientId, providerMessageId);

    public static MessagingSendResult Fail(MessagingFailureReason reason, string detail) =>
        new(false, new MessagingFailure(reason, detail), null, null);
}

public sealed record MessagingFailure(MessagingFailureReason Reason, string Detail);

/// <summary>
/// Port to Instagram's messaging send API (recipient.id / 24h-window semantics).
/// Implementations must never log token or message content; failures are structured
/// results, never exceptions across this boundary. Local content validation happens
/// before any provider call (zero provider traffic for invalid content).
/// </summary>
public interface IInstagramMessagingClient
{
    /// <summary>
    /// Sends typed content (plain text or button template) as a normal Direct Message:
    /// POST {graph}/{version}/me/messages, recipient.id = IGSID, Bearer IG User token,
    /// 24-hour-window classification (code 10 / subcode 2534022) preserved. Success
    /// returns the provider message identity (recipient_id + message_id).
    /// </summary>
    Task<MessagingSendResult> SendDirectAsync(
        string accessToken,
        string recipientProviderUserId,
        InstagramMessageContent content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Plain-text convenience used by existing conversation-reply callers (M05);
    /// delegates to <see cref="SendDirectAsync"/> with <see cref="InstagramMessageContent.PlainText"/>.
    /// </summary>
    Task<MessagingSendResult> SendTextAsync(
        string accessToken,
        string recipientProviderUserId,
        string text,
        CancellationToken cancellationToken = default);
}
