using Qasedak.Modules.Instagram.Application.Media;

namespace Qasedak.Modules.Instagram.Application.Insights;

/// <summary>
/// Low-cardinality insights observability port (M13-007). Application code records
/// events; Infrastructure owns the meter. Dimensions never include WorkspaceId,
/// AccountId, ProviderMediaId, usernames, tokens or provider bodies.
/// </summary>
public interface IInsightsObservability
{
    /// <summary>
    /// A metric Qasedak believes valid was rejected by the provider (contract drift).
    /// Dimensions are the Qasedak metric key and the media kind only.
    /// </summary>
    void RecordContractDrift(InsightMetricKey metricKey, MediaKind mediaKind);
}
