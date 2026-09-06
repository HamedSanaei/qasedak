using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.Insights;
using Qasedak.Modules.Instagram.Application.Media;
using Qasedak.Modules.Instagram.Infrastructure.Graph;

namespace Qasedak.Modules.Instagram.Infrastructure.Insights;

/// <summary>
/// M13-007 insights adapter over the shared M13-003 transport. Executes the verified
/// Instagram-Login surfaces:
///
/// <list type="bullet">
/// <item><c>GET {graph}/{version}/{IG_ID}/insights</c> (Account Insights reference,
/// retrieved 2026-09-06; <c>instagram_business_basic</c> +
/// <c>instagram_business_manage_insights</c>, Bearer IG User token, period=day,
/// metric_type=total_value, since/until = one UTC day).</item>
/// <item><c>GET {graph}/{version}/{MEDIA_ID}/insights</c> (Media Insights reference,
/// retrieved 2026-09-06; period is provider-fixed to lifetime).</item>
/// <item><c>GET {graph}/{version}/{IG_ID}?fields=followers_count</c> — the verified
/// ABSOLUTE current follower count (IG Login Get Started guide, retrieved 2026-09-06;
/// <c>instagram_business_basic</c> only).</item>
/// </list>
///
/// Provider metric names come exclusively from <see cref="InsightMetricRegistry"/>;
/// unknown media kinds produce no request. Empty data sets map to NoData (never 0);
/// a real provider 0 maps to Available(0). Failures classify through the M13-003
/// taxonomy; tokens travel only as Bearer headers; no Graph DTO leaves this adapter.
/// </summary>
public sealed class GraphInstagramInsightsClient(
    HttpClient http,
    IOptions<MetaGraphOptions> graphOptions) : IInstagramInsightsClient
{
    public const string HttpClientName = "MetaInstagramInsights";

    private readonly MetaGraphTransport _transport = new(http, graphOptions.Value.TimeoutSeconds);

    private readonly MetaGraphOptions _graph = graphOptions.Value;

    public GraphInstagramInsightsClient(HttpClient http)
        : this(http, Microsoft.Extensions.Options.Options.Create(new MetaGraphOptions()))
    {
    }

    public async Task<AccountInsightsResult> GetAccountInsightsAsync(
        string accessToken,
        string providerAccountId,
        DateOnly dayUtc,
        CancellationToken cancellationToken = default)
    {
        var metrics = string.Join(',', InsightMetricRegistry.AccountMetrics.Select(InsightMetricRegistry.AccountProviderName));
        var since = new DateTimeOffset(dayUtc.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).ToUnixTimeSeconds()
            .ToString(CultureInfo.InvariantCulture);
        var until = new DateTimeOffset(dayUtc.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).ToUnixTimeSeconds()
            .ToString(CultureInfo.InvariantCulture);
        var query = "metric=" + Uri.EscapeDataString(metrics)
            + "&period=day&metric_type=total_value"
            + "&since=" + since + "&until=" + until;

        var endpoint = MetaGraphUris.Versioned(_graph.GraphHost, _graph.ApiVersion, $"{providerAccountId}/insights", query).ToString();
        var outcome = await ExecuteAsync(endpoint, accessToken, cancellationToken);
        if (outcome is MetaGraphCallResult.Success success)
        {
            using (success.Document)
            {
                var parsed = ParseMetricList(success.Document, InsightMetricRegistry.AccountMetrics, InsightMetricRegistry.AccountProviderName);
                return parsed is null
                    ? new AccountInsightsResult.Failed(InsightsFailureKind.Malformed, InsightsFailures.Malformed)
                    : new AccountInsightsResult.Ok(parsed);
            }
        }

        return outcome is MetaGraphCallResult.Rejected rejected
            ? new AccountInsightsResult.Failed(MapFailure(rejected.Error), InsightsFailures.For(rejected.Error))
            : new AccountInsightsResult.Failed(InsightsFailureKind.Transport, InsightsFailures.Unavailable);
    }

    public async Task<MediaInsightsResult> GetMediaInsightsAsync(
        string accessToken,
        string providerMediaId,
        MediaKind kind,
        CancellationToken cancellationToken = default)
    {
        var metrics = InsightMetricRegistry.ForMedia(kind);
        if (metrics.Count == 0)
        {
            // Unknown media kind: no unsafe provider request, graceful degradation.
            return new MediaInsightsResult.Ok([]);
        }

        var query = "metric=" + Uri.EscapeDataString(string.Join(',', metrics.Select(InsightMetricRegistry.MediaProviderName)));
        var endpoint = MetaGraphUris.Versioned(_graph.GraphHost, _graph.ApiVersion, $"{providerMediaId}/insights", query).ToString();
        var outcome = await ExecuteAsync(endpoint, accessToken, cancellationToken);
        if (outcome is MetaGraphCallResult.Success success)
        {
            using (success.Document)
            {
                var parsed = ParseMetricList(success.Document, metrics, InsightMetricRegistry.MediaProviderName);
                return parsed is null
                    ? new MediaInsightsResult.Failed(InsightsFailureKind.Malformed, InsightsFailures.Malformed)
                    : new MediaInsightsResult.Ok(parsed);
            }
        }

        return outcome is MetaGraphCallResult.Rejected rejected
            ? new MediaInsightsResult.Failed(MapFailure(rejected.Error), InsightsFailures.For(rejected.Error))
            : new MediaInsightsResult.Failed(InsightsFailureKind.Transport, InsightsFailures.Unavailable);
    }

    public async Task<FollowerCountResult> GetFollowerCountAsync(
        string accessToken,
        string providerAccountId,
        CancellationToken cancellationToken = default)
    {
        var endpoint = MetaGraphUris.Versioned(_graph.GraphHost, _graph.ApiVersion, providerAccountId, "fields=followers_count").ToString();
        var outcome = await ExecuteAsync(endpoint, accessToken, cancellationToken);
        if (outcome is MetaGraphCallResult.Success success)
        {
            using (success.Document)
            {
                var root = success.Document.RootElement;
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("followers_count", out var value)
                    && value.TryGetInt64(out var count))
                {
                    // A real provider-returned 0 is a valid observation.
                    return new FollowerCountResult.Value(count);
                }

                // Missing/omitted field: never fabricate, never 0.
                return new FollowerCountResult.NoData(InsightsFailures.NoData);
            }
        }

        return outcome is MetaGraphCallResult.Rejected rejected
            ? new FollowerCountResult.Failed(MapFailure(rejected.Error), InsightsFailures.For(rejected.Error))
            : new FollowerCountResult.Failed(InsightsFailureKind.Transport, InsightsFailures.Unavailable);
    }

    private async Task<MetaGraphCallResult> ExecuteAsync(string endpoint, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _transport.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Parses the provider metric list into registry-ordered observations. Both official
    /// value shapes are accepted: <c>total_value.value</c> and <c>values[].value</c>
    /// (last entry). Missing metric → NoData; present numeric (incl. 0) → Available;
    /// malformed root → null (caller maps to Malformed).
    /// </summary>
    private static List<MetricObservation>? ParseMetricList(
        JsonDocument document,
        IReadOnlyList<InsightMetricKey> expected,
        Func<InsightMetricKey, string> providerName)
    {
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var byName = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var entry in data.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("name", out var name)
                || name.GetString() is not { Length: > 0 } metricName)
            {
                continue;
            }

            byName[metricName] = entry;
        }

        var observations = new List<MetricObservation>(expected.Count);
        foreach (var key in expected)
        {
            var provider = providerName(key);
            if (!byName.TryGetValue(provider, out var entry))
            {
                observations.Add(MetricObservation.Unavailable(key, MetricAvailability.NoData));
                continue;
            }

            var value = ReadMetricValue(entry);
            observations.Add(value is null
                ? MetricObservation.Unavailable(key, MetricAvailability.NoData)
                : MetricObservation.Available(key, value.Value));
        }

        return observations;
    }

    private static long? ReadMetricValue(JsonElement entry)
    {
        if (entry.TryGetProperty("total_value", out var total) && total.ValueKind == JsonValueKind.Object
            && total.TryGetProperty("value", out var totalValue) && totalValue.TryGetInt64(out var totalCount))
        {
            return totalCount;
        }

        if (entry.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array)
        {
            long? last = null;
            foreach (var point in values.EnumerateArray())
            {
                if (point.ValueKind == JsonValueKind.Object
                    && point.TryGetProperty("value", out var pointValue)
                    && pointValue.TryGetInt64(out var count))
                {
                    last = count;
                }
            }

            return last;
        }

        return null;
    }

    private static InsightsFailureKind MapFailure(MetaGraphError error) =>
        MetaGraphClassifier.Classify(error) switch
        {
            MetaGraphFailure.PermissionLoss => InsightsFailureKind.PermissionLoss,
            MetaGraphFailure.RateLimited => InsightsFailureKind.RateLimited,
            MetaGraphFailure.Transient => InsightsFailureKind.Transient,
            MetaGraphFailure.TransportFailure => InsightsFailureKind.Transport,
            MetaGraphFailure.InvalidRequest => InsightsFailureKind.ContractDrift,
            MetaGraphFailure.AuthenticationInvalid or MetaGraphFailure.TokenExpired or MetaGraphFailure.Revoked =>
                InsightsFailureKind.Authentication,
            MetaGraphFailure.MalformedResponse => InsightsFailureKind.Malformed,
            _ => InsightsFailureKind.Other,
        };
}

/// <summary>Stable insights failure codes (M13-007); one surface for adapters and tests.</summary>
public static class InsightsFailures
{
    public const string PermissionDenied = "insights.permissionDenied";

    public const string RateLimited = "insights.rateLimited";

    public const string Unavailable = "insights.unavailable";

    public const string Malformed = "insights.malformed";

    public const string NoData = "followers.noData";

    public const string ContractDrift = "insights.contractDrift";

    /// <summary>Bounded provider-derived failure code (never the provider body).</summary>
    public static string For(MetaGraphError error) =>
        MetaGraphClassifier.Classify(error) switch
        {
            MetaGraphFailure.PermissionLoss => PermissionDenied,
            MetaGraphFailure.RateLimited => RateLimited,
            MetaGraphFailure.Transient or MetaGraphFailure.TransportFailure => Unavailable,
            MetaGraphFailure.InvalidRequest => ContractDrift,
            MetaGraphFailure.AuthenticationInvalid or MetaGraphFailure.TokenExpired or MetaGraphFailure.Revoked =>
                "insights.authentication",
            _ => Unavailable,
        };
}
