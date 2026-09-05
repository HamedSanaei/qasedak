using Microsoft.EntityFrameworkCore;
using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Conversations.Application.Conversations;
using Qasedak.Modules.Conversations.Domain.Conversations;

namespace Qasedak.Modules.Conversations.Infrastructure.Persistence;

/// <summary>Application-facing repository over the conversations tables.</summary>
public sealed class EfConversationRepository(ConversationsDbContext context) : IConversationRepository
{
    public Task<Conversation?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Conversations
            .Include(c => c.Messages)
            .SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<Conversation?> FindByParticipantAsync(
        Guid workspaceId, string channel, ChannelAccountId? channelAccountId, string participantId, CancellationToken cancellationToken = default) =>
        await context.Conversations
            .Include(c => c.Messages)
            .SingleOrDefaultAsync(
                c => c.WorkspaceId == workspaceId && c.Channel == channel && c.ChannelAccountId == channelAccountId && c.ParticipantId == participantId,
                cancellationToken);

    public async Task AddAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        await context.Conversations.AddAsync(conversation, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
