using Qasedak.BuildingBlocks.Application;

namespace Qasedak.Modules.Instagram.Application.Webhooks;

/// <summary>
/// Consumes pending inbox entries: normalizes each body into explicit integration events,
/// resolves the exact active connected account per webhook entry, enriches events with the
/// Qasedak account key, dispatches every resolvable event, then closes the entry.
///
/// Failure semantics (unchanged, preserved): entries whose payload cannot be dispatched
/// stay pending so failure/retry visibility remains observable; cancellation between
/// normalization and dispatch leaves the entry pending (never marked complete after
/// cancellation); unrecognized, ignored and unresolvable fragments are recorded with
/// observability but do not block closing — the raw body is durably stored and never lost.
/// </summary>
public sealed class ProcessPendingWebhookEventsUseCase(
    IWebhookInboxStore inbox,
    IIntegrationEventDispatcher dispatcher,
    IClock clock,
    IInboundAccountResolver accountResolver,
    IWebhookNormalizationObservability observability)
{
    public async Task<WebhookProcessingSummary> ProcessPendingAsync(int maxEntries = 50, CancellationToken cancellationToken = default)
    {
        var entries = await inbox.ListPendingAsync(maxEntries, cancellationToken);
        var processed = 0;
        var unrecognized = 0;
        var ignored = 0;

        foreach (var entry in entries)
        {
            var outcome = MetaPayloadNormalizer.Normalize(entry.EventId, entry.Topic, entry.BodyJson);

            foreach (var fragment in outcome.TopLevelUnrecognized)
            {
                unrecognized++;
                observability.RecordUnrecognized(fragment.Kind);
            }

            foreach (var fragment in outcome.Entries)
            {
                unrecognized += fragment.Unrecognized.Count;
                ignored += fragment.Ignored.Count;
                foreach (var unrecognizedFragment in fragment.Unrecognized)
                {
                    observability.RecordUnrecognized(unrecognizedFragment.Kind);
                }

                foreach (var ignoredFragment in fragment.Ignored)
                {
                    observability.RecordIgnored(ignoredFragment.Reason);
                }

                // Exact-account resolution is per webhook entry: entry A resolves account A,
                // entry B resolves account B — never a reuse of a previous entry's account.
                var resolution = await accountResolver.ResolveAsync(fragment.ProviderAccountId, cancellationToken);
                if (resolution.Status != InboundAccountStatus.Resolved)
                {
                    // Fail closed: zero dispatch, zero guessed account; observable outcome.
                    var kind = resolution.Status == InboundAccountStatus.Ambiguous ? "ambiguous-account" : "unresolved-account";
                    unrecognized++;
                    observability.RecordUnrecognized(kind);
                    continue;
                }

                foreach (var integrationEvent in fragment.Events)
                {
                    var enriched = Enrich(integrationEvent, resolution);
                    observability.RecordNormalized(KindOf(enriched));
                    await dispatcher.DispatchAsync(enriched, cancellationToken);
                }
            }

            await inbox.MarkProcessedAsync(entry.EventId, clock.UtcNow, cancellationToken);
            processed++;
        }

        return new WebhookProcessingSummary(entries.Count, processed, unrecognized, ignored);
    }

    private static IIntegrationEvent Enrich(IIntegrationEvent integrationEvent, InboundAccountResolution resolution) => integrationEvent switch
    {
        InstagramMessageReceived message => message with { WorkspaceId = resolution.WorkspaceId, ConnectedAccountId = resolution.ConnectedAccountId },
        InstagramCommentCreated comment => comment with { WorkspaceId = resolution.WorkspaceId, ConnectedAccountId = resolution.ConnectedAccountId },
        InstagramMentionCreated mention => mention with { WorkspaceId = resolution.WorkspaceId, ConnectedAccountId = resolution.ConnectedAccountId },
        InstagramPostbackReceived postback => postback with { WorkspaceId = resolution.WorkspaceId, ConnectedAccountId = resolution.ConnectedAccountId },
        InstagramMessageRead read => read with { WorkspaceId = resolution.WorkspaceId, ConnectedAccountId = resolution.ConnectedAccountId },
        _ => integrationEvent,
    };

    private static string KindOf(IIntegrationEvent integrationEvent) => integrationEvent switch
    {
        InstagramMessageReceived => "message",
        InstagramCommentCreated => "comment",
        InstagramMentionCreated => "mention",
        InstagramPostbackReceived => "postback",
        InstagramMessageRead => "read",
        _ => "unknown",
    };
}

public readonly record struct WebhookProcessingSummary(
    int Inspected,
    int Processed,
    int UnrecognizedFragments,
    int IgnoredFragments);
