using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Conversations.Domain.Conversations;

namespace Qasedak.Modules.Conversations.Application.Conversations;

/// <summary>Persistence boundary for conversation aggregates (tracked loads for mutation).</summary>
public interface IConversationRepository
{
    Task<Conversation?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the workspace's conversation for one participant on one exact channel
    /// account; legacy (unresolved-account) threads are never returned here.
    /// </summary>
    Task<Conversation?> FindByParticipantAsync(
        Guid workspaceId, string channel, ChannelAccountId? channelAccountId, string participantId, CancellationToken cancellationToken = default);

    Task AddAsync(Conversation conversation, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
