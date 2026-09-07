using System.Net;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.Messaging;
using Qasedak.Modules.Instagram.Infrastructure.Graph;
using Qasedak.Modules.Instagram.Infrastructure.Messaging;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests;

/// <summary>
/// Deterministic contract tests for the messaging send adapter: versioned request
/// shape, auth header, success validation, official Graph error taxonomy
/// (code 10 + subcode 2534022 = 24h window) and transport failures.
/// </summary>
public sealed class GraphInstagramMessagingClientTests
{
    private const string AccessToken = "IGSVCTOKEN-material";

    private static (GraphInstagramMessagingClient Client, ScriptedHttpHandler Handler) NewClient(
        HttpResponseMessage response,
        string apiVersion = "v26.0")
    {
        var handler = new ScriptedHttpHandler(_ => response);
        var client = new GraphInstagramMessagingClient(
            new HttpClient(handler),
            Options.Create(new MetaMessagingOptions()),
            Options.Create(new MetaGraphOptions { ApiVersion = apiVersion }));
        return (client, handler);
    }

    [Fact]
    public async Task SendsDocumentedPayloadWithBearerAuthorization()
    {
        var (client, handler) = NewClient(
            new(HttpStatusCode.OK) { Content = new StringContent("""{"recipient_id":"customer-9","message_id":"m_1"}""") });

        var result = await client.SendTextAsync(AccessToken, "customer-9", "hello", default);

        Assert.True(result.Succeeded);
        var last = handler.LastRequest;
        Assert.NotNull(last);
        Assert.Equal(new Uri("https://graph.instagram.com/v26.0/me/messages"), last!.RequestUri);
        Assert.Equal("Bearer", last.Headers.Authorization!.Scheme);
        Assert.Equal(AccessToken, last.Headers.Authorization.Parameter);
        Assert.Contains("\"recipient\":{\"id\":\"customer-9\"}", handler.LastBody);
        Assert.Contains("\"message\":{\"text\":\"hello\"}", handler.LastBody);
    }

    [Fact]
    public async Task ConfiguredVersionChangesEveryGraphPath()
    {
        var (client, handler) = NewClient(
            new(HttpStatusCode.OK) { Content = new StringContent("""{"recipient_id":"r","message_id":"m_1"}""") },
            apiVersion: "v99.9");

        var result = await client.SendTextAsync(AccessToken, "r", "t", default);

        Assert.True(result.Succeeded);
        Assert.Equal(new Uri("https://graph.instagram.com/v99.9/me/messages"), handler.LastRequest!.RequestUri);
    }

    [Fact]
    public async Task SuccessWithoutMessageIdIsMalformed()
    {
        var (client, _) = NewClient(
            new(HttpStatusCode.OK) { Content = new StringContent("""{"unexpected":true}""") });

        var result = await client.SendTextAsync(AccessToken, "r", "t", default);

        Assert.False(result.Succeeded);
        Assert.Equal(MessagingFailureReason.MalformedResponse, result.Failure!.Reason);
    }

    [Fact]
    public async Task OfficialWindowSignalMapsToWindowExpired()
    {
        var (client, _) = NewClient(
            new(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("""{"error":{"message":"This message is sent outside of allowed window.","type":"OAuthException","code":10,"error_subcode":2534022,"fbtrace_id":"w"}}"""),
            });

        var result = await client.SendTextAsync(AccessToken, "r", "t", default);

        Assert.False(result.Succeeded);
        Assert.Equal(MessagingFailureReason.MessagingWindowExpired, result.Failure!.Reason);
        Assert.Contains("trace w", result.Failure.Detail);
    }

    [Fact]
    public async Task OtherGraphErrorsMapToRejectedWithBoundedDetail()
    {
        var (client, _) = NewClient(
            new(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":{\"message\":\"bad recipient\",\"type\":\"OAuthException\",\"code\":100,\"fbtrace_id\":\"y\"}}"),
            });

        var result = await client.SendTextAsync(AccessToken, "r", "t", default);

