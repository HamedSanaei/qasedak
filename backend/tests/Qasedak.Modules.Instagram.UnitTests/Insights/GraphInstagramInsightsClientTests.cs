using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.Insights;
using Qasedak.Modules.Instagram.Application.Media;
using Qasedak.Modules.Instagram.Infrastructure.Graph;
using Qasedak.Modules.Instagram.Infrastructure.Insights;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests.Insights;

/// <summary>
/// Deterministic contract tests for the insights adapter (M13-007): versioned
/// IG-Login paths, Bearer auth through the shared M13-003 transport (token never in
/// URL), exact registry-driven metric selection per surface/media type, availability
/// semantics (real zero vs NoData vs permission), failure taxonomy mapping and
/// redaction. No live Meta calls.
/// </summary>
public sealed class GraphInstagramInsightsClientTests
{
    private const string AccessToken = "INSIGHTS-TOKEN-material";
    private const string ProviderAccount = "17841400000000001";
    private const string ProviderMedia = "17900000000000001";

    private static readonly DateOnly Day = new(2026, 9, 6);

    private static (GraphInstagramInsightsClient Client, List<HttpRequestMessage> Requests) NewClient(
        params HttpResponseMessage[] responses)
    {
        var queue = new Queue<HttpResponseMessage>(responses);
        return NewClient((_, _) => Task.FromResult(
            queue.Count > 0 ? queue.Dequeue() : new HttpResponseMessage(HttpStatusCode.InternalServerError)));
    }

