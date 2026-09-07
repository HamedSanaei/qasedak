namespace Qasedak.Modules.Instagram.Application.Messaging;

/// <summary>
/// Observability seam for direct-message sends (M13-010). Implementations MUST keep
/// dimensions low-cardinality (operation content kind / button kind / outcome / failure
/// category only) — never WorkspaceId, AccountId, IGSID, CommentId, MessageId, payload,
/// URL or message content.
/// </summary>
public interface IMessageSendObservability
{
    /// <summary>
    /// Records one direct-message outcome. <paramref name="buttonKind"/> is
    /// "none" for plain text and "postback" / "web_url" / "mixed" for templates;
    /// <paramref name="failureCategory"/> is null on success.
    /// </summary>
    void DirectSend(string contentKind, string buttonKind, string outcome, string? failureCategory);
}
