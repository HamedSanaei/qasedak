using System.Diagnostics.Metrics;
using Qasedak.Modules.Instagram.Infrastructure.Webhooks;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests;

/// <summary>
/// Observability contract: the webhook meter exposes the documented counters and the
/// ingestion duration histogram with their outcome/kind tags, observable through any
/// MeterListener (dashboards and OTLP exporters see exactly this).
/// </summary>
public sealed class WebhookMetricsTests : IDisposable
{
    private readonly WebhookMetrics _metrics = new();
    private readonly MeterListener _listener = new();

    private readonly Dictionary<string, double> _counters = [];

    public WebhookMetricsTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == WebhookMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            var tag = tags.IsEmpty ? string.Empty : $"{tags[0].Key}={tags[0].Value}";
            var key = $"{instrument.Name}|{tag}";
            _counters[key] = _counters.GetValueOrDefault(key) + measurement;
        });
        _listener.Start();
    }

    [Fact]
    public void NotificationOutcomesAreCountedSeparately()
    {
        _metrics.NotificationsReceived.Add(1, new KeyValuePair<string, object?>("outcome", "accepted"));
        _metrics.NotificationsReceived.Add(1, new KeyValuePair<string, object?>("outcome", "rejected"));
        _metrics.NotificationsReceived.Add(1, new KeyValuePair<string, object?>("outcome", "rejected"));

        _listener.RecordObservableInstruments();

        Assert.Equal(1, _counters["qasedak.instagram.webhook.notifications|outcome=accepted"]);
        Assert.Equal(2, _counters["qasedak.instagram.webhook.notifications|outcome=rejected"]);
    }

    [Fact]
    public void DispatchedEventsAreCountedByKind()
    {
        _metrics.EventsDispatched.Add(1, new KeyValuePair<string, object?>("kind", "message"));
        _metrics.EventsDispatched.Add(1, new KeyValuePair<string, object?>("kind", "comment"));

        _listener.RecordObservableInstruments();

        Assert.Equal(1, _counters["qasedak.instagram.webhook.events|kind=message"]);
        Assert.Equal(1, _counters["qasedak.instagram.webhook.events|kind=comment"]);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _metrics.Dispose();
    }
}
