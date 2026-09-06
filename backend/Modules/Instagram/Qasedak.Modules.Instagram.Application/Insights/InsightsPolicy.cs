namespace Qasedak.Modules.Instagram.Application.Insights;

/// <summary>
/// Configurable insights concurrency bounds (M13-007). Bound from "Instagram:Insights";
/// injectable so tests can prove the configured maximum of simultaneous provider calls.
/// </summary>
public sealed class InsightsOptions
{
    public const string SectionName = "Instagram:Insights";

    /// <summary>
    /// Safe maximum of simultaneous per-media insight provider calls within one
    /// overview request. Provider calls are network-bound; the default of 4 keeps
    /// one overview bounded while still completing ~25 media calls quickly.
    /// </summary>
    public int MaxConcurrentMediaInsights { get; set; } = 4;
}

/// <summary>
/// Central insight bounds (M13-007) — one source of truth; no magic constants
/// scattered across endpoints/adapters/tests. Values derive from the current provider
/// contract (account user metrics retained 90 days, media 2 years; data delayed up to
/// 48h) and Qasedak overview needs: bounded reads, never unbounded fan-out.
/// </summary>
public static class InsightsPolicy
{
    /// <summary>Hard ceiling of media records surfaced in one overview request.</summary>
    public const int OverviewMediaWindow = 25;

    /// <summary>Default number of follower-history points returned.</summary>
    public const int DefaultHistoryLimit = 30;

    /// <summary>Hard per-request history ceiling (matches provider user-metric retention).</summary>
    public const int MaxHistoryLimit = 90;

    /// <summary>Snapshot bootstrap cap per host start; larger fleets are caught on later starts.</summary>
    public const int MaxBootstrapAccountsPerRun = 500;
}
