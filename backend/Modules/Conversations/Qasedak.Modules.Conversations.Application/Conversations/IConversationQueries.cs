namespace Qasedak.Modules.Conversations.Application.Conversations;

/// <summary>Read-side boundary for workspace inbox queries (no aggregate loading).</summary>
public interface IConversationQueries
{
    Task<InboxPage> ListAsync(Guid workspaceId, InboxFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Returns the thread with messages when it belongs to the workspace; otherwise null.</summary>
    Task<(InboxConversationRow Row, IReadOnlyList<InboxMessageRow> Messages)?> GetDetailAsync(
        Guid workspaceId, Guid conversationId, CancellationToken cancellationToken = default);
}
