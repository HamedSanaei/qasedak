using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Instagram.Application.Webhooks;

namespace Qasedak.Modules.Instagram.Infrastructure.Persistence;

/// <summary>Application-facing adapter over the webhook_inbox table.</summary>
public sealed class EfWebhookInboxStore(InstagramDbContext context) : IWebhookInboxStore
{
    public async Task<InboxEntryRecord?> LoadAsync(string eventId, CancellationToken cancellationToken = default)
    {
        var entry = await context.WebhookInbox.AsNoTracking()
            .SingleOrDefaultAsync(e => e.EventId == eventId, cancellationToken);
        return entry is null ? null : new InboxEntryRecord(entry.EventId, entry.Topic, entry.BodyJson);
    }

    public async Task<IReadOnlyList<InboxEntryRecord>> ListPendingAsync(int maxEntries, CancellationToken cancellationToken = default)
    {
        var entries = await context.WebhookInbox.AsNoTracking()
            .Where(e => e.Status == "pending")
            .OrderBy(e => e.ReceivedAtUtc)
            .Take(maxEntries)
            .Select(e => new InboxEntryRecord(e.EventId, e.Topic, e.BodyJson))
            .ToListAsync(cancellationToken);
        return entries;
    }

    public async Task MarkProcessedAsync(string eventId, DateTimeOffset processedAtUtc, CancellationToken cancellationToken = default)
    {
        await context.WebhookInbox
            .Where(e => e.EventId == eventId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.Status, "processed")
                .SetProperty(e => e.ProcessedAtUtc, processedAtUtc), cancellationToken);
    }

    public Task RecordDeliveryAttemptAsync(string eventId, DateTimeOffset atUtc, CancellationToken cancellationToken = default) =>
        context.WebhookInbox
            .Where(e => e.EventId == eventId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(e => e.DeliveryAttempts, e => e.DeliveryAttempts + 1), cancellationToken);

    public Task<int> CountPendingAsync(CancellationToken cancellationToken = default) =>
        context.WebhookInbox.CountAsync(e => e.Status == "pending", cancellationToken);
}
