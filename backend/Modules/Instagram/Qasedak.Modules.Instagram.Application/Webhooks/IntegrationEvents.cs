namespace Qasedak.Modules.Instagram.Application.Webhooks;

/// <summary>
/// Explicit integration events produced from raw Meta webhook payloads. These are the
/// module's stable contract toward future consumers; transport JSON never leaks past this
/// point (ADR-006: normalization happens once, inside the Instagram module).
/// </summary>
public interface IIntegrationEvent
{
    /// <summary>Inbox event identity this event was derived from.</summary>
    string EventId { get; }

    /// <summary>
    /// Canonical professional account routing identity carried by the webhook
    /// (entry.id / IG_ID). Never a participant IGSID, mid or comment id.
    /// </summary>
    string? ProviderAccountId { get; }
}

public sealed record InstagramMessageReceived(
    string EventId,
    string? ProviderAccountId,
    string SenderId,
    string? Text,
    DateTimeOffset SentAtUtc,
    /// <summary>Meta's per-message id ("mid"); the stable key for downstream deduplication.</summary>
    string? ProviderMessageId) : IIntegrationEvent;

public sealed record InstagramCommentCreated(
    string EventId,
    string? ProviderAccountId,
    string CommentId,
    /// <summary>Commenter's provider id ("value.from.id") — the DM target; null when Meta omits it.</summary>
    string? FromId,
    string? Text,
    DateTimeOffset CreatedAtUtc) : IIntegrationEvent;

public sealed record InstagramMentionCreated(
    string EventId,
    string? ProviderAccountId,
    string CommentId,
    DateTimeOffset CreatedAtUtc) : IIntegrationEvent;

/// <summary>A payload piece Meta sent that has no normalized representation yet.</summary>
public sealed record UnrecognizedWebhookFragment(string EventId, string Kind);

/// <summary>Everything one inbox entry normalized into.</summary>
public sealed record NormalizationOutcome(IReadOnlyList<IIntegrationEvent> Events, IReadOnlyList<UnrecognizedWebhookFragment> Unrecognized)
{
    public bool IsEmpty => Events.Count == 0 && Unrecognized.Count == 0;
}
