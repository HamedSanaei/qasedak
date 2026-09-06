using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Qasedak.Modules.Instagram.Application.Webhooks;

namespace Qasedak.Modules.Instagram.Infrastructure.Webhooks;

/// <summary>
/// Module-owned webhook observability: one meter, tag-consistent counters and a backlog
/// gauge. Counter identities are stable contracts for dashboards — do not rename without
/// an ADR note. Implements the Application normalization-observability port so the use
/// case records low-cardinality outcome counters without knowing the meter.
/// </summary>
public sealed class WebhookMetrics : IDisposable, IWebhookNormalizationObservability
{
    public const string MeterName = "Qasedak.Instagram.Webhooks";

    private readonly Meter _meter = new(MeterName);

    /// <summary>Every verified POST attempt, tagged by outcome.</summary>
    public Counter<long> NotificationsReceived { get; }

    /// <summary>Normalized integration events handed to the dispatcher, tagged by kind.</summary>
    public Counter<long> EventsDispatched { get; }

    /// <summary>Redeliveries of already-known event identities.</summary>
    public Counter<long> DuplicateDeliveries { get; }

    /// <summary>End-to-end ingestion duration in milliseconds.</summary>
    public Histogram<double> IngestionDuration { get; }

    /// <summary>Integration events normalized at the processing boundary, tagged by kind (M13-008).</summary>
    public Counter<long> FragmentsNormalized { get; }

    /// <summary>Recognized non-triggering fragments skipped with a reason (M13-008).</summary>
    public Counter<long> FragmentsIgnored { get; }

    /// <summary>Unrecognized/unresolvable fragments recorded with a kind (M13-008).</summary>
    public Counter<long> FragmentsUnrecognized { get; }

    public WebhookMetrics()
    {
        NotificationsReceived = _meter.CreateCounter<long>(
            "qasedak.instagram.webhook.notifications",
            unit: "{notification}",
            description: "Verified webhook notifications by outcome");
        EventsDispatched = _meter.CreateCounter<long>(
            "qasedak.instagram.webhook.events",
            unit: "{event}",
            description: "Normalized integration events dispatched by kind");
        DuplicateDeliveries = _meter.CreateCounter<long>(
            "qasedak.instagram.webhook.duplicates",
            unit: "{notification}",
            description: "Redeliveries of known webhook event identities");
        IngestionDuration = _meter.CreateHistogram<double>(
            "qasedak.instagram.webhook.ingestion.duration",
            unit: "ms",
            description: "Ingestion pipeline duration per notification");
        FragmentsNormalized = _meter.CreateCounter<long>(
            "qasedak.instagram.webhook.normalized",
            unit: "{fragment}",
            description: "Integration events normalized at the processing boundary by kind");
        FragmentsIgnored = _meter.CreateCounter<long>(
            "qasedak.instagram.webhook.ignored",
            unit: "{fragment}",
            description: "Recognized non-triggering fragments skipped by reason");
        FragmentsUnrecognized = _meter.CreateCounter<long>(
            "qasedak.instagram.webhook.unrecognized",
            unit: "{fragment}",
            description: "Unrecognized or unresolvable fragments by kind");
    }

    void IWebhookNormalizationObservability.RecordNormalized(string kind) =>
        FragmentsNormalized.Add(1, new KeyValuePair<string, object?>("kind", kind));

    void IWebhookNormalizationObservability.RecordIgnored(string reason) =>
        FragmentsIgnored.Add(1, new KeyValuePair<string, object?>("reason", reason));

    void IWebhookNormalizationObservability.RecordUnrecognized(string kind) =>
        FragmentsUnrecognized.Add(1, new KeyValuePair<string, object?>("kind", kind));

    /// <summary>
    /// Attaches the inbox-backlog gauge. The observer runs on metric-collection callbacks
    /// and must resolve the store through a fresh scope (hence the hosted-service indirection).
    /// </summary>
    public void AttachPendingBacklogGauge(Func<long> observe) =>
        _meter.CreateObservableGauge(
            "qasedak.instagram.webhook.pending",
            () => new Measurement<long>(observe()),
            "{entry}",
            "Inbox entries awaiting normalization");

    public void Dispose() => _meter.Dispose();
}

/// <summary>Attaches the backlog gauge once the host can scope service resolution.</summary>
public sealed class WebhookBacklogGauge(WebhookMetrics metrics, IServiceScopeFactory scopeFactory) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.AttachPendingBacklogGauge(() =>
        {
            using var scope = scopeFactory.CreateScope();
            return scope.ServiceProvider.GetRequiredService<IWebhookInboxStore>()
                .CountPendingAsync()
                .GetAwaiter()
                .GetResult();
        });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
