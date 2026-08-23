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

    /// <summary>Provider identity of the connected account the event belongs to, when resolvable.</summary>
    string? ProviderUserId { get; }
}

public sealed record InstagramMessageReceived(
    string EventId,
    string? ProviderUserId,
    string SenderId,
    string? Text,
    DateTimeOffset SentAtUtc) : IIntegrationEvent;

public sealed record InstagramCommentCreated(
    string EventId,
    string? ProviderUserId,
    string CommentId,
    string? Text,
    DateTimeOffset CreatedAtUtc) : IIntegrationEvent;

public sealed record InstagramMentionCreated(
    string EventId,
    string? ProviderUserId,
    string CommentId,
    DateTimeOffset CreatedAtUtc) : IIntegrationEvent;

/// <summary>A payload piece Meta sent that has no normalized representation yet.</summary>
public sealed record UnrecognizedWebhookFragment(string EventId, string Kind);

/// <summary>Everything one inbox entry normalized into.</summary>
public sealed record NormalizationOutcome(IReadOnlyList<IIntegrationEvent> Events, IReadOnlyList<UnrecognizedWebhookFragment> Unrecognized)
{
    public bool IsEmpty => Events.Count == 0 && Unrecognized.Count == 0;
}
