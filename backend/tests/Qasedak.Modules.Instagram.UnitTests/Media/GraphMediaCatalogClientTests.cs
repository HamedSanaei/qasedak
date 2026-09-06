using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.Media;
using Qasedak.Modules.Instagram.Infrastructure.Graph;
using Qasedak.Modules.Instagram.Infrastructure.Media;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests.Media;

/// <summary>
/// Deterministic contract tests for the media catalog adapter (M13-006): versioned
/// IG-Login media path, Bearer auth through the shared transport, no token in URL,
/// verified field set, media-type mapping, nullable semantics, cursor mapping and
/// M13-003 failure mapping. No live Meta calls.
/// </summary>
public sealed class GraphMediaCatalogClientTests
{
    private const string AccessToken = "MEDIA-TOKEN-material";
    private const string ProviderAccount = "17841400000000001";

    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly IMediaCursorCodec _codec = new MediaCatalogCursorCodec();

    private static (GraphMediaCatalogClient Client, List<HttpRequestMessage> Requests) NewClient(
        params HttpResponseMessage[] responses)
    {
        var queue = new Queue<HttpResponseMessage>(responses);
        return NewClient((request, _) => Task.FromResult(queue.Count > 0 ? queue.Dequeue() : new HttpResponseMessage(HttpStatusCode.InternalServerError)));
    }

