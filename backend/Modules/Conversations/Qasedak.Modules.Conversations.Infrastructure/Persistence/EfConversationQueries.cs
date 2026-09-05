using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Conversations.Application.Conversations;
using Qasedak.Modules.Conversations.Domain.Conversations;

namespace Qasedak.Modules.Conversations.Infrastructure.Persistence;

/// <summary>Read-optimized inbox queries: server-side paging/filtering, no tracking.</summary>
public sealed class EfConversationQueries(ConversationsDbContext context) : IConversationQueries
{
    public async Task<InboxPage> ListAsync(Guid workspaceId, InboxFilter filter, CancellationToken cancellationToken = default)
    {
        var baseQuery = context.Conversations.AsNoTracking()
            .Where(c => c.WorkspaceId == workspaceId);

        if (!string.IsNullOrWhiteSpace(filter.Status)
            && Enum.TryParse<ConversationStatus>(filter.Status, ignoreCase: true, out var status))
        {
            baseQuery = baseQuery.Where(c => c.Status == status);
        }

        // Case-insensitive contains-search over the counterpart identity and every message
        // body. The term is escaped (SearchPattern) so % / _ / \ in user input never act as
        // LIKE wildcards. Matches translate to an EXISTS over messages in PostgreSQL.
        var search = SearchPattern.Build(filter.Search);
        if (search is not null)
        {
            baseQuery = baseQuery.Where(c =>
                EF.Functions.ILike(c.ParticipantId, search)
                || c.Messages.Any(m => EF.Functions.ILike(m.Body, search)));
        }

        var total = await baseQuery.CountAsync(cancellationToken);

        var items = await baseQuery
            .OrderByDescending(c => c.LastMessageAtUtc)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .Select(c => new InboxConversationRow(
                c.Id,
                c.Channel,
                c.ChannelAccountId,
                c.ParticipantId,
                c.Status.ToString(),
                c.LastMessageAtUtc,
                c.UnreadCount,
                c.Messages
                    .OrderByDescending(m => m.OccurredAtUtc)
                    .Select(m => m.Body)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new InboxPage(items, filter.Page, filter.Take, total);
    }

    public async Task<(InboxConversationRow Row, IReadOnlyList<InboxMessageRow> Messages)?> GetDetailAsync(
        Guid workspaceId, Guid conversationId, CancellationToken cancellationToken = default)
    {
        var row = await context.Conversations.AsNoTracking()
            .Where(c => c.WorkspaceId == workspaceId && c.Id == conversationId)
            .Select(c => new InboxConversationRow(
                c.Id,
                c.Channel,
                c.ChannelAccountId,
                c.ParticipantId,
                c.Status.ToString(),
                c.LastMessageAtUtc,
                c.UnreadCount,
                null))
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var messages = await context.Set<Message>().AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.OccurredAtUtc)
            .ThenBy(m => m.Id)
            .Select(m => new InboxMessageRow(m.Id, m.Direction, m.ProviderMessageId, m.SenderId, m.Body, m.OccurredAtUtc))
            .ToListAsync(cancellationToken);

        return (row, messages);
    }
}
