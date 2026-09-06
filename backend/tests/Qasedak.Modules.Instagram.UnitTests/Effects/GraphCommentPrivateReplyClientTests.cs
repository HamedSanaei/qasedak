using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.Effects;
using Qasedak.Modules.Instagram.Infrastructure.Effects;
using Qasedak.Modules.Instagram.Infrastructure.Graph;
using Qasedak.Modules.Instagram.Infrastructure.Messaging;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests.Effects;

/// <summary>
/// Deterministic contract tests for the comment Private Reply adapter (M13-009 §75):
/// versioned IG_ID path, POST, Bearer token, comment_id addressing — never recipient.id —
/// success identity parsing, malformed success, official rejections, transport failure,
/// cancellation and redaction. No live Meta is ever called.
/// </summary>
public sealed class GraphCommentPrivateReplyClientTests
{
    private const string AccessToken = "IG-USER-TOKEN-SECRET";
    private const string IgId = "178414000000012345";
    private const string CommentId = "17895695792288124";

    private static (GraphCommentPrivateReplyClient Client, ScriptedHttpHandler Handler) NewClient(
        HttpResponseMessage response,
        string apiVersion = "v26.0")
    {
        var handler = new ScriptedHttpHandler(_ => response);
        var client = new GraphCommentPrivateReplyClient(
            new HttpClient(handler),
            Options.Create(new MetaMessagingOptions()),
            Options.Create(new MetaGraphOptions { ApiVersion = apiVersion }));
        return (client, handler);
    }

