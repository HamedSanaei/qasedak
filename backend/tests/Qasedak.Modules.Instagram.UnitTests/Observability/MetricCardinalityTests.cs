using System.Diagnostics.Metrics;
using Qasedak.Modules.Instagram.Application.HistorySync;
using Qasedak.Modules.Instagram.Application.Reconciliation;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests;

public sealed class MetricCardinalityTests
{
    private sealed record Measurement(string Name, long Value, IReadOnlyDictionary<string, object?> Tags);

    [Fact]
    public void ReconciliationAndHistoryVolumesAreMeasurementsNotCardinalityTags()
    {
        var records = new List<Measurement>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name is "Qasedak.Instagram.CommentReconciliation" or "Qasedak.Instagram.ConversationHistorySync")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            records.Add(new Measurement(instrument.Name, value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value)));
        });
        listener.Start();
        new CommentReconciliationMetrics().SweepCompleted("completed", mediaScanned: 17, pages: 3);
        new ConversationSyncMetrics().OperationCompleted("manual", "completed", conversations: 5, details: 11);

        string[] forbiddenTags = [
            "workspaceId", "connectedAccountId", "providerAccountId", "commentId",
            "messageId", "conversationId", "participantId", "mediaId", "token",
            "media", "pages", "conversations", "details"
        ];
        var tagNames = records.SelectMany(record => record.Tags.Keys).ToArray();
        foreach (var forbidden in forbiddenTags)
        {
            Assert.DoesNotContain(tagNames, tag => string.Equals(tag, forbidden, StringComparison.OrdinalIgnoreCase));
        }

        Assert.Contains(records, record =>
            record.Name == "qasedak.instagram.comment_reconciliation.scan_volume"
            && record.Value == 17 && Equals(record.Tags["kind"], "media"));
        Assert.Contains(records, record =>
            record.Name == "qasedak.instagram.comment_reconciliation.scan_volume"
            && record.Value == 3 && Equals(record.Tags["kind"], "pages"));
        Assert.Contains(records, record =>
            record.Name == "qasedak.instagram.conversation_history_sync.volume"
            && record.Value == 5 && Equals(record.Tags["kind"], "conversations"));
        Assert.Contains(records, record =>
            record.Name == "qasedak.instagram.conversation_history_sync.volume"
            && record.Value == 11 && Equals(record.Tags["kind"], "details"));
    }
}
