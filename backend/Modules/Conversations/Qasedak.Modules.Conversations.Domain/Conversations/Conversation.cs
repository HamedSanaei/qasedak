namespace Qasedak.Modules.Conversations.Domain.Conversations;

using Qasedak.BuildingBlocks.Domain;

/// <summary>
/// One conversation thread in a workspace inbox. Identity ownership: the workspace owns
/// the conversation; the external counterpart is an opaque participant id (never a Meta
/// type). Since M13-002 the natural key also carries the exact channel account
/// (ChannelAccountId): the same participant on two connected accounts yields two
/// threads. A missing account marks a legacy pre-M13-002 row, which stays readable but
/// must never route outbound traffic. Messages are appended through the aggregate so
/// invariants (unique provider message identity, monotonic last-activity, unread
/// accounting) hold in one place.
/// Timestamps are always passed in — the Domain owns no clock.
/// </summary>
public sealed class Conversation
{
    private readonly List<Message> _messages = [];

    private Conversation()
    {
    }

    public Guid Id { get; private init; }

    public Guid WorkspaceId { get; private init; }

    /// <summary>Logical channel, e.g. "instagram"; keeps the model channel-agnostic.</summary>
    public string Channel { get; private init; } = string.Empty;

    /// <summary>
    /// Exact connected channel account (opaque, provider-neutral). Null marks a legacy
    /// pre-M13-002 thread: readable, but outbound sends must refuse it explicitly.
    /// </summary>
    public ChannelAccountId? ChannelAccountId { get; private init; }

    /// <summary>Opaque id of the external counterpart (their provider user id).</summary>
    public string ParticipantId { get; private init; } = string.Empty;

    public ConversationStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private init; }

    public DateTimeOffset LastMessageAtUtc { get; private set; }

    public int UnreadCount { get; private set; }

    public IReadOnlyList<Message> Messages => _messages;

    public static Conversation Create(
        Guid id,
        Guid workspaceId,
        string channel,
        string participantId,
        DateTimeOffset createdAtUtc,
        ChannelAccountId? channelAccountId = null)
    {
        if (id == Guid.Empty)
        {
            throw new ConversationsDomainException("conversation.invalidId", "Conversation id must be provided.");
        }

        if (workspaceId == Guid.Empty)
        {
            throw new ConversationsDomainException("conversation.workspaceRequired", "A conversation requires a workspace.");
        }

        if (string.IsNullOrWhiteSpace(channel))
        {
            throw new ConversationsDomainException("conversation.channelRequired", "A conversation requires a channel.");
        }

        if (string.IsNullOrWhiteSpace(participantId))
        {
            throw new ConversationsDomainException("conversation.participantRequired", "A conversation requires a participant.");
        }

        if (channelAccountId is { IsResolved: false })
        {
            throw new ConversationsDomainException("conversation.accountInvalid", "A channel account identity must name a real account.");
        }

        return new Conversation
        {
            Id = id,
            WorkspaceId = workspaceId,
            Channel = channel.Trim(),
            ChannelAccountId = channelAccountId,
            ParticipantId = participantId.Trim(),
            Status = ConversationStatus.Open,
            CreatedAtUtc = createdAtUtc,
            LastMessageAtUtc = createdAtUtc,
        };
    }

    /// <summary>Persistence rehydration.</summary>
    public static Conversation FromState(
        Guid id,
        Guid workspaceId,
        string channel,
        ChannelAccountId? channelAccountId,
        string participantId,
        ConversationStatus status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset lastMessageAtUtc,
        int unreadCount,
        IReadOnlyList<MessageState> messages)
    {
        var conversation = new Conversation
        {
            Id = id,
            WorkspaceId = workspaceId,
            Channel = channel,
            ChannelAccountId = channelAccountId,
            ParticipantId = participantId,
            Status = status,
            CreatedAtUtc = createdAtUtc,
            LastMessageAtUtc = lastMessageAtUtc,
            UnreadCount = unreadCount,
        };

        foreach (var state in messages)
        {
            conversation._messages.Add(Message.FromState(
                state.Id, state.ConversationId, state.Direction, state.ProviderMessageId,
                state.SenderId, state.Body, state.OccurredAtUtc));
        }

        return conversation;
    }

    /// <summary>
    /// Appends one message. Idempotency lives here: a provider message id already present
    /// is rejected by rule code so projections can treat duplicates as data errors after
    /// their own deduplication layer.
    /// </summary>
    public Message AppendMessage(
        Guid messageId,
        MessageDirection direction,
        string? providerMessageId,
        string senderId,
        string body,
        DateTimeOffset occurredAtUtc)
    {
        if (_messages.Any(m => m.ProviderMessageId is not null && m.ProviderMessageId == providerMessageId))
        {
            throw new ConversationsDomainException(
                "message.duplicateProviderId",
                $"Provider message '{providerMessageId}' was already appended to this conversation.");
        }

        if (body.Length > MaxBodyLength)
        {
            throw new ConversationsDomainException("message.tooLong", $"Message body exceeds {MaxBodyLength} characters.");
        }

        // Note: occurredAtUtc may legitimately precede CreatedAtUtc — webhook payloads carry
        // the provider-side send time, while the thread is created at processing time.

        if (Status == ConversationStatus.Archived && direction == MessageDirection.Inbound)
        {
            // Inbound traffic reopens the thread: unread accounting must stay truthful.
            Status = ConversationStatus.Open;
        }

        var message = Message.Create(messageId, Id, direction, providerMessageId, senderId, body, occurredAtUtc);
        _messages.Add(message);
        if (occurredAtUtc > LastMessageAtUtc || _messages.Count == 1)
        {
            LastMessageAtUtc = occurredAtUtc;
        }

        if (direction == MessageDirection.Inbound)
        {
            UnreadCount++;
        }

        return message;
    }

    public const int MaxBodyLength = 1000;

    /// <summary>Workspace member read the inbound queue; resets unread accounting.</summary>
    public void MarkRead(DateTimeOffset readAtUtc)
    {
        if (UnreadCount == 0)
        {
            throw new ConversationsDomainException("conversation.alreadyRead", "No unread messages to mark.");
        }

        UnreadCount = 0;
        _lastReadAtUtc = readAtUtc;
    }

    private DateTimeOffset? _lastReadAtUtc;

    public DateTimeOffset? LastReadAtUtc => _lastReadAtUtc;

    /// <summary>Archiving hides a thread from the active inbox; inbound traffic reopens it.</summary>
    public void Archive(DateTimeOffset archivedAtUtc)
    {
        if (Status == ConversationStatus.Archived)
        {
            throw new ConversationsDomainException("conversation.alreadyArchived", "The conversation is already archived.");
        }

        Status = ConversationStatus.Archived;
        _archivedAtUtc = archivedAtUtc;
    }

    private DateTimeOffset? _archivedAtUtc;

    public DateTimeOffset? ArchivedAtUtc => _archivedAtUtc;
}

public sealed record MessageState(
    Guid Id,
    Guid ConversationId,
    MessageDirection Direction,
    string? ProviderMessageId,
    string SenderId,
    string Body,
    DateTimeOffset OccurredAtUtc);
