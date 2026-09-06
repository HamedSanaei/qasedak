namespace Qasedak.Modules.Instagram.Application.Webhooks;

/// <summary>
/// Explicit integration events produced from raw Meta webhook payloads. These are the
/// module's stable contract toward future consumers; transport JSON never leaks past this
/// point (ADR-010: normalization happens once, inside the Instagram module).
///
/// Exact-account enrichment (M13-008): a pure normalization produces provider-account-aware
/// fragments; the processing boundary resolves the exact active connected account behind
/// <see cref="ProviderAccountId"/> and stamps <see cref="WorkspaceId"/> /
/// <see cref="ConnectedAccountId"/> before dispatch. Downstream consumers MUST NOT
/// re-resolve provider identities — the Qasedak account key is the durable identity.
/// </summary>
public interface IIntegrationEvent
{
    /// <summary>
    /// Deterministic fragment identity this event was derived from
    /// ("&lt;inbox-event-id&gt;:e&lt;entry-index&gt;:m|&lt;item-index&gt;"). Redelivery of the
    /// same raw body produces the same identity; siblings in one inbox entry are distinct,
    /// so per-fragment idempotency (automation run ledger, interaction ledger) is safe.
    /// </summary>
    string EventId { get; }

    /// <summary>
    /// Qasedak workspace key of the resolved account, or null when the event was not
    /// enriched (legacy construction only; the processing boundary always resolves).
    /// </summary>
    Guid? WorkspaceId { get; }

    /// <summary>
    /// Qasedak exact connected-account key (the event's authoritative identity), or null
    /// when not enriched. Never a provider id and never a workspace-wide guess.
    /// </summary>
    Guid? ConnectedAccountId { get; }

    /// <summary>
    /// Canonical professional account routing identity carried by the webhook
    /// (entry.id / IG_ID). Provider correlation/debug identity only — consumers must not
    /// route on it. Never a participant IGSID, mid or comment id.
    /// </summary>
    string? ProviderAccountId { get; }
}

public sealed record InstagramMessageReceived(
    string EventId,
    Guid? WorkspaceId,
    Guid? ConnectedAccountId,
    string? ProviderAccountId,
    string SenderId,
    string? Text,
    DateTimeOffset SentAtUtc,
    /// <summary>Meta's per-message id ("mid"); the stable key for downstream deduplication.</summary>
    string? ProviderMessageId,
    /// <summary>Bounded quick-reply payload when the user tapped a quick reply (optional).</summary>
    string? QuickReplyPayload) : IIntegrationEvent;

public sealed record InstagramCommentCreated(
    string EventId,
    Guid? WorkspaceId,
    Guid? ConnectedAccountId,
    string? ProviderAccountId,
    string CommentId,
    /// <summary>Commenter's provider id ("value.from.id") — the DM target; null when Meta omits it. Never fabricated.</summary>
    string? FromId,
    /// <summary>Commenter's username ("value.from.username"); display metadata, not durable identity.</summary>
    string? CommenterUsername,
    string? Text,
    /// <summary>Opaque provider media id the comment was made on ("value.media.id"); never resolved.</summary>
    string? MediaId,
    /// <summary>Original media id for ad/boosted comments ("value.media.original_media_id"); preserved separately, never merged into <see cref="MediaId"/>.</summary>
    string? OriginalMediaId,
    /// <summary>Provider time: webhook entry.time (the only provider time in the current official comments payload).</summary>
    DateTimeOffset CreatedAtUtc) : IIntegrationEvent;

public sealed record InstagramMentionCreated(
    string EventId,
    Guid? WorkspaceId,
    Guid? ConnectedAccountId,
    string? ProviderAccountId,
    string CommentId,
    DateTimeOffset CreatedAtUtc) : IIntegrationEvent;

/// <summary>
/// A postback tap (icebreaker / CTA button) normalized from "messaging_postbacks".
/// Provider shape: postback:{mid,title,payload}. Fields are bounded by
/// <see cref="WebhookNormalizationPolicy"/>; oversized values become unrecognized
/// fragments instead of being truncated.
/// </summary>
public sealed record InstagramPostbackReceived(
    string EventId,
    Guid? WorkspaceId,
    Guid? ConnectedAccountId,
    string? ProviderAccountId,
    /// <summary>Participant IGSID who tapped ("sender.id").</summary>
    string SenderId,
    /// <summary>Provider message correlation id ("postback.mid").</summary>
    string ProviderMessageId,
    /// <summary>Icebreaker or CTA button label the user selected ("postback.title").</summary>
    string Title,
    /// <summary>Bounded provider payload the app user defined ("postback.payload").</summary>
    string Payload,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

/// <summary>
/// A read receipt normalized from "messaging_seen". Provider shape: read:{mid} — the
/// message id that was read. There is NO watermark in the current official Instagram
/// contract (ADR-010); this event deliberately carries no watermark-shaped field.
/// </summary>
public sealed record InstagramMessageRead(
    string EventId,
    Guid? WorkspaceId,
    Guid? ConnectedAccountId,
    string? ProviderAccountId,
    /// <summary>Participant IGSID whose message was read ("sender.id").</summary>
    string SenderId,
    /// <summary>Provider message id that was read ("read.mid").</summary>
    string ProviderMessageId,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

/// <summary>
/// A fragment Meta sent that has no normalized representation yet: malformed JSON,
/// unknown change fields, unknown messaging shapes, or an entry whose account could not
/// be resolved. Reported and observable; never silently dropped.
/// </summary>
public sealed record UnrecognizedWebhookFragment(string EventId, string Kind);

/// <summary>
/// A fragment Meta sent that is recognized but deliberately non-triggering for current
/// automation semantics (echoes, self tests, deleted/unsupported/attachment-only
/// messages, edits, reactions, referrals). Reported with a stable reason; never an
/// <see cref="IIntegrationEvent"/>.
/// </summary>
public sealed record IgnoredWebhookFragment(string EventId, string Reason);

/// <summary>Everything one entry of a webhook body normalized into.</summary>
public sealed record EntryNormalization(
    int EntryIndex,
    string? ProviderAccountId,
    IReadOnlyList<IIntegrationEvent> Events,
    IReadOnlyList<UnrecognizedWebhookFragment> Unrecognized,
    IReadOnlyList<IgnoredWebhookFragment> Ignored);

/// <summary>
/// Everything one inbox entry normalized into, grouped per webhook entry so exact-account
/// resolution and dispatch stay independent per entry (multi-entry fan-out never reuses a
/// previous entry's account).
/// </summary>
public sealed record NormalizationOutcome(
    IReadOnlyList<EntryNormalization> Entries,
    IReadOnlyList<UnrecognizedWebhookFragment> TopLevelUnrecognized)
{
    public bool IsEmpty => TopLevelUnrecognized.Count == 0 && Entries.All(entry =>
        entry.Events.Count == 0 && entry.Unrecognized.Count == 0 && entry.Ignored.Count == 0);
}
