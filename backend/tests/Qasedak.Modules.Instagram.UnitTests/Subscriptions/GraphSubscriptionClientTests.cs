using System.Net;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.Subscriptions;
using Qasedak.Modules.Instagram.Infrastructure.Graph;
using Qasedak.Modules.Instagram.Infrastructure.Subscriptions;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests;

/// <summary>
/// Deterministic contract tests for the webhook-subscription adapter: versioned
/// subscribe shape, success proof semantics, partial/missing confirmations and
/// failure mapping. No live Meta calls.
/// </summary>
public sealed class GraphSubscriptionClientTests
{
    private const string AccessToken = "SUBS-TOKEN-material";

    private static (GraphSubscriptionClient Client, List<HttpRequestMessage> Requests, List<string> Bodies) NewClient(HttpResponseMessage response)
    {
        var requests = new List<HttpRequestMessage>();
        var bodies = new List<string>();
        var handler = new ScriptedSubscriptionHandler(request =>
        {
            requests.Add(request);
            // Captured here: the client disposes the request (and its content)
            // before returning, so late reads would hit a disposed object.
            bodies.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return response;
        });
        return (new GraphSubscriptionClient(new HttpClient(handler), Options.Create(new MetaGraphOptions())), requests, bodies);
    }

    [Fact]
    public async Task SubscribePostsVersionedEdgeWithDesiredFields()
    {
        var (client, requests, bodies) = NewClient(new(HttpStatusCode.OK) { Content = new StringContent("""{"success":true}""") });

        var result = await client.SubscribeAsync(AccessToken, "17841400000000001", ["comments", "messages"], default);

        Assert.True(result.Success);
        Assert.Equal(["comments", "messages"], result.ConfirmedFields);
        var sent = Assert.Single(requests);
        // Verified official shape: POST /{IG_ID}/subscribed_apps (never /me).
        Assert.Equal("https://graph.instagram.com/v26.0/17841400000000001/subscribed_apps", sent.RequestUri!.GetLeftPart(UriPartial.Path));
        Assert.Equal("Bearer", sent.Headers.Authorization!.Scheme);
        var body = Assert.Single(bodies);
        // FormUrlEncodedContent percent-encodes the comma (uppercase hex).
        Assert.Contains("subscribed_fields=comments%2Cmessages", body);
        Assert.DoesNotContain(AccessToken, sent.RequestUri.Query);
        Assert.DoesNotContain(AccessToken, body);
    }

    [Fact]
    public async Task MissingSuccessFlagIsNotProof()
    {
        var (client, _, _) = NewClient(new(HttpStatusCode.OK) { Content = new StringContent("""{"success":false}""") });

        var result = await client.SubscribeAsync(AccessToken, "17841400000000001", ["comments"], default);

        Assert.False(result.Success);
        Assert.False(result.Transient);
    }

    [Fact]
    public async Task TransientAndPermissionFailuresAreDistinguished()
    {
        var (overloaded, _, _) = NewClient(new(HttpStatusCode.TooManyRequests) { Content = new StringContent("") });
        var (forbidden, _, _) = NewClient(new(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{"error":{"code":10,"type":"OAuthException","message":"denied"}}"""),
        });

        var transientResult = await overloaded.SubscribeAsync(AccessToken, "17841400000000001", ["comments"], default);
        var permissionResult = await forbidden.SubscribeAsync(AccessToken, "17841400000000001", ["comments"], default);

        Assert.False(transientResult.Success);
        Assert.True(transientResult.Transient);
        Assert.False(permissionResult.Success);
        Assert.Equal(SubscriptionFailures.PermissionDenied, permissionResult.FailureCode);
    }

    [Fact]
    public async Task EmptyFieldSetIsRejectedWithoutHttp()
    {
        var (client, requests, _) = NewClient(new(HttpStatusCode.OK) { Content = new StringContent("""{"success":true}""") });

        var result = await client.SubscribeAsync(AccessToken, "17841400000000001", [], default);

        Assert.False(result.Success);
        Assert.Empty(requests);
    }

    [Fact]
    public async Task MissingAccountIdentityIsRejectedWithoutHttp()
    {
        var (client, requests, _) = NewClient(new(HttpStatusCode.OK) { Content = new StringContent("""{"success":true}""") });

        var result = await client.SubscribeAsync(AccessToken, "  ", ["comments"], default);

        Assert.False(result.Success);
        Assert.Empty(requests);
    }

    private sealed class ScriptedSubscriptionHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
