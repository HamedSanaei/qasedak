namespace Qasedak.Modules.Instagram.Infrastructure.Persistence;

/// <summary>
/// Durable receipt of one verified webhook notification. The SHA-256 over the exact raw
/// body doubles as the event identity (Meta retries deliver byte-identical payloads), so
/// the primary key itself enforces at-most-once ingestion per delivery.
/// </summary>
public sealed class WebhookInboxEntry
{
    private WebhookInboxEntry()
    {
    }

    public string EventId { get; private init; } = string.Empty;

    /// <summary>Meta object discriminator, e.g. "instagram" or "unknown".</summary>
    public string Topic { get; private init; } = string.Empty;

    /// <summary>Body exactly as received (post JSON canonicalization for storage).</summary>
    public string BodyJson { get; private init; } = string.Empty;

    public DateTimeOffset ReceivedAtUtc { get; private init; }

    /// <summary>Processing state: pending until normalization consumes the entry.</summary>
    public string Status { get; private set; } = "pending";

    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    public int DeliveryAttempts { get; private set; }

    public static WebhookInboxEntry Receive(string eventId, string topic, string bodyJson, DateTimeOffset receivedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyJson);
        return new WebhookInboxEntry
        {
            EventId = eventId,
            Topic = topic,
            BodyJson = bodyJson,
            ReceivedAtUtc = receivedAtUtc,
        };
    }

    /// <summary>Records another redelivery attempt of an already-known event.</summary>
    public void RecordRedelivery(DateTimeOffset atUtc) => DeliveryAttempts++;

    /// <summary>Marks the entry consumed by downstream normalization.</summary>
    public void MarkProcessed(DateTimeOffset processedAtUtc)
    {
        Status = "processed";
        ProcessedAtUtc = processedAtUtc;
    }
}
