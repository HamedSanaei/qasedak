using Qasedak.Modules.Instagram.Application.Media;

namespace Qasedak.Modules.Instagram.Application.Insights;

/// <summary>
/// Qasedak-owned insight metric keys (M13-007). Provider metric names never leak
/// into Application/API; the mapping lives centrally in
/// <see cref="InsightMetricRegistry"/>. Only metrics verified against the current
/// first-party Instagram Login insights contract (retrieved 2026-09-06) exist here.
/// </summary>
public enum InsightMetricKey
{
    /// <summary>Account: unique accounts that saw content (estimated).</summary>
    Reach,

    /// <summary>Account: accounts that interacted with content, incl. ads (estimated).</summary>
    AccountsEngaged,

    /// <summary>Likes. Account metric sums posts/reels/videos; media metric counts the media object.</summary>
    Likes,

    /// <summary>Comments. Account metric sums posts/reels/videos/live; media metric counts the media object.</summary>
    Comments,

    /// <summary>Saves. Account provider name is "saves"; media provider name is "saved".</summary>
    Saves,

    /// <summary>Shares (account: posts/stories/reels/videos/live; media: the media object).</summary>
    Shares,

    /// <summary>Views (account: content played/displayed; media: times played on Instagram).</summary>
    Views,

    /// <summary>Total interactions (likes+saves+comments+shares minus unlikes/unsaves/deleted).</summary>
    TotalInteractions,

    /// <summary>Reposts (media: reposts minus deleted reposts; account: reposts of posts/stories/reels/videos).</summary>
    Reposts,

    /// <summary>
    /// Account only: combined number of accounts that followed AND unfollowed (or left
    /// Instagram) in the period. A combined count — NOT a net delta and NOT an absolute
    /// follower total. Never used to derive follower history.
    /// </summary>
    FollowsAndUnfollows,

    /// <summary>Reel only: average watch time.</summary>
    IgReelsAvgWatchTime,

    /// <summary>Reel only: total watch time incl. replays.</summary>
    IgReelsVideoViewTotalTime,

    /// <summary>Reel only: percentage of views skipped within the first 3 seconds.</summary>
    ReelsSkipRate,
}

/// <summary>
/// Metric availability state (M13-007). Provider semantics require distinguishing a
/// real zero from missing data: the provider returns an EMPTY data set (never 0) for
/// metrics without data, and some surfaces are unavailable under permission loss.
/// These states are first-class and must never collapse into nullable integers.
/// </summary>
public enum MetricAvailability
{
    /// <summary>The provider returned a value — including a real zero.</summary>
    Available,

    /// <summary>Provider returned no value for the metric (empty data set / omitted metric).</summary>
    NoData,

    /// <summary>Qasedak does not request this metric for the surface/media type by verified contract.</summary>
    Unsupported,

    /// <summary>The insights permission (<c>instagram_business_manage_insights</c>) is missing or revoked.</summary>
    PermissionRequired,

    /// <summary>Transient provider/transport failure; the metric is temporarily unavailable.</summary>
    TemporarilyUnavailable,
}

/// <summary>One metric observation with explicit availability; value is null unless Available.</summary>
public sealed record MetricObservation(InsightMetricKey Key, MetricAvailability Availability, long? Value)
{
    public static MetricObservation Available(InsightMetricKey key, long value) => new(key, MetricAvailability.Available, value);

    public static MetricObservation Unavailable(InsightMetricKey key, MetricAvailability availability) => new(key, availability, null);
}

/// <summary>Outcome of one account-insights request; never throws for provider behavior.</summary>
public abstract record AccountInsightsResult
{
    public sealed record Ok(IReadOnlyList<MetricObservation> Metrics) : AccountInsightsResult;

    public sealed record Failed(InsightsFailureKind Kind, string FailureCode) : AccountInsightsResult;
}

/// <summary>Outcome of one media-insights request; never throws for provider behavior.</summary>
public abstract record MediaInsightsResult
{
    public sealed record Ok(IReadOnlyList<MetricObservation> Metrics) : MediaInsightsResult;

    public sealed record Failed(InsightsFailureKind Kind, string FailureCode) : MediaInsightsResult;
}

/// <summary>Outcome of one direct follower-count observation; never throws for provider behavior.</summary>
public abstract record FollowerCountResult
{
    /// <summary>Provider returned the current absolute follower count for the professional account.</summary>
    public sealed record Value(long FollowerCount) : FollowerCountResult;

    /// <summary>Provider answered but carried no usable value (omitted field); never fabricate.</summary>
    public sealed record NoData(string FailureCode) : FollowerCountResult;

    public sealed record Failed(InsightsFailureKind Kind, string FailureCode) : FollowerCountResult;
}

/// <summary>
/// Classified insights failure kinds over the M13-003 taxonomy. ContractDrift marks a
/// provider rejection of a metric Qasedak believes valid (contract drift observability).
/// </summary>
public enum InsightsFailureKind
{
    PermissionLoss,
    RateLimited,
    Transient,
    Transport,
    Malformed,
    ContractDrift,
    Authentication,
    Other,
}

/// <summary>
/// Focused provider-facing Instagram insights port (M13-007). Callers own
/// exact-account authorization and protected-token retrieval; implementations build
/// versioned Graph requests through the shared M13-003 transport and never expose
/// Meta DTOs. Metric selection is registry-owned, never scattered by callers.
/// </summary>
public interface IInstagramInsightsClient
{
    /// <summary>
    /// Account insights for one UTC day: <c>GET /{IG_ID}/insights</c> with the
    /// registry account metric set, <c>period=day</c>, <c>metric_type=total_value</c>
    /// and <c>since/until</c> bounding the given UTC day.
    /// </summary>
    Task<AccountInsightsResult> GetAccountInsightsAsync(
        string accessToken,
        string providerAccountId,
        DateOnly dayUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Media insights for one top-level media record: <c>GET /{MEDIA_ID}/insights</c>
    /// with the registry metric set for <paramref name="kind"/> (period is provider-fixed
    /// to lifetime). Unknown kinds return Unsupported without any provider request.
    /// </summary>
    Task<MediaInsightsResult> GetMediaInsightsAsync(
        string accessToken,
        string providerMediaId,
        MediaKind kind,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Direct absolute current follower observation: <c>GET /{IG_ID}?fields=followers_count</c>
    /// (IG Login Get Started reference, retrieved 2026-09-06; requires only
    /// <c>instagram_business_basic</c>). This is the verified ABSOLUTE source; the
    /// account-insights <c>follower_count</c> metric is NOT used (its current documented
    /// semantics are a daily delta, not a total — see the contract document §3.8).
    /// </summary>
    Task<FollowerCountResult> GetFollowerCountAsync(
        string accessToken,
        string providerAccountId,
        CancellationToken cancellationToken = default);
}
