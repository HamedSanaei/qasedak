using System.Diagnostics.Metrics;
using Qasedak.Modules.Instagram.Application.Messaging;

namespace Qasedak.Modules.Instagram.Infrastructure.Messaging;

/// <summary>
/// Direct-message send observability: one meter with low-cardinality counters.
/// Dimensions are content kind, button kind, outcome and failure category ONLY —
/// never account ids, recipient ids, message ids, payloads, URLs or message content.
/// </summary>
public sealed class MessageSendMetrics : IMessageSendObservability, IDisposable
{
    public const string MeterName = "Qasedak.Instagram.MessageSends";

    private readonly Meter _meter = new(MeterName);

    private readonly Counter<long> _directSends;

    public MessageSendMetrics()
    {
        _directSends = _meter.CreateCounter<long>(
            "qasedak.instagram.message_send.direct",
            unit: "{send}",
            description: "Direct message sends by content kind, button kind, outcome and failure category");
    }

    public void DirectSend(string contentKind, string buttonKind, string outcome, string? failureCategory)
    {
        var tags = new List<KeyValuePair<string, object?>>(4)
        {
            new("content", contentKind),
            new("button", buttonKind),
            new("outcome", outcome),
        };
        if (failureCategory is not null)
        {
            tags.Add(new KeyValuePair<string, object?>("category", failureCategory));
        }

        _directSends.Add(1, tags.ToArray());
    }

    public void Dispose() => _meter.Dispose();
}