        Assert.False(result.Succeeded);
        Assert.Equal(MessagingFailureReason.RejectedByMeta, result.Failure!.Reason);
        Assert.Contains("(code 100)", result.Failure.Detail);
        Assert.DoesNotContain(AccessToken, result.Failure.Detail);
    }

    [Fact]
    public async Task NonJsonMetaRejectionIsRejectedNotCrash()
    {
        var (client, _) = NewClient(
            new(HttpStatusCode.InternalServerError) { Content = new StringContent("<html>boom</html>") });

        var result = await client.SendTextAsync(AccessToken, "r", "t", default);

        Assert.Equal(MessagingFailureReason.RejectedByMeta, result.Failure!.Reason);
    }

    [Fact]
    public async Task TransportFailuresAreStructuredResults()
    {
        // A handler that throws simulates network failure.
        var client = new GraphInstagramMessagingClient(
            new HttpClient(new ThrowingHandler()),
            Microsoft.Extensions.Options.Options.Create(new MetaMessagingOptions()));

        var result = await client.SendTextAsync(AccessToken, "r", "t", default);

        Assert.Equal(MessagingFailureReason.TransportFailure, result.Failure!.Reason);
    }

    // ------------------------------------------------------------- button templates (M13-010)

    [Fact]
    public async Task PostbackButtonTemplateSerializesExactDocumentedShapeWithTypedSuccess()
    {
        var (client, handler) = NewClient(
            new(HttpStatusCode.OK) { Content = new StringContent("""{"recipient_id":"customer-9","message_id":"m_t1"}""") });

        var result = await client.SendDirectAsync(AccessToken, "customer-9",
            new InstagramMessageContent.ButtonTemplate(
                "What do you want to do next?",
                [new InstagramMessageButton.Postback("postback button", "postback_payload")]),
            default);

        Assert.True(result.Succeeded);
        Assert.Equal("customer-9", result.ProviderRecipientId);
        Assert.Equal("m_t1", result.ProviderMessageId);
        var request = handler.LastRequest!;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("https://graph.instagram.com/v26.0/me/messages"), request.RequestUri);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(AccessToken, request.Headers.Authorization!.Parameter);
        Assert.DoesNotContain(AccessToken, request.RequestUri!.ToString());
        Assert.Contains("\"recipient\":{\"id\":\"customer-9\"}", handler.LastBody);
        Assert.Contains("\"attachment\":{\"type\":\"template\",\"payload\":{\"template_type\":\"button\"", handler.LastBody);
        Assert.Contains("\"text\":\"What do you want to do next?\"", handler.LastBody);
        Assert.Contains("\"buttons\":[{\"type\":\"postback\",\"title\":\"postback button\",\"payload\":\"postback_payload\"}]", handler.LastBody);
    }

    [Fact]
    public async Task WebUrlButtonTemplateSerializesExactDocumentedShape()
    {
        var (client, handler) = NewClient(
            new(HttpStatusCode.OK) { Content = new StringContent("""{"recipient_id":"r","message_id":"m_t2"}""") });

        var result = await client.SendDirectAsync(AccessToken, "r",
            new InstagramMessageContent.ButtonTemplate(
                "Pick an option",
                [new InstagramMessageButton.WebUrl("Visit Instagram", "https://www.instagram.com")]),
            default);

        Assert.True(result.Succeeded);
        Assert.Contains("\"buttons\":[{\"type\":\"web_url\",\"title\":\"Visit Instagram\",\"url\":\"https://www.instagram.com\"}]", handler.LastBody);
    }

    [Fact]
    public async Task MixedTemplatePreservesButtonDeclarationOrder()
    {
        var (client, handler) = NewClient(
            new(HttpStatusCode.OK) { Content = new StringContent("""{"recipient_id":"r","message_id":"m_t3"}""") });

        var result = await client.SendDirectAsync(AccessToken, "r",
            new InstagramMessageContent.ButtonTemplate(
                "prompt",
                [
                    new InstagramMessageButton.WebUrl("first", "https://a.example"),
                    new InstagramMessageButton.Postback("second", "p2"),
                ]),
            default);

        Assert.True(result.Succeeded);
        var body = handler.LastBody!;
        var first = body.IndexOf("\"type\":\"web_url\"", StringComparison.Ordinal);
        var second = body.IndexOf("\"type\":\"postback\"", StringComparison.Ordinal);
        Assert.True(first >= 0 && second > first, "declaration order must be preserved");
    }

    [Fact]
    public async Task PlainTextSendStillUsesRecipientIdAndTypedSuccess()
    {
        var (client, handler) = NewClient(
            new(HttpStatusCode.OK) { Content = new StringContent("""{"recipient_id":"customer-9","message_id":"m_2"}""") });

        var result = await client.SendDirectAsync(AccessToken, "customer-9", new InstagramMessageContent.PlainText("hello"), default);

        Assert.True(result.Succeeded);
        Assert.Equal("customer-9", result.ProviderRecipientId);
        Assert.Equal("m_2", result.ProviderMessageId);
        Assert.Contains("\"recipient\":{\"id\":\"customer-9\"}", handler.LastBody);
        Assert.Contains("\"message\":{\"text\":\"hello\"}", handler.LastBody);
        Assert.DoesNotContain("comment_id", handler.LastBody);
    }

    // ------------------------------------------------------------- local limit enforcement

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task InvalidButtonCountIsRejectedLocallyWithZeroProviderCalls(int count)
    {
        var (client, handler) = NewClient(
            new(HttpStatusCode.OK) { Content = new StringContent("""{"recipient_id":"r","message_id":"m"}""") });

        var result = await client.SendDirectAsync(AccessToken, "r",
            new InstagramMessageContent.ButtonTemplate(
                "prompt",
                Enumerable.Range(0, count).Select(i => (InstagramMessageButton)new InstagramMessageButton.Postback($"b{i}", "p")).ToArray()),
            default);

        Assert.False(result.Succeeded);
        Assert.Equal(MessagingFailureReason.LocalValidation, result.Failure!.Reason);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task OversizedTemplateTextIsRejectedLocallyWithZeroProviderCalls()
    {
        var (client, handler) = NewClient(
            new(HttpStatusCode.OK) { Content = new StringContent("""{"recipient_id":"r","message_id":"m"}""") });

        var result = await client.SendDirectAsync(AccessToken, "r",
            new InstagramMessageContent.ButtonTemplate(
                new string('t', 641),
                [new InstagramMessageButton.Postback("b", "p")]),
            default);

        Assert.Equal(MessagingFailureReason.LocalValidation, result.Failure!.Reason);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task OversizedPostbackPayloadIsRejectedLocallyWithZeroProviderCalls()
    {
        var (client, handler) = NewClient(
            new(HttpStatusCode.OK) { Content = new StringContent("""{"recipient_id":"r","message_id":"m"}""") });

        var result = await client.SendDirectAsync(AccessToken, "r",
            new InstagramMessageContent.ButtonTemplate(
                "prompt",
                [new InstagramMessageButton.Postback("b", new string('p', 1001))]),
            default);

        Assert.Equal(MessagingFailureReason.LocalValidation, result.Failure!.Reason);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task InvalidUrlSchemeIsRejectedLocallyWithZeroProviderCalls()
    {
        var (client, handler) = NewClient(
            new(HttpStatusCode.OK) { Content = new StringContent("""{"recipient_id":"r","message_id":"m"}""") });

        var result = await client.SendDirectAsync(AccessToken, "r",
            new InstagramMessageContent.ButtonTemplate(
                "prompt",
                [new InstagramMessageButton.WebUrl("b", "javascript:alert(1)")]),
            default);

        Assert.Equal(MessagingFailureReason.LocalValidation, result.Failure!.Reason);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task OversizedPlainTextIsRejectedLocallyWithZeroProviderCalls()
    {
        var (client, handler) = NewClient(
            new(HttpStatusCode.OK) { Content = new StringContent("""{"recipient_id":"r","message_id":"m"}""") });

        var result = await client.SendDirectAsync(AccessToken, "r",
            new InstagramMessageContent.PlainText(new string('a', 1001)),
            default);

        Assert.Equal(MessagingFailureReason.LocalValidation, result.Failure!.Reason);
        Assert.Equal(0, handler.Calls);
    }

    // ------------------------------------------------------------- typed success / failures

    [Fact]
    public async Task SuccessWithoutRecipientIdIsMalformed()
    {
        var (client, _) = NewClient(
            new(HttpStatusCode.OK) { Content = new StringContent("""{"message_id":"m_1"}""") });

        var result = await client.SendTextAsync(AccessToken, "r", "t", default);

        Assert.False(result.Succeeded);
        Assert.Equal(MessagingFailureReason.MalformedResponse, result.Failure!.Reason);
    }

    [Fact]
    public async Task TokenEchoInProviderErrorIsRedacted()
    {
        const string sentinel = "TOKEN-SHOULD-NEVER-APPEAR";
        var (client, _) = NewClient(
            new(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"error":{"message":"bad token TOKEN-SHOULD-NEVER-APPEAR used","type":"OAuthException","code":190}}"""),
            });

        var result = await client.SendDirectAsync(sentinel, "r", new InstagramMessageContent.PlainText("t"), default);

        Assert.False(result.Succeeded);
        Assert.DoesNotContain(sentinel, result.Failure!.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallerCancellationPropagatesNotAsTransportFailure()
    {
        var handler = new CancellationAwareHandler();
        var client = new GraphInstagramMessagingClient(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new MetaMessagingOptions()),
            Microsoft.Extensions.Options.Options.Create(new MetaGraphOptions()));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.SendDirectAsync(AccessToken, "r", new InstagramMessageContent.PlainText("t"), cts.Token));
    }

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
            throw new InvalidOperationException("unreachable");
        }
    }
}
