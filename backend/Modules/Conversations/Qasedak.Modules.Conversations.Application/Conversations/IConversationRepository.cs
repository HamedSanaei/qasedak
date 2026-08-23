using Qasedak.Modules.Conversations.Domain.Conversations;

namespace Qasedak.Modules.Conversations.Application.Conversations;

/// <summary>Persistence boundary for conversation aggregates (tracked loads for mutation).</summary>
public interface IConversationRepository
{
    Task<Conversation?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Finds the workspace's conversation with one participant; read-only use.</summary>
    Task<Conversation?> FindByParticipantAsync(Guid workspaceId, string channel, string participantId, CancellationToken cancellationToken = default);

    Task AddAsync(Conversation conversation, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
