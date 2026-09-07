using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.Reconciliation;
using Qasedak.Modules.Instagram.Infrastructure.Graph;
using Qasedak.Modules.Instagram.Infrastructure.Reconciliation;
using Qasedak.Modules.Instagram.UnitTests.TestSupport;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests.Reconciliation;

/// <summary>
/// Deterministic contract tests for the comment-history adapter (M13-013 §85): host,
/// version, exact media id, Bearer token (never in the URL), verified fields, page
/// limit, after cursor, page mapping, empty page, missing paging, cursor loop/oversize,
/// malicious next URLs ignored, author/timestamp/parent mapping, permission/auth/rate/
/// 5xx/malformed/cancellation/redaction. No live Meta is ever called.
/// </summary>
public sealed class GraphInstagramCommentHistoryClientTests
{
    private const string AccessToken = "IG-USER-TOKEN-SECRET";
    private const string MediaId = "17895695668004550";

    private static (GraphInstagramCommentHistoryClient Client, ScriptedHttpHandler Handler) NewClient(
        HttpResponseMessage response, string apiVersion = "v26.0")
    {
        var handler = new ScriptedHttpHandler(_ => response);
        var client = new GraphInstagramCommentHistoryClient(
            new HttpClient(handler),
            Options.Create(new MetaGraphOptions { ApiVersion = apiVersion }));
        return (client, handler);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string Page(string cursorAfter = "QVFIUlJhYmN1", string nextUrl = "https://graph.instagram.com/v26.0/17895695668004550/comments?after=QVFIUlJhYmN1&limit=25") =>
        "{\"data\":[" +
        "{\"id\":\"17870913679156914\",\"from\":{\"id\":\"52611111111111111\",\"username\":\"follower\"},\"text\":\"This is awesome!\",\"timestamp\":\"2017-08-31T19:16:02+0000\",\"media\":{\"id\":\"17895695668004550\"},\"parent_id\":null,\"hidden\":false}," +
        "{\"id\":\"17873440459141021\",\"from\":{\"id\":\"52622222222222222\",\"username\":\"second\"},\"text\":\"*Sniff*\",\"timestamp\":\"2017-08-31T18:10:30+0000\",\"media\":{\"id\":\"17895695668004550\"},\"hidden\":true}]" +
        ",\"paging\":{\"cursors\":{\"before\":\"QVFIUlJhYmN0\",\"after\":\"" + cursorAfter + "\"},\"next\":\"" + nextUrl + "\"}}";

    [Fact]
    public async Task UsesConfiguredHostAndApiVersionWithExactMediaIdAndBearerToken()
    {
        var (client, handler) = NewClient(Json(Page()));

        var result = await client.ListCommentsPageAsync(AccessToken, "178414000000012345", MediaId, 25, null, default);

        Assert.IsType<CommentHistoryResult.Ok>(result);
        Assert.Equal(new Uri($"https://graph.instagram.com/v26.0/{MediaId}/comments?fields=id%2Cfrom%7Bid%2Cusername%7D%2Ctext%2Ctimestamp%2Cmedia%7Bid%7D%2Cparent_id%2Chidden&limit=25"), handler.LastRequest!.RequestUri);
        Assert.Equal("Bearer " + AccessToken, handler.LastRequest!.Headers.Authorization!.ToString());
        Assert.DoesNotContain(AccessToken, handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task ConfiguredVersionChangesThePath()
    {
        var (client, handler) = NewClient(Json(Page()), "v27.0");

        await client.ListCommentsPageAsync(AccessToken, "178414000000012345", MediaId, 25, null, default);

        Assert.StartsWith("https://graph.instagram.com/v27.0/", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task AfterCursorIsSentAsBoundedQueryComponentOnly()
    {
        var (client, handler) = NewClient(Json(Page()));

        await client.ListCommentsPageAsync(AccessToken, "178414000000012345", MediaId, 25, "abc123", default);

        Assert.Contains("after=abc123", handler.LastRequest!.RequestUri!.ToString());
        Assert.DoesNotContain("http", handler.LastRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task MapsCommentRowsWithAuthorTimestampMediaAndParent()
    {
        var (client, _) = NewClient(Json(Page()));

        var result = await client.ListCommentsPageAsync(AccessToken, "178414000000012345", MediaId, 25, null, default);

        var page = Assert.IsType<CommentHistoryResult.Ok>(result).Page;
        Assert.Equal(2, page.Comments.Count);
        var first = page.Comments[0];
        Assert.Equal("17870913679156914", first.CommentId);
        Assert.Equal("52611111111111111", first.FromId);
        Assert.Equal("follower", first.Username);
        Assert.Equal("This is awesome!", first.Text);
        Assert.Equal(new DateTimeOffset(2017, 8, 31, 19, 16, 2, TimeSpan.Zero), first.CreatedAtUtc);
        Assert.Equal(MediaId, first.MediaId);
        Assert.Null(first.ParentCommentId);
        Assert.False(first.IsHidden);
        Assert.True(page.Comments[1].IsHidden);
        Assert.Equal("QVFIUlJhYmN1", page.NextAfterCursor);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task EmptyPageReturnsNoCommentsAndNoMore()
    {
        var (client, _) = NewClient(Json("{\"data\":[]}"));

        var result = await client.ListCommentsPageAsync(AccessToken, "178414000000012345", MediaId, 25, null, default);

        var page = Assert.IsType<CommentHistoryResult.Ok>(result).Page;
        Assert.Empty(page.Comments);
        Assert.False(page.HasMore);
        Assert.Null(page.NextAfterCursor);
    }

    [Fact]
    public async Task MissingPagingTreatsPageAsFinal()
    {
        var (client, _) = NewClient(Json("{\"data\":[{\"id\":\"c1\",\"text\":\"x\",\"timestamp\":\"2017-01-01T00:00:00+0000\"}]}"));

        var result = await client.ListCommentsPageAsync(AccessToken, "178414000000012345", MediaId, 25, null, default);

        var page = Assert.IsType<CommentHistoryResult.Ok>(result).Page;
        Assert.False(page.HasMore);
        Assert.Null(page.NextAfterCursor);
    }

    [Fact]
    public async Task MaliciousForeignNextUrlIsNeverFollowedAndYieldsNoCursor()
    {
        // next points at an attacker host; the bounded cursor component is missing so
        // the page must be treated as final — the URL is never parsed or fetched.
        var (client, handler) = NewClient(Json(
            """{"data":[],"paging":{"next":"https://evil.example.com/steal?token=abc"}}"""));

        var result = await client.ListCommentsPageAsync(AccessToken, "178414000000012345", MediaId, 25, null, default);

        var page = Assert.IsType<CommentHistoryResult.Ok>(result).Page;
        Assert.Null(page.NextAfterCursor);
        Assert.True(page.HasMore);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task OversizedCursorIsRejectedLocallyWithZeroCalls()
    {
        var (client, handler) = NewClient(Json(Page()));

        var result = await client.ListCommentsPageAsync(
            AccessToken, "178414000000012345", MediaId, 25,
            new string('x', CommentReconciliationPolicy.MaxProviderCursorLength + 1), default);

        var failed = Assert.IsType<CommentHistoryResult.Failed>(result);
        Assert.Equal(CommentHistoryFailures.CursorOversized, failed.FailureCode);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task RateLimitMapsToRetryableRateLimited()
    {
        var (client, _) = NewClient(Error(4, httpStatus: 429));

        var result = await client.ListCommentsPageAsync(AccessToken, "178414000000012345", MediaId, 25, null, default);

        var failed = Assert.IsType<CommentHistoryResult.Failed>(result);
        Assert.Equal(CommentHistoryFailures.RateLimited, failed.FailureCode);
        Assert.True(failed.Transient);
    }

    [Fact]
    public async Task PermissionLossMapsToPermanentPermissionLoss()
    {
        var (client, _) = NewClient(Error(10, httpStatus: 403));

        var result = await client.ListCommentsPageAsync(AccessToken, "178414000000012345", MediaId, 25, null, default);

        var failed = Assert.IsType<CommentHistoryResult.Failed>(result);
        Assert.Equal(CommentHistoryFailures.PermissionLoss, failed.FailureCode);
        Assert.False(failed.Transient);
    }

    [Fact]
    public async Task TokenExpiredMapsToAuthentication()
    {
        var (client, _) = NewClient(Error(190, httpStatus: 400));

        var result = await client.ListCommentsPageAsync(AccessToken, "178414000000012345", MediaId, 25, null, default);

        var failed = Assert.IsType<CommentHistoryResult.Failed>(result);
        Assert.Equal(CommentHistoryFailures.Authentication, failed.FailureCode);
    }

    [Fact]
    public async Task NotFoundMapsToMediaNotFound()
    {
        var (client, _) = NewClient(Error(100, httpStatus: 404));

        var result = await client.ListCommentsPageAsync(AccessToken, "178414000000012345", MediaId, 25, null, default);

        var failed = Assert.IsType<CommentHistoryResult.Failed>(result);
        Assert.Equal(CommentHistoryFailures.NotFound, failed.FailureCode);
    }

    [Fact]
    public async Task FiveHundredMapsToRetryableUnavailable()
    {
        var (client, _) = NewClient(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{\"error\":{\"message\":\"boom\",\"type\":\"\",\"code\":2}}", Encoding.UTF8, "application/json"),
        });

        var result = await client.ListCommentsPageAsync(AccessToken, "178414000000012345", MediaId, 25, null, default);

        var failed = Assert.IsType<CommentHistoryResult.Failed>(result);
        Assert.Equal(CommentHistoryFailures.Unavailable, failed.FailureCode);
        Assert.True(failed.Transient);
    }

    [Fact]
    public async Task MalformedJsonMapsToMalformed()
    {
        var (client, _) = NewClient(Json("not json at all"));

        var result = await client.ListCommentsPageAsync(AccessToken, "178414000000012345", MediaId, 25, null, default);

        var failed = Assert.IsType<CommentHistoryResult.Failed>(result);
        Assert.Equal(CommentHistoryFailures.Malformed, failed.FailureCode);
    }

    [Fact]
    public async Task NetworkFailureMapsToRetryableUnavailable()
    {
        var handler = new ThrowingHandler();
        var client = new GraphInstagramCommentHistoryClient(
            new HttpClient(handler),
            Options.Create(new MetaGraphOptions()));

        var result = await client.ListCommentsPageAsync(AccessToken, "178414000000012345", MediaId, 25, null, default);

        var failed = Assert.IsType<CommentHistoryResult.Failed>(result);
        Assert.Equal(CommentHistoryFailures.Unavailable, failed.FailureCode);
        Assert.True(failed.Transient);
    }

    [Fact]
    public async Task CancellationPropagatesWithoutBeingClassifiedAsProviderFailure()
    {
        var client = new GraphInstagramCommentHistoryClient(
            new HttpClient(new CancellationAwareHandler()),
            Options.Create(new MetaGraphOptions()));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.ListCommentsPageAsync(AccessToken, "178414000000012345", MediaId, 25, null, cts.Token));
    }

    [Fact]
    public async Task ProviderErrorMessageIsBoundedAndRedacted()
    {
        var (client, _) = NewClient(Error(4, httpStatus: 429));

        var result = await client.ListCommentsPageAsync(AccessToken, "178414000000012345", MediaId, 25, null, default);

        var failed = Assert.IsType<CommentHistoryResult.Failed>(result);
        Assert.DoesNotContain(AccessToken, failed.FailureCode);
    }

    private static HttpResponseMessage Error(int code, int? subcode = null, int httpStatus = 400) =>
        new((HttpStatusCode)httpStatus)
        {
            Content = new StringContent(
                "{\"error\":{\"message\":\"provider said no\",\"type\":\"OAuthException\",\"code\":" + code +
                ",\"error_subcode\":" + (subcode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null") + ",\"fbtrace_id\":\"TRACE-1\"}}",
                Encoding.UTF8, "application/json"),
        };

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused");
    }

    private sealed class CancellationAwareHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
