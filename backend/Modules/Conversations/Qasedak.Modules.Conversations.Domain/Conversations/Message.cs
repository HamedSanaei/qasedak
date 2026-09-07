namespace Qasedak.Modules.Conversations.Domain.Conversations;

/// <summary>Content classification of one message (M13-013 §55). Never fabricates
/// provider content: Unsupported/Share/NoText are truthful representations.</summary>
public enum MessageContentKind
{
    /// <summary>Provider-supplied plain text body.</summary>
    Text = 1,

    /// <summary>Provider marked the message unsupported (is_unsupported).</summary>
    Unsupported = 2,

    /// <summary>Share content; only a bounded URL is available from the provider.</summary>
    Share = 3,

    /// <summary>Supported message without text/share content (e.g. media-only) —
    /// deliberately distinct from an empty text message.</summary>
    NoText = 4,
}

/// <summary>Provenance of a message row (M13-013 §113). Small and channel-neutral;
/// used only for merge precedence, unread correctness and audit truth.</summary>
public enum MessageImportSource
{
    /// <summary>Projected from a live real-time event (webhook).</summary>
    RealTime = 1,

    /// <summary>Imported from bounded provider history (never a new-message notification).</summary>
    ProviderHistory = 2,
}

/// <summary>One message inside a conversation thread. Immutable after creation.</summary>
public sealed class Message
{
    private Message()
    {
    }

    public Guid Id { get; private init; }

    public Guid ConversationId { get; private init; }

    public MessageDirection Direction { get; private init; }

    /// <summary>Provider identity (e.g. Meta "mid"); null for messages without one.
    /// Unique per conversation when present (Domain contract; the corrected scoped
    /// database index enforces (ConversationId, ProviderMessageId)).</summary>
    public string? ProviderMessageId { get; private init; }

    /// <summary>Opaque sender identity (participant id or workspace account's provider id).</summary>
    public string SenderId { get; private init; } = string.Empty;

    public string Body { get; private init; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; private init; }

    /// <summary>Truthful content classification; Body may be empty without meaning
    /// the provider sent an empty text message.</summary>
    public MessageContentKind ContentKind { get; private init; } = MessageContentKind.Text;

    /// <summary>Provenance: live webhook projection vs bounded provider history import.</summary>
    public MessageImportSource ImportSource { get; private init; } = MessageImportSource.RealTime;

    /// <summary>
    /// True once the live webhook observed this provider message (relevant only for
    /// history-imported rows): the real-time event proves delivery and accounts for
    /// unread exactly once (M13-013 §59).
    /// </summary>
    public bool WebhookObserved { get; private set; }

    public static Message Create(
        Guid id,
        Guid conversationId,
        MessageDirection direction,
        string? providerMessageId,
        string senderId,
        string body,
        DateTimeOffset occurredAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ConversationsDomainException("message.invalidId", "Message id must be provided.");
        }

        if (conversationId == Guid.Empty)
        {
            throw new ConversationsDomainException("message.conversationRequired", "A message requires its conversation.");
        }

        if (body.Length > Conversation.MaxBodyLength)
        {
            throw new ConversationsDomainException("message.tooLong", $"Message body exceeds {Conversation.MaxBodyLength} characters.");
        }

        return new Message
        {
            Id = id,
            ConversationId = conversationId,
            Direction = direction,
            ProviderMessageId = string.IsNullOrWhiteSpace(providerMessageId) ? null : providerMessageId.Trim(),
            SenderId = senderId.Trim(),
            Body = body,
            OccurredAtUtc = occurredAtUtc,
            ContentKind = MessageContentKind.Text,
            ImportSource = MessageImportSource.RealTime,
        };
    }

    /// <summary>Creates a history-imported message. The provider message identity is
    /// REQUIRED (a history row without it is skipped upstream, never imported as a
    /// normal provider message — M13-013 §47).</summary>
    public static Message CreateFromHistory(
        Guid id,
        Guid conversationId,
        MessageDirection direction,
        string providerMessageId,
        string senderId,
        string? body,
        DateTimeOffset occurredAtUtc,
        MessageContentKind contentKind)
    {
        if (id == Guid.Empty)
        {
            throw new ConversationsDomainException("message.invalidId", "Message id must be provided.");
        }

        if (conversationId == Guid.Empty)
        {
            throw new ConversationsDomainException("message.conversationRequired", "A message requires its conversation.");
        }

        if (string.IsNullOrWhiteSpace(providerMessageId))
        {
            throw new ConversationsDomainException("message.providerIdRequired", "History import requires the provider message identity.");
        }

        var boundedBody = body is null ? string.Empty : body.Length > Conversation.MaxBodyLength ? body[..Conversation.MaxBodyLength] : body;
        return new Message
        {
            Id = id,
            ConversationId = conversationId,
            Direction = direction,
            ProviderMessageId = providerMessageId.Trim(),
            SenderId = senderId.Trim(),
            Body = boundedBody,
            OccurredAtUtc = occurredAtUtc,
            ContentKind = contentKind,
            ImportSource = MessageImportSource.ProviderHistory,
        };
    }

    /// <summary>Marks a history-imported row as observed by the live webhook; the
    /// unread increment happens exactly once here (M13-013 §59).</summary>
    public void MarkObservedByWebhook(DateTimeOffset observedAtUtc)
    {
        if (ImportSource != MessageImportSource.ProviderHistory || WebhookObserved)
        {
            throw new ConversationsDomainException("message.webhookObservationInvalid", "Only a history-imported message not yet observed may be marked.");
        }

        WebhookObserved = true;
        _observedAtUtc = observedAtUtc;
    }

    private DateTimeOffset? _observedAtUtc;

    public DateTimeOffset? WebhookObservedAtUtc => _observedAtUtc;

    /// <summary>Persistence rehydration.</summary>
    public static Message FromState(
        Guid id,
        Guid conversationId,
        MessageDirection direction,
        string? providerMessageId,
        string senderId,
        string body,
        DateTimeOffset occurredAtUtc,
        MessageContentKind contentKind = MessageContentKind.Text,
        MessageImportSource importSource = MessageImportSource.RealTime,
        bool webhookObserved = false)
    {
        var message = new Message
        {
            Id = id,
            ConversationId = conversationId,
            Direction = direction,
            ProviderMessageId = providerMessageId,
            SenderId = senderId,
            Body = body,
            OccurredAtUtc = occurredAtUtc,
            ContentKind = contentKind,
            ImportSource = importSource,
            WebhookObserved = webhookObserved,
        };
        return message;
    }
}