    private static (GraphInstagramInsightsClient Client, List<HttpRequestMessage> Requests) NewClient(
        Func<HttpRequestMessage, int, Task<HttpResponseMessage>> respond)
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new ScriptedInsightsHandler((request, count) =>
        {
            requests.Add(request);
            return respond(request, count);
        });
        return (new GraphInstagramInsightsClient(new HttpClient(handler), Options.Create(new MetaGraphOptions())), requests);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Error(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task AccountInsightsUsesVersionedPathBearerAndExactVerifiedMetrics()
    {
        var (client, requests) = NewClient(Json("""{"data":[]}"""));

        var result = await client.GetAccountInsightsAsync(AccessToken, ProviderAccount, Day, default);

        Assert.IsType<AccountInsightsResult.Ok>(result);
        var sent = Assert.Single(requests);
        Assert.Equal($"https://graph.instagram.com/v26.0/{ProviderAccount}/insights", sent.RequestUri!.GetLeftPart(UriPartial.Path));
        Assert.Equal("Bearer", sent.Headers.Authorization!.Scheme);
        Assert.Equal(AccessToken, sent.Headers.Authorization!.Parameter);
        Assert.DoesNotContain(AccessToken, sent.RequestUri.Query);
        // Exact verified account metric set — one source of truth (registry).
        Assert.Contains(
            Uri.EscapeDataString(string.Join(',', InsightMetricRegistry.AccountMetrics.Select(InsightMetricRegistry.AccountProviderName))),
            sent.RequestUri.Query);
        Assert.Contains("period=day", sent.RequestUri.Query);
        Assert.Contains("metric_type=total_value", sent.RequestUri.Query);
        // since/until bound the requested UTC day.
        var since = new DateTimeOffset(Day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var until = new DateTimeOffset(Day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).ToUnixTimeSeconds();
        Assert.Contains($"since={since}", sent.RequestUri.Query);
        Assert.Contains($"until={until}", sent.RequestUri.Query);
    }

    [Fact]
    public async Task AccountInsightsMapsValuesRealZeroAndNoDataDistinctly()
    {
        var (client, _) = NewClient(Json("""
        {
          "data": [
            { "name": "reach", "period": "day", "total_value": { "value": 224 } },
            { "name": "likes", "period": "day", "total_value": { "value": 0 } },
            { "name": "comments", "period": "day", "total_value": { "value": 3 } }
          ]
        }
        """));

        var result = Assert.IsType<AccountInsightsResult.Ok>(await client.GetAccountInsightsAsync(AccessToken, ProviderAccount, Day, default));

        var observations = result.Metrics.ToDictionary(m => m.Key);
        // Provider-returned zero stays a real zero.
        Assert.Equal(MetricAvailability.Available, observations[InsightMetricKey.Likes].Availability);
        Assert.Equal(0, observations[InsightMetricKey.Likes].Value);
        Assert.Equal(224, observations[InsightMetricKey.Reach].Value);
        // Metrics the provider omitted are NoData — never 0.
        Assert.Equal(MetricAvailability.NoData, observations[InsightMetricKey.AccountsEngaged].Availability);
        Assert.Null(observations[InsightMetricKey.AccountsEngaged].Value);
        Assert.Equal(MetricAvailability.NoData, observations[InsightMetricKey.FollowsAndUnfollows].Availability);
    }

    [Fact]
    public async Task AccountInsightsAcceptsTimeSeriesValuesShape()
    {
        var (client, _) = NewClient(Json("""
        {
          "data": [
            { "name": "reach", "period": "day", "values": [ { "value": 10, "end_time": "2026-09-06T07:00:00+0000" }, { "value": 42 } ] }
          ]
        }
        """));

        var result = Assert.IsType<AccountInsightsResult.Ok>(await client.GetAccountInsightsAsync(AccessToken, ProviderAccount, Day, default));

        var reach = Assert.Single(result.Metrics, m => m.Key == InsightMetricKey.Reach);
        Assert.Equal(MetricAvailability.Available, reach.Availability);
        Assert.Equal(42, reach.Value);
    }

    [Fact]
    public async Task AccountInsightsEmptyDataSetIsNotZero()
    {
        var (client, _) = NewClient(Json("""{"data":[]}"""));

        var result = Assert.IsType<AccountInsightsResult.Ok>(await client.GetAccountInsightsAsync(AccessToken, ProviderAccount, Day, default));

        Assert.All(result.Metrics, m => Assert.Equal(MetricAvailability.NoData, m.Availability));
        Assert.All(result.Metrics, m => Assert.Null(m.Value));
    }

    [Theory]
    [InlineData("""{"error":{"message":"(#10) Permission is not granted","type":"OAuthException","code":10}}""", InsightsFailureKind.PermissionLoss)]
    [InlineData("""{"error":{"message":"Rate limit","type":"OAuthException","code":4}}""", InsightsFailureKind.RateLimited)]
    [InlineData("""{"error":{"message":"Invalid parameter","type":"OAuthException","code":100}}""", InsightsFailureKind.ContractDrift)]
    [InlineData("""{"error":{"message":"Session expired","type":"OAuthException","code":190}}""", InsightsFailureKind.Authentication)]
    public async Task AccountInsightsMapsTaxonomy(string errorBody, InsightsFailureKind expected)
    {
        var (client, _) = NewClient(Error(HttpStatusCode.BadRequest, errorBody));

        var result = await client.GetAccountInsightsAsync(AccessToken, ProviderAccount, Day, default);

        var failed = Assert.IsType<AccountInsightsResult.Failed>(result);
        Assert.Equal(expected, failed.Kind);
    }

    [Fact]
    public async Task AccountInsightsTransientServerFailureMapsToTransient()
    {
        var (client, _) = NewClient(Error(HttpStatusCode.InternalServerError, """{"error":{"message":"down","type":"OAuthException","code":2}}"""));

        var result = await client.GetAccountInsightsAsync(AccessToken, ProviderAccount, Day, default);

        Assert.Equal(InsightsFailureKind.Transient, Assert.IsType<AccountInsightsResult.Failed>(result).Kind);
    }

    [Fact]
    public async Task AccountInsightsMalformedResponseMapsToMalformed()
    {
        var (client, _) = NewClient(Json("""{"nope":true}"""));

        var result = await client.GetAccountInsightsAsync(AccessToken, ProviderAccount, Day, default);

        Assert.Equal(InsightsFailureKind.Malformed, Assert.IsType<AccountInsightsResult.Failed>(result).Kind);
    }

    [Theory]
    [InlineData(MediaKind.Image)]
    [InlineData(MediaKind.Video)]
    [InlineData(MediaKind.Carousel)]
    public async Task MediaInsightsFeedFamilyRequestsExactFeedMetricSet(MediaKind kind)
    {
        var (client, requests) = NewClient(Json("""{"data":[]}"""));

        await client.GetMediaInsightsAsync(AccessToken, ProviderMedia, kind, default);

        var sent = Assert.Single(requests);
        Assert.Equal($"https://graph.instagram.com/v26.0/{ProviderMedia}/insights", sent.RequestUri!.GetLeftPart(UriPartial.Path));
        Assert.Equal("Bearer", sent.Headers.Authorization!.Scheme);
        Assert.DoesNotContain(AccessToken, sent.RequestUri.Query);
        var expected = string.Join(',', InsightMetricRegistry.ForMedia(kind).Select(InsightMetricRegistry.MediaProviderName));
        Assert.Contains(Uri.EscapeDataString(expected), sent.RequestUri.Query);
        // Feed-family media never request reel-only metrics.
        Assert.DoesNotContain("ig_reels_", sent.RequestUri.Query);
        Assert.DoesNotContain("reels_skip_rate", sent.RequestUri.Query);
    }

    [Fact]
    public async Task MediaInsightsReelRequestsReelMetricsIncludingReelOnlyOnes()
    {
        var (client, requests) = NewClient(Json("""{"data":[]}"""));

        await client.GetMediaInsightsAsync(AccessToken, ProviderMedia, MediaKind.Reel, default);

        var sent = Assert.Single(requests);
        var expected = string.Join(',', InsightMetricRegistry.ForMedia(MediaKind.Reel).Select(InsightMetricRegistry.MediaProviderName));
        Assert.Contains(Uri.EscapeDataString(expected), sent.RequestUri!.Query);
        Assert.Contains("ig_reels_avg_watch_time", sent.RequestUri.Query);
        Assert.Contains("reels_skip_rate", sent.RequestUri.Query);
    }

    [Fact]
    public async Task UnknownMediaKindMakesNoProviderRequest()
    {
        var (client, requests) = NewClient();

        var result = await client.GetMediaInsightsAsync(AccessToken, ProviderMedia, MediaKind.Unknown, default);

        Assert.Empty(requests);
        var ok = Assert.IsType<MediaInsightsResult.Ok>(result);
        Assert.Empty(ok.Metrics);
    }

    [Fact]
    public async Task MediaInsightsSubsetResponseLeavesOthersNoDataAndZeroStaysZero()
    {
        var (client, _) = NewClient(Json("""
        {
          "data": [
            { "name": "likes", "period": "lifetime", "values": [ { "value": 0 } ] },
            { "name": "comments", "period": "lifetime", "values": [ { "value": 8 } ] }
          ]
        }
        """));

        var result = Assert.IsType<MediaInsightsResult.Ok>(await client.GetMediaInsightsAsync(AccessToken, ProviderMedia, MediaKind.Image, default));

        var byKey = result.Metrics.ToDictionary(m => m.Key);
        Assert.Equal(MetricAvailability.Available, byKey[InsightMetricKey.Likes].Availability);
        Assert.Equal(0, byKey[InsightMetricKey.Likes].Value); // real zero
        Assert.Equal(8, byKey[InsightMetricKey.Comments].Value);
        Assert.Equal(MetricAvailability.NoData, byKey[InsightMetricKey.Reach].Availability); // omitted ≠ 0
    }

    [Fact]
    public async Task MediaInsightsPermissionAndRateLimitMapToFailures()
    {
        var (client, _) = NewClient(
            Error(HttpStatusCode.BadRequest, """{"error":{"message":"Permission not granted","type":"OAuthException","code":200}}"""));

        var permission = await client.GetMediaInsightsAsync(AccessToken, ProviderMedia, MediaKind.Image, default);
        Assert.Equal(InsightsFailureKind.PermissionLoss, Assert.IsType<MediaInsightsResult.Failed>(permission).Kind);

        var (rateClient, _) = NewClient(
            Error(HttpStatusCode.TooManyRequests, """{"error":{"message":"slow down","type":"OAuthException","code":4}}"""));
        var rate = await rateClient.GetMediaInsightsAsync(AccessToken, ProviderMedia, MediaKind.Image, default);
        Assert.Equal(InsightsFailureKind.RateLimited, Assert.IsType<MediaInsightsResult.Failed>(rate).Kind);
    }

    [Fact]
    public async Task FollowerCountUsesVerifiedAbsoluteFieldOnVersionedAccountNode()
    {
        var (client, requests) = NewClient(Json("""{"followers_count": 1234, "id": "17841400000000001"}"""));

        var result = await client.GetFollowerCountAsync(AccessToken, ProviderAccount, default);

        Assert.Equal(1234, Assert.IsType<FollowerCountResult.Value>(result).FollowerCount);
        var sent = Assert.Single(requests);
        Assert.Equal($"https://graph.instagram.com/v26.0/{ProviderAccount}", sent.RequestUri!.GetLeftPart(UriPartial.Path));
        Assert.Contains("fields=followers_count", sent.RequestUri.Query);
        Assert.DoesNotContain(AccessToken, sent.RequestUri.Query);
        Assert.Equal(AccessToken, sent.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task FollowerCountRealZeroIsAvailableZero()
    {
        var (client, _) = NewClient(Json("""{"followers_count": 0}"""));

        var result = await client.GetFollowerCountAsync(AccessToken, ProviderAccount, default);

        Assert.Equal(0, Assert.IsType<FollowerCountResult.Value>(result).FollowerCount);
    }

    [Fact]
    public async Task FollowerCountMissingFieldIsNoDataNeverZero()
    {
        var (client, _) = NewClient(Json("""{"id":"17841400000000001"}"""));

        var result = await client.GetFollowerCountAsync(AccessToken, ProviderAccount, default);

        Assert.IsType<FollowerCountResult.NoData>(result);
    }

    [Fact]
    public async Task FollowerCountPermissionLossMapsToPermissionLoss()
    {
        var (client, _) = NewClient(
            Error(HttpStatusCode.BadRequest, """{"error":{"message":"(#10) Missing permission","type":"OAuthException","code":10}}"""));

        var result = await client.GetFollowerCountAsync(AccessToken, ProviderAccount, default);

        var failed = Assert.IsType<FollowerCountResult.Failed>(result);
        Assert.Equal(InsightsFailureKind.PermissionLoss, failed.Kind);
        // Provider body/token never surfaces in failure codes.
        Assert.DoesNotContain("Missing permission", failed.FailureCode);
        Assert.DoesNotContain(AccessToken, failed.FailureCode);
    }

    private sealed class ScriptedInsightsHandler(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        private int _count;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var count = Interlocked.Increment(ref _count);
            return respond(request, count);
        }
    }
}
