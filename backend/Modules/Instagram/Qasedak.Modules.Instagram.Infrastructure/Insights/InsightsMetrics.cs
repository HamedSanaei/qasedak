using System.Diagnostics;
using System.Diagnostics.Metrics;
using Qasedak.Modules.Instagram.Application.Insights;
using Qasedak.Modules.Instagram.Application.Media;

namespace Qasedak.Modules.Instagram.Infrastructure.Insights;

/// <summary>
/// Module-owned insights observability (M13-007): one meter, low-cardinality counters.
/// Dimensions NEVER include WorkspaceId, AccountId, ProviderMediaId, usernames, tokens
/// or provider bodies — only surface/outcome/state classes.
/// </summary>
public sealed class InsightsMetrics : IInsightsObservability, IDisposable
{
    public const string MeterName = "Qasedak.Instagram.Insights";

    private readonly Meter _meter = new(MeterName);

    /// <summary>Provider request outcomes, tagged by surface + outcome class.</summary>
    public Counter<long> RequestOutcomes { get; }

    /// <summary>Daily snapshot outcomes (observed/noData/retry/terminal).</summary>
    public Counter<long> SnapshotOutcomes { get; }

    /// <summary>Contract-drift events, tagged by metric key + media kind.</summary>
    public Counter<long> ContractDrift { get; }

    public InsightsMetrics()
    {
        RequestOutcomes = _meter.CreateCounter<long>(
            "qasedak.instagram.insights.requests",
            unit: "{request}",
            description: "Insights provider request outcomes by surface and outcome class");
        SnapshotOutcomes = _meter.CreateCounter<long>(
            "qasedak.instagram.insights.snapshots",
            unit: "{snapshot}",
            description: "Daily follower snapshot outcomes by outcome class");
        ContractDrift = _meter.CreateCounter<long>(
            "qasedak.instagram.insights.contract_drift",
            unit: "{event}",
            description: "Provider rejections of centrally verified insight metrics (metric key, media kind)");
    }

    public void RecordContractDrift(InsightMetricKey metricKey, MediaKind mediaKind) =>
        ContractDrift.Add(1,
            new KeyValuePair<string, object?>("metric", InsightMetricRegistry.ApiName(metricKey)),
            new KeyValuePair<string, object?>("mediaKind", mediaKind.ToString()));

    public void Dispose() => _meter.Dispose();
}
