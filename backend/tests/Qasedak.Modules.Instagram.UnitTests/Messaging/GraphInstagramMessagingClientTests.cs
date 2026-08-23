using System.Net;
using Qasedak.Modules.Instagram.Application.Messaging;
using Qasedak.Modules.Instagram.Infrastructure.Messaging;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests;

/// <summary>
/// Deterministic contract tests for the messaging send adapter: request shape, auth header,
/// success validation, Graph error taxonomy (490 = 24h window) and transport failures.
/// </summary>
public sealed class GraphInstagramMessagingClientTests
{
    private const string AccessToken = "IGSVCTOKEN-material";

    private static (GraphInstagramMessagingClient Client, ScriptedHttpHandler Handler) NewClient(
        HttpResponseMessage response)
    {
        var handler = new ScriptedHttpHandler(_ => response);
        var client = new GraphInstagramMessagingClient(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new MetaMessagingOptions()));
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
        Assert.Equal(new Uri("https://graph.instagram.com/me/messages"), last!.RequestUri);
        Assert.Equal("Bearer", last.Headers.Authorization!.Scheme);
        Assert.Equal(AccessToken, last.Headers.Authorization.Parameter);
        Assert.Contains("\"recipient\":{\"id\":\"customer-9\"}", handler.LastBody);
        Assert.Contains("\"message\":{\"text\":\"hello\"}", handler.LastBody);
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
    public async Task GraphErrorCode490MapsToWindowExpired()
    {
        var (client, _) = NewClient(
            new(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("""{"error":{"message":"outside allowed window","type":"OAuthException","code":490,"fbtrace_id":"x"}}"""),
            });

        var result = await client.SendTextAsync(AccessToken, "r", "t", default);

        Assert.False(result.Succeeded);
        Assert.Equal(MessagingFailureReason.MessagingWindowExpired, result.Failure!.Reason);
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