    private static HttpResponseMessage Ok(string recipientId = "52612345678901234", string messageId = "aWdfZCN1MTIz") =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"recipient_id":"{{recipientId}}","message_id":"{{messageId}}"}""", Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage Error(int code, int? subcode = null, int httpStatus = 400) =>
        new((HttpStatusCode)httpStatus)
        {
            Content = new StringContent(
                "{\"error\":{\"message\":\"provider said no\",\"type\":\"OAuthException\",\"code\":" + code +
                ",\"error_subcode\":" + (subcode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null") + ",\"fbtrace_id\":\"TRACE-1\"}}",
                Encoding.UTF8, "application/json"),
        };

    [Fact]
    public async Task UsesConfiguredHostAndApiVersionWithIgIdPath()
    {
        var (client, handler) = NewClient(Ok());

        var result = await client.SendPrivateReplyAsync(AccessToken, IgId, CommentId, "thanks!", default);

        Assert.True(result.Succeeded);
        Assert.Equal(new Uri($"https://graph.instagram.com/v26.0/{IgId}/messages"), handler.LastRequest!.RequestUri);
    }

    [Fact]
    public async Task ConfiguredVersionChangesThePath()
    {
        var (client, handler) = NewClient(Ok(), apiVersion: "v27.0");

        await client.SendPrivateReplyAsync(AccessToken, IgId, CommentId, "t", default);

        Assert.Equal(new Uri($"https://graph.instagram.com/v27.0/{IgId}/messages"), handler.LastRequest!.RequestUri);
    }

    [Fact]
    public async Task UsesConfiguredGraphBaseUrl()
    {
        var handler = new ScriptedHttpHandler(_ => Ok());
        var client = new GraphCommentPrivateReplyClient(
            new HttpClient(handler),
            Options.Create(new MetaMessagingOptions { GraphBaseUrl = "https://graph.example.test" }),
            Options.Create(new MetaGraphOptions()));

        await client.SendPrivateReplyAsync(AccessToken, IgId, CommentId, "t", default);

        Assert.Equal(new Uri($"https://graph.example.test/v26.0/{IgId}/messages"), handler.LastRequest!.RequestUri);
    }

    [Fact]
    public async Task PostsWithBearerTokenInHeaderOnly()
    {
        var (client, handler) = NewClient(Ok());

        await client.SendPrivateReplyAsync(AccessToken, IgId, CommentId, "t", default);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal(AccessToken, handler.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task TokenNeverAppearsInUrlOrBody()
    {
        var (client, handler) = NewClient(Ok());

        await client.SendPrivateReplyAsync(AccessToken, IgId, CommentId, "t", default);

        Assert.DoesNotContain(AccessToken, handler.LastRequest!.RequestUri!.ToString());
        Assert.DoesNotContain(AccessToken, handler.LastBody);
    }

    [Fact]
    public async Task AddressesRecipientByCommentId()
    {
        var (client, handler) = NewClient(Ok());

        await client.SendPrivateReplyAsync(AccessToken, IgId, CommentId, "t", default);

        Assert.Contains($$"""{"recipient":{"comment_id":"{{CommentId}}"}""", handler.LastBody);
    }

    [Fact]
    public async Task NeverSendsRecipientId()
    {
        var (client, handler) = NewClient(Ok());

        await client.SendPrivateReplyAsync(AccessToken, IgId, CommentId, "t", default);

        Assert.DoesNotContain("\"id\":", handler.LastBody);
        Assert.DoesNotContain("recipient_id", handler.LastBody);
    }

    [Fact]
    public async Task SendsExactMessageText()
    {
        var (client, handler) = NewClient(Ok());

        await client.SendPrivateReplyAsync(AccessToken, IgId, CommentId, "DM: thanks for asking!", default);

        Assert.Contains("\"message\":{\"text\":\"DM: thanks for asking!\"}", handler.LastBody);
    }

    [Fact]
    public async Task SuccessParsesRecipientAndMessageIdentity()
    {
        var (client, _) = NewClient(Ok("526-customer-1", "mid-abc-42"));

        var result = await client.SendPrivateReplyAsync(AccessToken, IgId, CommentId, "t", default);

        Assert.True(result.Succeeded);
        Assert.Equal("526-customer-1", result.RecipientId);
        Assert.Equal("mid-abc-42", result.MessageId);
    }

    [Fact]
    public async Task MissingMessageIdIsMalformedSuccess()
    {
        var (client, _) = NewClient(new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"recipient_id":"526-customer-1"}""", Encoding.UTF8, "application/json"),
        });

        var result = await client.SendPrivateReplyAsync(AccessToken, IgId, CommentId, "t", default);

        Assert.False(result.Succeeded);
        Assert.Equal(PrivateReplyFailureReason.MalformedResponse, result.Failure!.Reason);
    }

    [Fact]
    public async Task MissingRecipientIdIsMalformedSuccess()
    {
        var (client, _) = NewClient(new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"message_id":"mid-abc"}""", Encoding.UTF8, "application/json"),
        });

        var result = await client.SendPrivateReplyAsync(AccessToken, IgId, CommentId, "t", default);

        Assert.False(result.Succeeded);
        Assert.Equal(PrivateReplyFailureReason.MalformedResponse, result.Failure!.Reason);
    }

    [Fact]
    public async Task NonJsonTwoHundredIsRejectedNotMalformedOrCrash()
    {
        // The shared transport cannot parse a 2xx non-JSON payload; the adapter must
        // surface a structured rejection, never throw.
        var (client, _) = NewClient(new(HttpStatusCode.OK) { Content = new StringContent("not json") });

        var result = await client.SendPrivateReplyAsync(AccessToken, IgId, CommentId, "t", default);

        Assert.False(result.Succeeded);
        Assert.Equal(PrivateReplyFailureReason.RejectedByMeta, result.Failure!.Reason);
    }

    [Fact]
    public async Task PermissionRejectionSurfacesAsRejectedByMetaWithRedactedDetail()
    {
        var (client, _) = NewClient(Error(10, subcode: 33));

        var result = await client.SendPrivateReplyAsync(AccessToken, IgId, CommentId, "t", default);

        Assert.False(result.Succeeded);
        Assert.Equal(PrivateReplyFailureReason.RejectedByMeta, result.Failure!.Reason);
        Assert.DoesNotContain(AccessToken, result.Failure!.Detail);
    }

    [Fact]
    public async Task RateLimitSurfacesAsRejectedByMeta()
    {
        var (client, _) = NewClient(Error(4, httpStatus: 429));

        var result = await client.SendPrivateReplyAsync(AccessToken, IgId, CommentId, "t", default);

        Assert.False(result.Succeeded);
        Assert.Equal(PrivateReplyFailureReason.RejectedByMeta, result.Failure!.Reason);
    }

    [Fact]
    public async Task FiveHundredSeriesSurfacesAsRejectedByMeta()
    {
        var (client, _) = NewClient(Error(2, httpStatus: 500));

        var result = await client.SendPrivateReplyAsync(AccessToken, IgId, CommentId, "t", default);

        Assert.False(result.Succeeded);
        Assert.Equal(PrivateReplyFailureReason.RejectedByMeta, result.Failure!.Reason);
    }

    [Fact]
    public async Task TransportTimeoutIsTransportFailure()
    {
        var handler = new ScriptedHttpHandler(_ => throw new TaskCanceledException("timeout"));
        var client = new GraphCommentPrivateReplyClient(
            new HttpClient(handler),
            Options.Create(new MetaMessagingOptions()),
            Options.Create(new MetaGraphOptions()));

        var result = await client.SendPrivateReplyAsync(AccessToken, IgId, CommentId, "t", default);

        Assert.False(result.Succeeded);
        Assert.Equal(PrivateReplyFailureReason.TransportFailure, result.Failure!.Reason);
    }

    [Fact]
    public async Task NetworkFailureIsTransportFailure()
    {
        var handler = new ScriptedHttpHandler(_ => throw new HttpRequestException("refused"));
        var client = new GraphCommentPrivateReplyClient(
            new HttpClient(handler),
            Options.Create(new MetaMessagingOptions()),
            Options.Create(new MetaGraphOptions()));

        var result = await client.SendPrivateReplyAsync(AccessToken, IgId, CommentId, "t", default);

        Assert.False(result.Succeeded);
        Assert.Equal(PrivateReplyFailureReason.TransportFailure, result.Failure!.Reason);
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        var handler = new ScriptedHttpHandler(_ => throw new TaskCanceledException("cancelled"));
        var client = new GraphCommentPrivateReplyClient(
            new HttpClient(handler),
            Options.Create(new MetaMessagingOptions()),
            Options.Create(new MetaGraphOptions()));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SendPrivateReplyAsync(AccessToken, IgId, CommentId, "t", cts.Token));
    }

    [Fact]
    public async Task CredentialShapedProviderProseIsWithheldByTheSharedSanitizer()
    {
        // This token contains the word "secret", so the shared M13-003 sanitizer withholds
        // the entire provider message before it can reach logs or the effect ledger.
        var (client, _) = NewClient(new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                "{\"error\":{\"message\":\"invalid token " + AccessToken + " was rejected\",\"code\":190}}",
                Encoding.UTF8, "application/json"),
        });

        var result = await client.SendPrivateReplyAsync(AccessToken, IgId, CommentId, "t", default);

        Assert.False(result.Succeeded);
        Assert.Contains("(message withheld)", result.Failure!.Detail);
        Assert.DoesNotContain(AccessToken, result.Failure!.Detail);
    }

    [Fact]
    public async Task TokenEchoedInProviderProseIsRedactedByTheAdapter()
    {
        // A realistic token that does NOT trip the shared keyword sanitizer: provider prose
        // echoing it must still be redacted (full token + bounded prefix) by the adapter.
        const string echoedToken = "IGAA-USER-TOKEN-9f3k2z";
        var (client, _) = NewClient(new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                "{\"error\":{\"message\":\"invalid token " + echoedToken + " was rejected\",\"code\":190}}",
                Encoding.UTF8, "application/json"),
        });

        var result = await client.SendPrivateReplyAsync(echoedToken, IgId, CommentId, "t", default);

        Assert.False(result.Succeeded);
        Assert.DoesNotContain(echoedToken, result.Failure!.Detail);
        Assert.DoesNotContain(echoedToken[..8], result.Failure!.Detail);
        Assert.Contains("[REDACTED]", result.Failure!.Detail);
    }
}
