namespace Qasedak.Modules.Instagram.Application.Webhooks;

/// <summary>
/// Seam for work performed after durable ingestion. The module registers a no-op default;
/// the composition root may replace it to hand pending entries to downstream consumers
/// without Instagram knowing any consumer types.
/// </summary>
public interface IWebhookPostIngestProcessor
{
    Task ProcessPendingAsync(int maxEntries = 50, CancellationToken cancellationToken = default);
}

/// <summary>Module-default no-op: ingestion alone is the contract; consumers opt in.</summary>
public sealed class NullWebhookPostIngestProcessor : IWebhookPostIngestProcessor
{
    public Task ProcessPendingAsync(int maxEntries = 50, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
