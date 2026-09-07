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

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Concurrent history/webhook projections race on the exact-thread unique
            // index or the scoped provider-message index; application use cases retry
            // or treat the collision as an idempotent no-op (M13-013 §60/§64).
            throw new UniqueConstraintViolationException("A unique constraint was violated.", exception);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        var current = (Exception?)exception;
        while (current is not null)
        {
            if (current is Npgsql.PostgresException { SqlState: "23505" })
            {
                return true;
            }

            current = current.InnerException;
        }

        return exception.Message.Contains("23505", StringComparison.Ordinal);
    }
}
