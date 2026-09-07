using System.Diagnostics.Metrics;
using Qasedak.Modules.Instagram.Application.RevealFlow;

namespace Qasedak.Modules.Instagram.Infrastructure.RevealFlow;

/// <summary>
/// Reveal-flow observability: one meter with low-cardinality counters. Dimensions are
/// operation and outcome ONLY — never FlowId, CommentId, AccountId, participant, token
/// hash, message text or button content.
/// </summary>
public sealed class RevealFlowMetrics : IRevealFlowObservability, IDisposable
{
    public const string MeterName = "Qasedak.Instagram.RevealFlows";

    private readonly Meter _meter = new(MeterName);

    private readonly Counter<long> _attempted;

    private readonly Counter<long> _succeeded;

    private readonly Counter<long> _uncertain;

    private readonly Counter<long> _failed;

    private readonly Counter<long> _suppressed;

    public RevealFlowMetrics()
    {
        _attempted = _meter.CreateCounter<long>(
            "qasedak.instagram.reveal_flow.attempted",
            unit: "{attempt}",
            description: "Provider mutations issued after the durable attempt marker by operation");
        _succeeded = _meter.CreateCounter<long>(
            "qasedak.instagram.reveal_flow.succeeded",
            unit: "{message}",
            description: "Provider-confirmed deliveries by operation");
        _uncertain = _meter.CreateCounter<long>(
            "qasedak.instagram.reveal_flow.uncertain",
            unit: "{attempt}",
            description: "Ambiguous outcomes — never re-attempted");
        _failed = _meter.CreateCounter<long>(
            "qasedak.instagram.reveal_flow.failed",
            unit: "{attempt}",
            description: "Terminal failures with a stable log-safe code");
        _suppressed = _meter.CreateCounter<long>(
            "qasedak.instagram.reveal_flow.suppressed",
            unit: "{event}",
            description: "Safe no-ops and truthful suppressions (ignored events, ambiguity, correlation rejections)");
    }

    public void Attempted(string operation) =>
        _attempted.Add(1, new KeyValuePair<string, object?>("operation", operation));

    public void Succeeded(string operation) =>
        _succeeded.Add(1, new KeyValuePair<string, object?>("operation", operation));

    public void Uncertain(string operation) =>
        _uncertain.Add(1, new KeyValuePair<string, object?>("operation", operation));

    public void Failed(string operation, string code) =>
        _failed.Add(1,
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("code", code));

    public void Suppressed(string operation, string code) =>
        _suppressed.Add(1,
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("code", code));

    public void Dispose() => _meter.Dispose();
}
