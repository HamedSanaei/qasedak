namespace Qasedak.Modules.Instagram.Application.Webhooks;

/// <summary>
/// Application view over the durable webhook inbox: pending entries are consumed by
/// normalization, then closed; failures leave them pending for retry visibility.
/// </summary>
public interface IWebhookInboxStore
{
    Task<InboxEntryRecord?> LoadAsync(string eventId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InboxEntryRecord>> ListPendingAsync(int maxEntries, CancellationToken cancellationToken = default);

    Task MarkProcessedAsync(string eventId, DateTimeOffset processedAtUtc, CancellationToken cancellationToken = default);

    Task RecordDeliveryAttemptAsync(string eventId, DateTimeOffset atUtc, CancellationToken cancellationToken = default);

    /// <summary>Backlog size for observability: entries awaiting normalization.</summary>
    Task<int> CountPendingAsync(CancellationToken cancellationToken = default);
}

public sealed record InboxEntryRecord(string EventId, string Topic, string BodyJson);
