namespace Qasedak.Modules.Instagram.Application.Webhooks;

/// <summary>
/// A verified Meta webhook notification handed to the module after authenticity checks.
/// Carries the raw bytes exactly as received plus the parsed object/topic discriminator
/// and the request's correlation id; no ASP.NET Core transport types leak into Application.
/// </summary>
public sealed record MetaWebhookNotification(string ObjectTopic, string BodyJson, string CorrelationId);

/// <summary>Outcome accepted by the ingestion boundary.</summary>
public readonly record struct WebhookIngestionResult(bool Accepted, string? Reason)
{
    public static WebhookIngestionResult Accept() => new(true, null);

    public static WebhookIngestionResult Reject(string reason) => new(false, reason);
}

/// <summary>
/// Ingestion boundary for verified notifications. Implementations must be duplicate-safe
/// (the durable inbox owns idempotency per ADR-006's deduplication mandate).
/// </summary>
public interface IMetaWebhookIngester
{
    Task<WebhookIngestionResult> IngestAsync(MetaWebhookNotification notification, CancellationToken cancellationToken = default);
}
