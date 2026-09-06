using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.FollowerSnapshots;
using Qasedak.Modules.Instagram.Application.Media;

namespace Qasedak.Modules.Instagram.Application.Insights;

/// <summary>Account analytics availability as exposed by the overview (truthful degradation).</summary>
public enum InsightsAvailability
{
    /// <summary>Insights permission present; metrics carry per-metric availability.</summary>
    Available,

    /// <summary>
    /// <c>instagram_business_manage_insights</c> missing/revoked: analytics degraded,
    /// media catalog and follower data remain usable.
    /// </summary>
    PermissionRequired,

    /// <summary>Transient provider/transport failure on the analytics surface.</summary>
    TemporarilyUnavailable,
}

/// <summary>Truthful state of the current-follower projection (never silently stale).</summary>
public enum CurrentFollowerState
{
    /// <summary>A directly observed snapshot exists for today (UTC).</summary>
    Available,

    /// <summary>Latest snapshot is from an earlier UTC day: surfaced with provenance/staleness.</summary>
    Stale,

    /// <summary>No snapshot exists yet.</summary>
    NoData,
}

/// <summary>Media-section state for the overview (degradation without discarding results).</summary>
public enum MediaOverviewState
{
    Available,
    PermissionDenied,
    TemporarilyUnavailable,
    NoData,
}

/// <summary>One current-follower projection; value null unless state != NoData.</summary>
public sealed record CurrentFollowersModel(long? Value, CurrentFollowerState State, DateTimeOffset? ObservedAtUtc, FollowerSnapshotProvenance? Provenance);

/// <summary>One durable follower-history point.</summary>
public sealed record FollowerHistoryPoint(DateOnly Date, long Value, FollowerSnapshotProvenance Provenance);

/// <summary>One media record in the overview with per-metric insight availability.</summary>
public sealed record OverviewMediaItem(
    string ProviderMediaId,
    MediaKind Kind,
    int? LikeCount,
    int? CommentCount,
    IReadOnlyList<MetricObservation> Insights);

/// <summary>Bounded overview media section; items null unless state is Available.</summary>
public sealed record OverviewMediaModel(
    MediaOverviewState State,
    int? Count,
    long? LikeTotal,
    long? CommentTotal,
    bool LikeTotalComplete,
    bool CommentTotalComplete,
    IReadOnlyList<OverviewMediaItem>? Items);

/// <summary>Exact-account analytics overview read model (Qasedak-owned, token-free DTO).</summary>
public sealed record InstagramOverview(
    Guid AccountId,
    InsightsAvailability AnalyticsAvailability,
    CurrentFollowersModel CurrentFollowers,
    IReadOnlyList<FollowerHistoryPoint> FollowerHistory,
    OverviewMediaModel Media,
    IReadOnlyList<MetricObservation> AccountInsights);

/// <summary>Outcome of an overview request.</summary>
public abstract record InstagramOverviewResult
{
    public sealed record Ok(InstagramOverview Overview) : InstagramOverviewResult;

    public sealed record Refused(string FailureCode) : InstagramOverviewResult;
}

/// <summary>Outcome of a follower-history request (pure read model).</summary>
public abstract record FollowerHistoryResult
{
    public sealed record Ok(IReadOnlyList<FollowerHistoryPoint> Points) : FollowerHistoryResult;

    public sealed record Refused(string FailureCode) : FollowerHistoryResult;
}

