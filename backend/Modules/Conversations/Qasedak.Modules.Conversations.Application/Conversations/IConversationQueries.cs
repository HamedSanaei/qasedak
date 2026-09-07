using Qasedak.BuildingBlocks.Domain;

namespace Qasedak.Modules.Conversations.Application.Conversations;

/// <summary>Read-side boundary for workspace inbox queries (no aggregate loading).</summary>
public interface IConversationQueries
{
    Task<InboxPage> ListAsync(Guid workspaceId, InboxFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Returns the thread with messages when it belongs to the workspace; otherwise null.</summary>
    Task<(InboxConversationRow Row, IReadOnlyList<InboxMessageRow> Messages)?> GetDetailAsync(
        Guid workspaceId, Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Latest inbound-message occurred-at for an exact channel account + participant, or
    /// null when no inbound evidence exists. This is the authoritative local 24h-window
    /// anchor for delayed Direct effects (M13-012 §49) — never a comment/private-reply/
    /// postback/schedule timestamp.
    /// </summary>
    Task<DateTimeOffset?> GetLatestInboundOccurredAtUtcAsync(
        Guid workspaceId,
        string channel,
        ChannelAccountId channelAccountId,
        string participantId,
        CancellationToken cancellationToken = default);
}
