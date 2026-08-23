using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Qasedak.BuildingBlocks.Application;
using Qasedak.Modules.Instagram.Application.Webhooks;
using Qasedak.Modules.Instagram.Infrastructure.Webhooks;

namespace Qasedak.Modules.Instagram.Infrastructure.Persistence;

/// <summary>
/// Durable, duplicate-safe ingestion boundary (ADR-006): every verified notification is
/// persisted before acceptance. Event identity is the SHA-256 of the exact raw body, so a
/// Meta redelivery maps to the same primary key and is swallowed as an accepted no-op.
/// Duplicate and redelivery counts feed webhook observability.
/// </summary>
public sealed partial class InboxWebhookIngester(
    InstagramDbContext context,
    IClock clock,
    WebhookMetrics metrics,
    ILogger<InboxWebhookIngester> logger) : IMetaWebhookIngester
{
    /// <summary>Redelivery attempts beyond this count signal a stuck consumer upstream.</summary>
    public const int RedeliveryAttentionThreshold = 3;

    private MetaWebhookLogs Logs { get; } = new(logger);

    public async Task<WebhookIngestionResult> IngestAsync(MetaWebhookNotification notification, CancellationToken cancellationToken = default)
    {
        var eventId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(notification.BodyJson))).ToLowerInvariant();

        var known = await context.WebhookInbox.FindAsync([eventId], cancellationToken);
        if (known is not null)
        {
            known.RecordRedelivery(clock.UtcNow);
            await context.SaveChangesAsync(cancellationToken);
            metrics.DuplicateDeliveries.Add(1, new KeyValuePair<string, object?>("topic", notification.ObjectTopic));
            if (known.DeliveryAttempts >= RedeliveryAttentionThreshold)
            {
                Logs.RedeliveryAttention(eventId, known.DeliveryAttempts, notification.CorrelationId, notification.ObjectTopic);
            }

            return WebhookIngestionResult.Accept();
        }

        context.WebhookInbox.Add(WebhookInboxEntry.Receive(eventId, notification.ObjectTopic, notification.BodyJson, clock.UtcNow));
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return WebhookIngestionResult.Accept();
        }
        catch (DbUpdateException)
        {
            // Concurrent delivery raced us to the same identity: still exactly-once stored.
            context.ChangeTracker.Clear();
            metrics.DuplicateDeliveries.Add(1, new KeyValuePair<string, object?>("topic", notification.ObjectTopic));
            return WebhookIngestionResult.Accept();
        }
    }
}
