namespace Qasedak.Modules.Conversations.Domain.Conversations;

/// <summary>Lifecycle state of a conversation in the workspace inbox.</summary>
public enum ConversationStatus
{
    Open = 1,
    Archived = 2,
}

/// <summary>Direction of one message relative to the workspace.</summary>
public enum MessageDirection
{
    Inbound = 1,
    Outbound = 2,
}
