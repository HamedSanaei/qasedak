using Qasedak.Modules.Instagram.Application.Media;

namespace Qasedak.Modules.Instagram.Application.Insights;

/// <summary>
/// Central verified insight-metric registry (M13-007). Single source of truth for:
/// which metrics exist, which provider names they map to per surface, and which media
/// product types they are valid for — derived from the current first-party Instagram
/// Login contract (Insights guide, Account Insights and Media Insights references,
/// retrieved 2026-09-06). No metric strings are scattered across adapters, use cases,
/// endpoints, scheduler code or tests. Unknown media kinds return the EMPTY set and
/// never produce a provider request.
///
/// Deliberately excluded (verified but out of M13-007 scope or unsafe):
/// <c>impressions</c> (deprecated), <c>total_*</c> (FB-Login only), story-only metrics
/// (<c>replies</c>, <c>link_clicks</c>, <c>navigation</c> — stories are not in the media
/// catalog), FB-crosspost metrics (<c>crossposted_views</c>/<c>facebook_views</c> — throw
/// when media is not shared to Facebook), demographic metrics (M13-015 scale), media
/// <c>follows</c> (ambiguous per-media semantics — never usable as an absolute total),
/// <c>profile_activity</c>/<c>profile_visits</c> (not needed for the overview).
/// </summary>
public static class InsightMetricRegistry
{
    /// <summary>
    /// Verified account insights metric set (Account Insights reference 2026-06-16):
    /// all support <c>period=day</c> + <c>metric_type=total_value</c>.
    /// </summary>
    public static IReadOnlyList<InsightMetricKey> AccountMetrics { get; } =
    [
        InsightMetricKey.Reach,
        InsightMetricKey.AccountsEngaged,
        InsightMetricKey.Likes,
        InsightMetricKey.Comments,
        InsightMetricKey.Saves,
        InsightMetricKey.Shares,
        InsightMetricKey.Views,
        InsightMetricKey.TotalInteractions,
        InsightMetricKey.Reposts,
        InsightMetricKey.FollowsAndUnfollows,
    ];

    /// <summary>
    /// Valid media metrics for a top-level media record. Carousel CONTAINERs are FEED
    /// posts for insights purposes (album CHILDREN have no insights — the provider
    /// returns none; Qasedak never requests them). Unknown kinds get the empty set:
    /// no unsafe provider request, graceful degradation.
    /// </summary>
    public static IReadOnlyList<InsightMetricKey> ForMedia(MediaKind kind) => kind switch
    {
        MediaKind.Image or MediaKind.Video or MediaKind.Carousel => FeedMetrics,
        MediaKind.Reel => ReelMetrics,
        _ => [],
    };

    private static readonly InsightMetricKey[] FeedMetrics =
    [
        InsightMetricKey.Likes,
        InsightMetricKey.Comments,
        InsightMetricKey.Reach,
        InsightMetricKey.Saves,
        InsightMetricKey.Shares,
        InsightMetricKey.Views,
        InsightMetricKey.TotalInteractions,
        InsightMetricKey.Reposts,
    ];

    private static readonly InsightMetricKey[] ReelMetrics =
    [
        InsightMetricKey.Likes,
        InsightMetricKey.Comments,
        InsightMetricKey.Reach,
        InsightMetricKey.Saves,
        InsightMetricKey.Shares,
        InsightMetricKey.Views,
        InsightMetricKey.TotalInteractions,
        InsightMetricKey.Reposts,
        InsightMetricKey.IgReelsAvgWatchTime,
        InsightMetricKey.IgReelsVideoViewTotalTime,
        InsightMetricKey.ReelsSkipRate,
    ];

    /// <summary>Provider metric name on the account-insights surface.</summary>
    public static string AccountProviderName(InsightMetricKey key) => key switch
    {
        InsightMetricKey.Reach => "reach",
        InsightMetricKey.AccountsEngaged => "accounts_engaged",
        InsightMetricKey.Likes => "likes",
        InsightMetricKey.Comments => "comments",
        InsightMetricKey.Saves => "saves",
        InsightMetricKey.Shares => "shares",
        InsightMetricKey.Views => "views",
        InsightMetricKey.TotalInteractions => "total_interactions",
        InsightMetricKey.Reposts => "reposts",
        InsightMetricKey.FollowsAndUnfollows => "follows_and_unfollows",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Metric is not valid on the account surface."),
    };

    /// <summary>Provider metric name on the media-insights surface (lifetime period).</summary>
    public static string MediaProviderName(InsightMetricKey key) => key switch
    {
        InsightMetricKey.Likes => "likes",
        InsightMetricKey.Comments => "comments",
        InsightMetricKey.Reach => "reach",
        // The media surface spells it "saved" (the account surface spells it "saves").
        InsightMetricKey.Saves => "saved",
        InsightMetricKey.Shares => "shares",
        InsightMetricKey.Views => "views",
        InsightMetricKey.TotalInteractions => "total_interactions",
        InsightMetricKey.Reposts => "reposts",
        InsightMetricKey.IgReelsAvgWatchTime => "ig_reels_avg_watch_time",
        InsightMetricKey.IgReelsVideoViewTotalTime => "ig_reels_video_view_total_time",
        InsightMetricKey.ReelsSkipRate => "reels_skip_rate",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Metric is not valid on the media surface."),
    };

    /// <summary>Stable API-facing metric name (camelCase, Qasedak-owned).</summary>
    public static string ApiName(InsightMetricKey key) => key switch
    {
        InsightMetricKey.Reach => "reach",
        InsightMetricKey.AccountsEngaged => "accountsEngaged",
        InsightMetricKey.Likes => "likes",
        InsightMetricKey.Comments => "comments",
        InsightMetricKey.Saves => "saves",
        InsightMetricKey.Shares => "shares",
        InsightMetricKey.Views => "views",
        InsightMetricKey.TotalInteractions => "totalInteractions",
        InsightMetricKey.Reposts => "reposts",
        InsightMetricKey.FollowsAndUnfollows => "followsAndUnfollows",
        InsightMetricKey.IgReelsAvgWatchTime => "igReelsAvgWatchTime",
        InsightMetricKey.IgReelsVideoViewTotalTime => "igReelsVideoViewTotalTime",
        InsightMetricKey.ReelsSkipRate => "reelsSkipRate",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown metric key."),
    };
}
