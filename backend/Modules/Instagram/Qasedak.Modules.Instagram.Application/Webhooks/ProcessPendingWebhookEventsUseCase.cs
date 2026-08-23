using Qasedak.BuildingBlocks.Application;

namespace Qasedak.Modules.Instagram.Application.Webhooks;

/// <summary>
/// Consumes pending inbox entries: normalizes each body into explicit integration events,
/// dispatches every event, then closes the entry. Entries whose payload cannot be
/// dispatched stay pending so failure/retry visibility remains observable (M04-004 adds
/// metrics on top). Unrecognized fragments are reported but do not block closing — the raw
/// body is durably stored and never lost.
/// </summary>
public sealed class ProcessPendingWebhookEventsUseCase(
    IWebhookInboxStore inbox,
    IIntegrationEventDispatcher dispatcher,
    IClock clock)
{
    public async Task<WebhookProcessingSummary> ProcessPendingAsync(int maxEntries = 50, CancellationToken cancellationToken = default)
    {
        var entries = await inbox.ListPendingAsync(maxEntries, cancellationToken);
        var processed = 0;
        var unrecognized = 0;

        foreach (var entry in entries)
        {
            var outcome = MetaPayloadNormalizer.Normalize(entry.EventId, entry.Topic, entry.BodyJson);
            unrecognized += outcome.Unrecognized.Count;

            foreach (var integrationEvent in outcome.Events)
            {
                await dispatcher.DispatchAsync(integrationEvent, cancellationToken);
            }

            await inbox.MarkProcessedAsync(entry.EventId, clock.UtcNow, cancellationToken);
            processed++;
        }

        return new WebhookProcessingSummary(entries.Count, processed, unrecognized);
    }
}

public readonly record struct WebhookProcessingSummary(int Inspected, int Processed, int UnrecognizedFragments);
