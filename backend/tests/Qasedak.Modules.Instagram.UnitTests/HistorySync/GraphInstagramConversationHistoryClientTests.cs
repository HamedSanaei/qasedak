using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.HistorySync;
using Qasedak.Modules.Instagram.Infrastructure.Graph;
using Qasedak.Modules.Instagram.Infrastructure.HistorySync;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests.HistorySync;

/// <summary>
/// Deterministic contract tests for the Conversations API adapter (M13-013 §88):
/// versioned paths, platform=instagram, Bearer token, cursor pages, message-id rows,
/// detail mapping (from/to/message/share/unsupported), the older-than-window detail
/// classification, permission/rate/transient failures, cancellation and redaction.
/// No live Meta is ever called.
/// </summary>
public sealed class GraphInstagramConversationHistoryClientTests
{
    private const string AccessToken = "IG-USER-TOKEN-SECRET";
    private const string IgId = "178414000000012345";
    private const string ConversationId = "ggAAAQXXX";
    private const string MessageId = "aWdfZCN1MTIz";

    private static (GraphInstagramConversationHistoryClient Client, ScriptedHttpHandler Handler) NewClient(
        HttpResponseMessage response, string apiVersion = "v26.0")
    {
        var handler = new ScriptedHttpHandler(_ => response);
        var client = new GraphInstagramConversationHistoryClient(
            new HttpClient(handler),
            Options.Create(new MetaGraphOptions { ApiVersion = apiVersion }));
        return (client, handler);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ConversationListUsesVersionedPathPlatformAndBearerToken()
    {
        var (client, handler) = NewClient(Json(
            """{"data":[{"id":"c1","updated_time":1771900500},{"id":"c2","updated_time":1771900501}],"paging":{"cursors":{"after":"QVFIUlJhYmN1"},"next":"https://graph.instagram.com/v26.0/178414000000012345/conversations?platform=instagram&after=QVFIUlJhYmN1"}}"""));

        var result = await client.ListConversationsPageAsync(AccessToken, IgId, 25, null, default);

        var page = Assert.IsType<ConversationListResult.Ok>(result).Page;
        Assert.Equal(2, page.Conversations.Count);
        Assert.Equal("c1", page.Conversations[0].ConversationId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1771900500), page.Conversations[0].UpdatedAtUtc);
        Assert.Equal("QVFIUlJhYmN1", page.NextAfterCursor);
        Assert.True(page.HasMore);
        Assert.StartsWith($"https://graph.instagram.com/v26.0/{IgId}/conversations?platform=instagram", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("Bearer " + AccessToken, handler.LastRequest!.Headers.Authorization!.ToString());
        Assert.DoesNotContain(AccessToken, handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task ConversationListAfterCursorSentAndForeignNextNeverFollowed()
    {
        var (client, handler) = NewClient(Json(
            """{"data":[],"paging":{"cursors":{"after":"abc"},"next":"https://evil.example.com/steal"}}"""));

        var result = await client.ListConversationsPageAsync(AccessToken, IgId, 25, "abc", default);

        var page = Assert.IsType<ConversationListResult.Ok>(result).Page;
        Assert.Equal("abc", page.NextAfterCursor);
        Assert.True(page.HasMore);
        Assert.Contains("after=abc", handler.LastRequest!.RequestUri!.ToString());
        Assert.DoesNotContain("evil.example.com", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task ConversationMessagesExposeIdsCreatedTimeAndUnsupportedFlag()
    {
        var (client, handler) = NewClient(Json(
            """{"messages":{"data":[{"id":"m1","created_time":1771900500},{"id":"m2","created_time":1771900501,"is_unsupported":true}]},"id":"c1"}"""));

        var result = await client.GetConversationMessagesPageAsync(AccessToken, ConversationId, default);

        var rows = Assert.IsType<ConversationMessagesResult.Ok>(result).Messages;
        Assert.Equal(2, rows.Count);
        Assert.Equal("m1", rows[0].MessageId);
        Assert.False(rows[0].IsUnsupported);
        Assert.True(rows[1].IsUnsupported);
        Assert.Equal($"https://graph.instagram.com/v26.0/{ConversationId}?fields=messages", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task MessageDetailMapsFromToAndBody()
    {
        var (client, handler) = NewClient(Json(
            """{"id":"m1","created_time":1771900500,"from":{"id":"52611111111111111","username":"follower"},"to":{"data":[{"id":"178414000000012345","username":"shop"}]},"message":"Hi Kitty!"}"""));

        var result = await client.GetMessageDetailAsync(AccessToken, MessageId, default);

        var detail = Assert.IsType<MessageDetailResult.Ok>(result).Detail;
        Assert.Equal("m1", detail.MessageId);
        Assert.Equal("52611111111111111", detail.FromId);
        Assert.Equal(["178414000000012345"], detail.ToIds);
        Assert.Equal("Hi Kitty!", detail.Body);
        Assert.Null(detail.ShareUrl);
        Assert.False(detail.IsUnsupported);
        Assert.StartsWith($"https://graph.instagram.com/v26.0/{MessageId}?fields=id", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task MessageDetailExposesShareUrlOnlyAndUnsupportedFlag()
    {
        var (client, _) = NewClient(Json(
            """{"id":"m2","created_time":1771900500,"from":{"id":"52611111111111111"},"to":{"data":[{"id":"178414000000012345"}]},"share":{"link":"https://www.instagram.com/p/xyz/"},"is_unsupported":false}"""));

        var result = await client.GetMessageDetailAsync(AccessToken, MessageId, default);

        var detail = Assert.IsType<MessageDetailResult.Ok>(result).Detail;
        Assert.Equal(HistoryShareUrl, detail.ShareUrl);
        Assert.Null(detail.Body);
    }

    [Fact]
    public async Task OlderThanWindowDetailErrorIsClassifiedAsHistoryLimitationNotRetryable()
    {
        // The documented provider shape for messages older than the 20-detail window:
        // a "deleted" error. It must NEVER become a deletion signal or a retry.
        var (client, _) = NewClient(Error(100, httpStatus: 400));

        var result = await client.GetMessageDetailAsync(AccessToken, MessageId, default);

        var failed = Assert.IsType<MessageDetailResult.Failed>(result);
        Assert.Equal(ConversationHistoryFailures.HistoryUnavailableOutsideRecentWindow, failed.FailureCode);
        Assert.False(failed.Transient);
    }

    [Fact]
    public async Task DetailRateLimitIsRetryable()
    {
        var (client, _) = NewClient(Error(4, httpStatus: 429));

        var result = await client.GetMessageDetailAsync(AccessToken, MessageId, default);

        var failed = Assert.IsType<MessageDetailResult.Failed>(result);
        Assert.Equal(ConversationHistoryFailures.RateLimited, failed.FailureCode);
        Assert.True(failed.Transient);
    }

    [Fact]
    public async Task PermissionLossMapsToPermanentPermissionLoss()
    {
        var (client, _) = NewClient(Error(10, httpStatus: 403));

        var result = await client.ListConversationsPageAsync(AccessToken, IgId, 25, null, default);

        var failed = Assert.IsType<ConversationListResult.Failed>(result);
        Assert.Equal(ConversationHistoryFailures.PermissionLoss, failed.FailureCode);
        Assert.False(failed.Transient);
    }

    [Fact]
    public async Task FiveHundredMapsToRetryableUnavailable()
    {
        var (client, _) = NewClient(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{\"error\":{\"message\":\"boom\",\"type\":\"\",\"code\":2}}", Encoding.UTF8, "application/json"),
        });

        var result = await client.GetConversationMessagesPageAsync(AccessToken, ConversationId, default);

        var failed = Assert.IsType<ConversationMessagesResult.Failed>(result);
        Assert.Equal(ConversationHistoryFailures.Unavailable, failed.FailureCode);
        Assert.True(failed.Transient);
    }

    [Fact]
    public async Task MalformedJsonMapsToMalformed()
    {
        var (client, _) = NewClient(Json("not json"));

        var result = await client.GetConversationMessagesPageAsync(AccessToken, ConversationId, default);

        Assert.IsType<ConversationMessagesResult.Failed>(result);
    }

    [Fact]
    public async Task OversizedCursorRejectedWithZeroCalls()
    {
        var (client, handler) = NewClient(Json("{\"data\":[]}"));

        var result = await client.ListConversationsPageAsync(
            AccessToken, IgId, 25, new string('x', ConversationHistoryPolicy.MaxProviderCursorLength + 1), default);

        var failed = Assert.IsType<ConversationListResult.Failed>(result);
        Assert.Equal(ConversationHistoryFailures.CursorOversized, failed.FailureCode);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task CancellationPropagatesWithoutBeingClassifiedAsProviderFailure()
    {
        var client = new GraphInstagramConversationHistoryClient(
            new HttpClient(new CancellationAwareHandler()),
            Options.Create(new MetaGraphOptions()));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.ListConversationsPageAsync(AccessToken, IgId, 25, null, cts.Token));
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

    private const string HistoryShareUrl = "https://www.instagram.com/p/xyz/";

    private static HttpResponseMessage Error(int code, int? subcode = null, int httpStatus = 400) =>
        new((HttpStatusCode)httpStatus)
        {
            Content = new StringContent(
                "{\"error\":{\"message\":\"provider said no\",\"type\":\"OAuthException\",\"code\":" + code +
                ",\"error_subcode\":" + (subcode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null") + ",\"fbtrace_id\":\"TRACE-1\"}}",
                Encoding.UTF8, "application/json"),
        };
}
