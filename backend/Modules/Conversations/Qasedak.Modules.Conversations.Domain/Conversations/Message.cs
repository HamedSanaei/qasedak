namespace Qasedak.Modules.Conversations.Domain.Conversations;

/// <summary>One message inside a conversation thread. Immutable after creation.</summary>
public sealed class Message
{
    private Message()
    {
    }

    public Guid Id { get; private init; }

    public Guid ConversationId { get; private init; }

    public MessageDirection Direction { get; private init; }

    /// <summary>Provider identity (e.g. Meta "mid"); null for messages without one. Unique per conversation when present.</summary>
    public string? ProviderMessageId { get; private init; }

    /// <summary>Opaque sender identity (participant id or workspace account's provider id).</summary>
    public string SenderId { get; private init; } = string.Empty;

    public string Body { get; private init; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; private init; }

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
        };
    }

    /// <summary>Persistence rehydration.</summary>
    public static Message FromState(
        Guid id,
        Guid conversationId,
        MessageDirection direction,
        string? providerMessageId,
        string senderId,
        string body,
        DateTimeOffset occurredAtUtc)
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
        };
        return message;
    }
}