/// <summary>
/// M13-007 exact-account overview. Executes the exact-account contract before any
/// provider interaction (find → workspace ownership → active → THIS account's token),
/// then: account insights (one call) → bounded recent media catalog (M13-006 port) →
/// per-media insights with bounded concurrency and cancellation → local snapshot
/// history (DB). Provider calls happen OUTSIDE any database write transaction.
///
/// Degradation rules: account-level insights permission loss disables only analytics —
/// media catalog items/basic counts, follower data and history survive; per-media
/// insight failures degrade only that media (never discarding successful results); an
/// unknown media kind never produces a provider request; empty/missing metrics are
/// NoData, never 0. No provider DTO, token or raw error body leaves this boundary.
/// </summary>
public sealed class GetInstagramOverviewUseCase(
    IConnectedAccountRepository accounts,
    IProtectedTokenStore tokens,
    IMediaCatalogClient media,
    IInstagramInsightsClient insights,
    IFollowerSnapshotStore snapshots,
    InsightsOptions insightsOptions,
    IInsightsObservability observability,
    Qasedak.BuildingBlocks.Application.IClock clock)
{
    public async Task<InstagramOverviewResult> ExecuteAsync(
        Guid workspaceId,
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var account = await accounts.FindByIdAsync(accountId, cancellationToken);
        if (account is null || account.WorkspaceId != workspaceId)
        {
            // Foreign/unknown account: 404, zero token read, zero provider call.
            return new InstagramOverviewResult.Refused(AccountFailures.NotFound);
        }

        if (account.IsDisconnected)
        {
            return new InstagramOverviewResult.Refused(AccountFailures.AlreadyDisconnected);
        }

        var accessToken = await tokens.GetAsync(account.Id, cancellationToken);
        if (string.IsNullOrEmpty(accessToken))
        {
            return new InstagramOverviewResult.Refused(AccountFailures.TokenMissing);
        }

        var today = FollowerSnapshotPolicy.UtcDay(clock.UtcNow);
        var latest = await snapshots.GetLatestAsync(account.Id, cancellationToken);
        var history = await snapshots.ListHistoryAsync(account.Id, InsightsPolicy.DefaultHistoryLimit, cancellationToken);
        var currentFollowers = latest is null
            ? new CurrentFollowersModel(null, CurrentFollowerState.NoData, null, null)
            : new CurrentFollowersModel(
                latest.FollowerCount,
                latest.SnapshotDateUtc >= today ? CurrentFollowerState.Available : CurrentFollowerState.Stale,
                latest.ObservedAtUtc,
                latest.Provenance);
        var historyPoints = history
            .Select(point => new FollowerHistoryPoint(point.SnapshotDateUtc, point.FollowerCount, point.Provenance))
            .ToList();

        // Account insights first: a clear account-level permission loss stops the media
        // insight fan-out (no provider amplification); media catalog and followers
        // remain intact.
        var accountResult = await insights.GetAccountInsightsAsync(
            accessToken, account.ProviderUserId, today, cancellationToken);
        InsightsAvailability analyticsAvailability;
        IReadOnlyList<MetricObservation> accountMetrics;
        var skipMediaInsights = false;
        switch (accountResult)
        {
            case AccountInsightsResult.Ok ok:
                analyticsAvailability = InsightsAvailability.Available;
                accountMetrics = ok.Metrics;
                break;
            case AccountInsightsResult.Failed accountFailure when accountFailure.Kind == InsightsFailureKind.PermissionLoss:
                analyticsAvailability = InsightsAvailability.PermissionRequired;
                accountMetrics = AllUnavailable(InsightMetricRegistry.AccountMetrics, MetricAvailability.PermissionRequired);
                skipMediaInsights = true;
                break;
            case AccountInsightsResult.Failed accountFailure when accountFailure.Kind is InsightsFailureKind.ContractDrift:
                foreach (var metric in InsightMetricRegistry.AccountMetrics)
                {
                    observability.RecordContractDrift(metric, MediaKind.Unknown);
                }

                analyticsAvailability = InsightsAvailability.Available;
                accountMetrics = AllUnavailable(InsightMetricRegistry.AccountMetrics, MetricAvailability.NoData);
                break;
            default:
                analyticsAvailability = InsightsAvailability.TemporarilyUnavailable;
                accountMetrics = AllUnavailable(InsightMetricRegistry.AccountMetrics, MetricAvailability.TemporarilyUnavailable);
                break;
        }

        var mediaResult = await media.GetRecentAsync(
            accessToken, account.ProviderUserId, account.Id, InsightsPolicy.OverviewMediaWindow, cancellationToken);
        if (mediaResult is MediaCatalogResult.Failed mediaFailure)
        {
            var state = mediaFailure.FailureCode == MediaCatalogFailures.PermissionDenied
                ? MediaOverviewState.PermissionDenied
                : mediaFailure.Transient
                    ? MediaOverviewState.TemporarilyUnavailable
                    : MediaOverviewState.NoData;
            return new InstagramOverviewResult.Ok(new InstagramOverview(
                account.Id, analyticsAvailability, currentFollowers, historyPoints,
                new OverviewMediaModel(state, null, null, null, false, false, null),
                accountMetrics));
        }

        var items = ((MediaCatalogResult.Ok)mediaResult).Page.Items;
        List<(MediaCatalogItem Item, IReadOnlyList<MetricObservation> Insights)> mediaInsights;
        if (skipMediaInsights)
        {
            mediaInsights = items
                .Select(item => (item, (IReadOnlyList<MetricObservation>)AllUnavailable(
                    InsightMetricRegistry.ForMedia(item.Kind), MetricAvailability.PermissionRequired)))
                .ToList();
        }
        else
        {
            mediaInsights = await FetchMediaInsightsAsync(accessToken, items, cancellationToken);
        }

        var (likeTotal, likeComplete) = SumCounts(items.Select(i => i.LikeCount));
        var (commentTotal, commentComplete) = SumCounts(items.Select(i => i.CommentCount));

        return new InstagramOverviewResult.Ok(new InstagramOverview(
            account.Id,
            analyticsAvailability,
            currentFollowers,
            historyPoints,
            new OverviewMediaModel(
                MediaOverviewState.Available,
                items.Count,
                likeTotal,
                commentTotal,
                likeComplete,
                commentComplete,
                items.Select((item, index) => new OverviewMediaItem(
                    item.ProviderMediaId,
                    item.Kind,
                    item.LikeCount,
                    item.CommentCount,
                    mediaInsights[index].Insights)).ToList()),
            accountMetrics));
    }

    /// <summary>
    /// Bounded per-media insight fan-out: at most <c>MaxConcurrentMediaInsights</c>
    /// simultaneous provider calls, cancellation-aware (queued calls never start after
    /// cancellation; in-flight calls receive the token). One media's failure degrades
    /// only that media.
    /// </summary>
    private async Task<List<(MediaCatalogItem Item, IReadOnlyList<MetricObservation> Insights)>> FetchMediaInsightsAsync(
        string accessToken,
        IReadOnlyList<MediaCatalogItem> items,
        CancellationToken cancellationToken)
    {
        var maxConcurrent = Math.Max(1, insightsOptions.MaxConcurrentMediaInsights);
        using var gate = new SemaphoreSlim(maxConcurrent);
        var results = new (MediaCatalogItem Item, IReadOnlyList<MetricObservation> Insights)?[items.Count];

        var tasks = items.Select(async (item, index) =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                // WaitAsync(token) can still grant the slot when cancellation and a
                // slot-release race; re-check before any provider call so queued
                // calls never start after cancellation.
                cancellationToken.ThrowIfCancellationRequested();

                var metrics = InsightMetricRegistry.ForMedia(item.Kind);
                if (metrics.Count == 0)
                {
                    // Unknown media kind: no unsafe provider request; explicit
                    // Unsupported availability with no provider call.
                    results[index] = (item, AllUnavailable([], MetricAvailability.Unsupported));
                    return;
                }

                var result = await insights.GetMediaInsightsAsync(accessToken, item.ProviderMediaId, item.Kind, cancellationToken);
                IReadOnlyList<MetricObservation> observations = result switch
                {
                    MediaInsightsResult.Ok ok => ok.Metrics,
                    MediaInsightsResult.Failed mediaFailure when mediaFailure.Kind == InsightsFailureKind.PermissionLoss =>
                        AllUnavailable(metrics, MetricAvailability.PermissionRequired),
                    MediaInsightsResult.Failed mediaFailure when mediaFailure.Kind is InsightsFailureKind.RateLimited
                        or InsightsFailureKind.Transient or InsightsFailureKind.Transport =>
                        AllUnavailable(metrics, MetricAvailability.TemporarilyUnavailable),
                    MediaInsightsResult.Failed mediaFailure => MapOtherMediaFailure(mediaFailure, metrics, item.Kind),
                    _ => AllUnavailable(metrics, MetricAvailability.NoData),
                };
                results[index] = (item, observations);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results.Select(entry => entry!.Value).ToList();
    }

    private List<MetricObservation> MapOtherMediaFailure(
        MediaInsightsResult.Failed failure,
        IReadOnlyList<InsightMetricKey> metrics,
        MediaKind kind)
    {
        if (failure.Kind == InsightsFailureKind.ContractDrift)
        {
            foreach (var metric in metrics)
            {
                // Low-cardinality drift observability: metric key + media kind only —
                // never account identifiers or provider bodies.
                observability.RecordContractDrift(metric, kind);
            }
        }

        return AllUnavailable(metrics, MetricAvailability.NoData);
    }

    private static List<MetricObservation> AllUnavailable(IReadOnlyList<InsightMetricKey> keys, MetricAvailability availability) =>
        keys.Select(key => MetricObservation.Unavailable(key, availability)).ToList();

    private static (long? Total, bool Complete) SumCounts(IEnumerable<int?> counts)
    {
        var present = 0L;
        var missing = false;
        foreach (var count in counts)
        {
            if (count is null)
            {
                missing = true;
            }
            else
            {
                present += count.Value;
            }
        }

        // Totals are exposed only when every counted item reported a value: a sum over
        // unknown entries would be misleading.
        return missing ? (null, false) : (present, true);
    }
}

/// <summary>
/// M13-007 follower-history read model. Exact-account gate; DB-only (no token read, no
/// provider call). Disconnected accounts are refused uniformly with other Instagram
/// surfaces (documented decision: history requires an active connection).
/// </summary>
public sealed class GetFollowerHistoryUseCase(
    IConnectedAccountRepository accounts,
    IFollowerSnapshotStore snapshots)
{
    public async Task<FollowerHistoryResult> ExecuteAsync(
        Guid workspaceId,
        Guid accountId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var account = await accounts.FindByIdAsync(accountId, cancellationToken);
        if (account is null || account.WorkspaceId != workspaceId)
        {
            return new FollowerHistoryResult.Refused(AccountFailures.NotFound);
        }

        if (account.IsDisconnected)
        {
            return new FollowerHistoryResult.Refused(AccountFailures.AlreadyDisconnected);
        }

        var points = await snapshots.ListHistoryAsync(accountId, limit, cancellationToken);
        return new FollowerHistoryResult.Ok(points
            .Select(point => new FollowerHistoryPoint(point.SnapshotDateUtc, point.FollowerCount, point.Provenance))
            .ToList());
    }
}