    private static (GraphMediaCatalogClient Client, List<HttpRequestMessage> Requests) NewClient(
        Func<HttpRequestMessage, int, Task<HttpResponseMessage>> respond)
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new ScriptedMediaHandler((request, count) =>
        {
            requests.Add(request);
            return respond(request, count);
        });
        return (new GraphMediaCatalogClient(new HttpClient(handler), Options.Create(new MetaGraphOptions()), new MediaCatalogCursorCodec()), requests);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Error(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task GetPageUsesVersionedIGLoginPathWithBearerAndNoTokenInUrl()
    {
        var (client, requests) = NewClient(Json("""{"data":[]}"""));

        var result = await client.GetPageAsync(AccessToken, ProviderAccount, AccountId, 25, null, default);

        Assert.IsType<MediaCatalogResult.Ok>(result);
        var sent = Assert.Single(requests);
        Assert.Equal($"https://graph.instagram.com/v26.0/{ProviderAccount}/media", sent.RequestUri!.GetLeftPart(UriPartial.Path));
        Assert.Equal("Bearer", sent.Headers.Authorization!.Scheme);
        Assert.Equal(AccessToken, sent.Headers.Authorization!.Parameter);
        Assert.Equal("limit=25", sent.RequestUri.Query.Substring(sent.RequestUri.Query.IndexOf('&') + 1));
        Assert.Contains("fields=" + Uri.EscapeDataString(MediaCatalogPolicy.Fields), sent.RequestUri.Query);
        Assert.DoesNotContain(AccessToken, sent.RequestUri.Query);
    }

    [Fact]
    public async Task SinglePageMapsAllVerifiedFields()
    {
        var (client, _) = NewClient(Json("""
        {
          "data": [
            {
              "id": "media-1",
              "caption": "یک پست آزمایشی",
              "media_type": "IMAGE",
              "media_url": "https://scontent.example/img1.jpg",
              "permalink": "https://www.instagram.com/p/ABC123/",
              "timestamp": "2026-08-01T10:30:00+0000",
              "like_count": 12,
              "comments_count": 3
            }
          ],
          "paging": { "cursors": { "after": "QVFIUmN1cnNvcnRva2Vu" } }
        }
        """));

        var result = Assert.IsType<MediaCatalogResult.Ok>(await client.GetPageAsync(AccessToken, ProviderAccount, AccountId, 25, null, default));

        var item = Assert.Single(result.Page.Items);
        Assert.Equal("media-1", item.ProviderMediaId);
        Assert.Equal("یک پست آزمایشی", item.Caption);
        Assert.Equal(MediaKind.Image, item.Kind);
        Assert.Null(item.MediaProductType); // never requested/expected on IG Login
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 10, 30, 0, TimeSpan.Zero), item.CreatedAtUtc);
        Assert.Equal("https://www.instagram.com/p/ABC123/", item.Permalink);
        Assert.Equal("https://scontent.example/img1.jpg", item.MediaUrl);
        Assert.True(item.HasMediaPreview);
        Assert.False(item.HasThumbnail);
        Assert.Equal(12, item.LikeCount);
        Assert.Equal(3, item.CommentCount);
        Assert.True(result.Page.HasMore);
        // The returned cursor is a Qasedak envelope, never the raw paging URL/token.
        Assert.NotNull(result.Page.NextCursor);
        Assert.DoesNotContain("paging", result.Page.NextCursor);
        Assert.DoesNotContain("cursors", result.Page.NextCursor);
    }

    [Fact]
    public async Task MediaTypeMappingCoversImageVideoReelCarouselAndUnknown()
    {
        var (client, _) = NewClient(Json("""
        {
          "data": [
            { "id": "m-img", "media_type": "IMAGE" },
            { "id": "m-vid", "media_type": "VIDEO", "thumbnail_url": "https://scontent.example/vid_thumb.jpg" },
            { "id": "m-reel", "media_type": "REELS" },
            { "id": "m-car", "media_type": "CAROUSEL_ALBUM",
              "children": { "data": [ { "id": "c1", "media_type": "IMAGE", "media_url": "https://scontent.example/c1.jpg" }, { "id": "c2", "media_type": "VIDEO" } ] } },
            { "id": "m-future", "media_type": "NEW_FUTURE_TYPE" }
          ]
        }
        """));

        var result = Assert.IsType<MediaCatalogResult.Ok>(await client.GetPageAsync(AccessToken, ProviderAccount, AccountId, 25, null, default));

        Assert.Equal(MediaKind.Image, result.Page.Items[0].Kind);
        Assert.Equal(MediaKind.Video, result.Page.Items[1].Kind);
        Assert.Equal("https://scontent.example/vid_thumb.jpg", result.Page.Items[1].ThumbnailUrl);
        Assert.True(result.Page.Items[1].HasThumbnail);
        Assert.Equal(MediaKind.Reel, result.Page.Items[2].Kind);
        Assert.Equal(MediaKind.Carousel, result.Page.Items[3].Kind);
        Assert.Equal(2, result.Page.Items[3].Children!.Count);
        Assert.Equal("c1", result.Page.Items[3].Children![0].ProviderMediaId);
        Assert.Equal(MediaKind.Image, result.Page.Items[3].Children![0].Kind);
        // Unknown future provider values fail closed to Unknown — no crash.
        Assert.Equal(MediaKind.Unknown, result.Page.Items[4].Kind);
    }

    [Fact]
    public async Task MissingOptionalFieldsDegradeToNullNeverFabricated()
    {
        var (client, _) = NewClient(Json("""
        { "data": [ { "id": "m-1", "media_type": "VIDEO", "timestamp": "not-a-date" } ] }
        """));

        var result = Assert.IsType<MediaCatalogResult.Ok>(await client.GetPageAsync(AccessToken, ProviderAccount, AccountId, 25, null, default));

        var item = Assert.Single(result.Page.Items);
        Assert.Null(item.Caption);
        Assert.Null(item.Permalink);
        Assert.Null(item.MediaUrl);
        Assert.Null(item.ThumbnailUrl);
        Assert.False(item.HasMediaPreview);
        Assert.False(item.HasThumbnail);
        Assert.Null(item.CreatedAtUtc); // unparsable timestamp = unknown, not crash
        Assert.Null(item.LikeCount); // missing count = unknown, never zero
        Assert.Null(item.CommentCount);
    }

    [Fact]
    public async Task UnknownAddedJsonFieldsAreTolerated()
    {
        var (client, _) = NewClient(Json("""
        { "data": [ { "id": "m-1", "media_type": "IMAGE", "future_field": { "nested": [1,2,3] }, "another_future": "x" } ],
          "future_paging": { "weird": true } }
        """));

        var result = Assert.IsType<MediaCatalogResult.Ok>(await client.GetPageAsync(AccessToken, ProviderAccount, AccountId, 25, null, default));

        var item = Assert.Single(result.Page.Items);
        Assert.Equal("m-1", item.ProviderMediaId);
        Assert.Equal(MediaKind.Image, item.Kind);
        Assert.False(result.Page.HasMore);
    }

    [Fact]
    public async Task MalformedIdItemsAreSkippedWithBoundedObservability()
    {
        var (client, _) = NewClient(Json("""
        { "data": [ { "media_type": "IMAGE" }, { "id": "", "media_type": "IMAGE" }, { "id": "ok-id", "media_type": "IMAGE" }, 42 ] }
        """));

        var result = Assert.IsType<MediaCatalogResult.Ok>(await client.GetPageAsync(AccessToken, ProviderAccount, AccountId, 25, null, default));

        var item = Assert.Single(result.Page.Items);
        Assert.Equal("ok-id", item.ProviderMediaId);
    }

    [Fact]
    public async Task CarouselChildrenAreCappedAndNestedNotFlattened()
    {
        var children = string.Join(",", Enumerable.Range(1, 30).Select(i => $"{{\"id\":\"c{i}\",\"media_type\":\"IMAGE\"}}"));
        var (client, _) = NewClient(Json($$"""
        { "data": [ { "id": "m-car", "media_type": "CAROUSEL_ALBUM", "children": { "data": [ {{children}} ] } } ] }
        """));

        var result = Assert.IsType<MediaCatalogResult.Ok>(await client.GetPageAsync(AccessToken, ProviderAccount, AccountId, 25, null, default));

        var item = Assert.Single(result.Page.Items);
        Assert.Equal(MediaCatalogPolicy.MaxCarouselChildren, item.Children!.Count);
        Assert.Equal("c1", item.Children![0].ProviderMediaId);
        Assert.Equal("c10", item.Children![^1].ProviderMediaId);
    }

    [Fact]
    public async Task AfterCursorIsSentOnSubsequentPage()
    {
        var (client, requests) = NewClient(Json("""{"data":[]}"""));

        await client.GetPageAsync(AccessToken, ProviderAccount, AccountId, 25, "raw-provider-after", default);

        var sent = Assert.Single(requests);
        Assert.Contains("after=" + Uri.EscapeDataString("raw-provider-after"), sent.RequestUri!.Query);
    }

    [Fact]
    public async Task CursorRoundTripThroughEnvelopeKeepsAccountBinding()
    {
        var codec = new MediaCatalogCursorCodec();
        var (client, requests) = NewClient(Json("""{"data":[],"paging":{"cursors":{"after":"provider-after-xyz"}}}"""));

        var result = Assert.IsType<MediaCatalogResult.Ok>(await client.GetPageAsync(AccessToken, ProviderAccount, AccountId, 25, null, default));
        Assert.NotNull(result.Page.NextCursor);

        // Same account: decodes; other account: rejected — cursor can never paginate another account.
        Assert.True(codec.Decode(result.Page.NextCursor, AccountId).Valid);
        Assert.False(codec.Decode(result.Page.NextCursor, Guid.NewGuid()).Valid);

        // And the envelope never contains the token or a URL.
        Assert.DoesNotContain(AccessToken, result.Page.NextCursor);

        await client.GetPageAsync(AccessToken, ProviderAccount, AccountId, 25, codec.Decode(result.Page.NextCursor, AccountId).ProviderAfterCursor, default);
        var second = requests[1];
        Assert.Contains("after=" + Uri.EscapeDataString("provider-after-xyz"), second.RequestUri!.Query);
        Assert.DoesNotContain(AccessToken, second.RequestUri!.Query);
    }

    [Fact]
    public async Task RateLimitMapsToRetryableMediaFailure()
    {
        var (client, _) = NewClient(Error(HttpStatusCode.TooManyRequests, """{"error":{"message":"rate limit","type":"OAuthException","code":4}}"""));

        var result = Assert.IsType<MediaCatalogResult.Failed>(await client.GetPageAsync(AccessToken, ProviderAccount, AccountId, 25, null, default));

        Assert.Equal(MediaCatalogFailures.RateLimited, result.FailureCode);
        Assert.True(result.Transient);
    }

    [Fact]
    public async Task PermissionFailureMapsToPermissionDenied()
    {
        var (client, _) = NewClient(Error(HttpStatusCode.BadRequest, """{"error":{"message":"(#10) permission","type":"OAuthException","code":10}}"""));

        var result = Assert.IsType<MediaCatalogResult.Failed>(await client.GetPageAsync(AccessToken, ProviderAccount, AccountId, 25, null, default));

        Assert.Equal(MediaCatalogFailures.PermissionDenied, result.FailureCode);
        Assert.False(result.Transient);
    }

    [Fact]
    public async Task RevokedTokenMapsToNonRetryableUnavailableWithoutLeakingMessage()
    {
        var (client, _) = NewClient(Error(HttpStatusCode.BadRequest, """{"error":{"message":"Session has expired","type":"OAuthException","code":190,"error_subcode":463,"fbtrace_id":"trace-123"}}"""));

        var result = Assert.IsType<MediaCatalogResult.Failed>(await client.GetPageAsync(AccessToken, ProviderAccount, AccountId, 25, null, default));

        Assert.Equal(MediaCatalogFailures.Unavailable, result.FailureCode);
        Assert.False(result.Transient);
    }

    [Fact]
    public async Task MalformedPageShapeFailsClosed()
    {
        var (client, _) = NewClient(Json("""{"unexpected":"shape"}"""));

        var result = Assert.IsType<MediaCatalogResult.Failed>(await client.GetPageAsync(AccessToken, ProviderAccount, AccountId, 25, null, default));

        Assert.Equal(MediaCatalogFailures.Malformed, result.FailureCode);
    }

    [Fact]
    public async Task ProviderErrorBodyNeverLeaksIntoResult()
    {
        var (client, _) = NewClient(Error(HttpStatusCode.BadRequest, """{"error":{"message":"secret access_token=IGSECRET leaked","code":100}}"""));

        var result = Assert.IsType<MediaCatalogResult.Failed>(await client.GetPageAsync(AccessToken, ProviderAccount, AccountId, 25, null, default));

        Assert.Equal(MediaCatalogFailures.Unavailable, result.FailureCode);
        Assert.DoesNotContain("IGSECRET", result.ToString());
        Assert.DoesNotContain(AccessToken, result.ToString());
    }

    // ---------- Recent-N traversal ----------

    [Fact]
    public async Task RecentStopsExactlyAtRequestedNWithoutFetchingNextPage()
    {
        var page1 = Page(Enumerable.Range(1, 10).Select(i => Item($"m{i}")), after: "cursor-1");
        var page2 = Page(Enumerable.Range(11, 10).Select(i => Item($"m{i}")));
        var (client, requests) = NewClient(Json(page1), Json(page2));

        var result = Assert.IsType<MediaCatalogResult.Ok>(
            await client.GetRecentAsync(AccessToken, ProviderAccount, AccountId, 12, default));

        Assert.Equal(12, result.Page.Items.Count);
        Assert.Equal(["m1", "m10", "m11", "m12"], new[] { result.Page.Items[0].ProviderMediaId, result.Page.Items[9].ProviderMediaId, result.Page.Items[10].ProviderMediaId, result.Page.Items[11].ProviderMediaId });
        // N reached on page 2 → page 3 never requested.
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task RecentDeduplicatesOverlappingProviderPages()
    {
        var page1 = Page(Enumerable.Range(1, 5).Select(i => Item($"m{i}")), after: "cursor-1");
        var page2 = Page(new[] { Item("m3"), Item("m4"), Item("m5"), Item("m6"), Item("m7") });
        var (client, requests) = NewClient(Json(page1), Json(page2));

        var result = Assert.IsType<MediaCatalogResult.Ok>(
            await client.GetRecentAsync(AccessToken, ProviderAccount, AccountId, 10, default));

        Assert.Equal(["m1", "m2", "m3", "m4", "m5", "m6", "m7"], result.Page.Items.Select(i => i.ProviderMediaId).ToArray());
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task RecentRejectsRequestAboveHardCeiling()
    {
        var (client, requests) = NewClient(Json("""{"data":[]}"""));

        var result = Assert.IsType<MediaCatalogResult.Failed>(
            await client.GetRecentAsync(AccessToken, ProviderAccount, AccountId, MediaCatalogPolicy.MaxRecentItems + 1, default));

        Assert.Equal(MediaCatalogFailures.InvalidLimit, result.FailureCode);
        Assert.Empty(requests);
    }

    [Fact]
    public async Task RecentCancellationStopsBeforeSecondPageRequest()
    {
        // Page 1 is held open until the test cancels; cancellation must prevent page 2.
        var page1Gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var page2 = Page(Enumerable.Range(26, 25).Select(i => Item($"m{i}")));
        var (client, requests) = NewClient((_, count) => count == 1 ? page1Gate.Task : Task.FromResult(Json(page2)));

        using var cts = new CancellationTokenSource();
        var traversal = client.GetRecentAsync(AccessToken, ProviderAccount, AccountId, 100, cts.Token);
        await Task.Delay(50); // let page 1 reach the provider
        cts.Cancel();
        page1Gate.SetResult(Json(Page(Enumerable.Range(1, 25).Select(i => Item($"m{i}")), after: "cursor-1")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => traversal);

        Assert.Single(requests); // page 2 never requested after cancellation
    }

    [Fact]
    public async Task RecentEmptyPageWithNextCursorTerminatesBounded()
    {
        // Each empty page advances its cursor; the traversal must still stop at MaxPages.
        var (client, requests) = NewClient((_, count) =>
            Task.FromResult(Json(Page([], after: $"cursor-{count}"))));

        var result = Assert.IsType<MediaCatalogResult.Ok>(
            await client.GetRecentAsync(AccessToken, ProviderAccount, AccountId, 10, default));

        Assert.Empty(result.Page.Items);
        Assert.Equal(MediaCatalogPolicy.MaxPages, requests.Count);
    }

    [Fact]
    public async Task RecentRepeatedCursorTerminatesAsMalformed()
    {
        // Every page returns the same next cursor → the traversal must not loop forever.
        var loopingPage = Page([], after: "same-cursor");
        var (client, requests) = NewClient(Json(loopingPage), Json(loopingPage));

        var result = Assert.IsType<MediaCatalogResult.Failed>(
            await client.GetRecentAsync(AccessToken, ProviderAccount, AccountId, 10, default));

        Assert.Equal(MediaCatalogFailures.Malformed, result.FailureCode);
        Assert.Equal(2, requests.Count); // first page + one repeated-cursor attempt, then stops
    }

    [Fact]
    public async Task RecentEmptyPageWithoutCursorTerminatesImmediately()
    {
        var (client, requests) = NewClient(Json("""{"data":[]}"""));

        var result = Assert.IsType<MediaCatalogResult.Ok>(
            await client.GetRecentAsync(AccessToken, ProviderAccount, AccountId, 10, default));

        Assert.Empty(result.Page.Items);
        Assert.Single(requests);
    }

    private static string Page(IEnumerable<string> items, string? after = null)
    {
        var cursors = after is null ? string.Empty : $",\"paging\":{{\"cursors\":{{\"after\":\"{after}\"}}}}";
        return $"{{\"data\":[{string.Join(",", items)}]{cursors}}}";
    }

    private static string Item(string id) => $"{{\"id\":\"{id}\",\"media_type\":\"IMAGE\"}}";

    private sealed class ScriptedMediaHandler(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        private int _count;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var count = Interlocked.Increment(ref _count);
            return respond(request, count);
        }
    }
}
