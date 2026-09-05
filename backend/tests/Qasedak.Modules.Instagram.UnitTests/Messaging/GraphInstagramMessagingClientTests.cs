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

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused");
    }
}
