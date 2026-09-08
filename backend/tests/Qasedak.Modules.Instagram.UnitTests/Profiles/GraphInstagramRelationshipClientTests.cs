using System.Net;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.RevealFlow;
using Qasedak.Modules.Instagram.Infrastructure.Graph;
using Qasedak.Modules.Instagram.Infrastructure.Profiles;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests;

public sealed class GraphInstagramRelationshipClientTests
{
    private const string Token = "RELATIONSHIP-TOKEN-material";

    [Fact]
    public async Task UsesConfiguredVersionedProfilePathAndBearerHeaderOnly()
    {
        HttpRequestMessage? sent = null;
        var client = NewClient((request, _) =>
        {
            sent = request;
            return Task.FromResult(Json(HttpStatusCode.OK, "{\"is_user_follow_business\":true}"));
        }, new MetaGraphOptions { GraphHost = "https://graph.instagram.com", ApiVersion = "v27.0" });

        var result = await client.GetFollowStateAsync(Token, "17890000000000001");
        Assert.Equal(FollowState.Follows, result.State);
        Assert.Equal("https://graph.instagram.com/v27.0/17890000000000001?fields=is_user_follow_business", sent!.RequestUri!.ToString());
        Assert.Equal("Bearer", sent.Headers.Authorization?.Scheme);
        Assert.Equal(Token, sent.Headers.Authorization?.Parameter);
        Assert.DoesNotContain(Token, sent.RequestUri.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, FollowState.Follows)]
    [InlineData(false, FollowState.DoesNotFollow)]
    public async Task ProviderBooleanIsAuthoritative(bool follows, FollowState expected)
    {
        var body = follows ? "{\"is_user_follow_business\":true}" : "{\"is_user_follow_business\":false}";
        var client = NewClient((_, _) => Task.FromResult(Json(HttpStatusCode.OK, body)));

        var result = await client.GetFollowStateAsync(Token, "user-1");

        Assert.Equal(expected, result.State);
        Assert.Null(result.UnavailableReason);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"is_user_follow_business\":null}")]
    [InlineData("{\"is_user_follow_business\":\"yes\"}")]
    public async Task MissingOrMalformedFieldFailsClosed(string body)
    {
        var client = NewClient((_, _) => Task.FromResult(Json(HttpStatusCode.OK, body)));
        var result = await client.GetFollowStateAsync(Token, "user-1");
        Assert.Equal(FollowState.UnknownUnavailable, result.State);
        Assert.Equal(FollowStateUnavailableReason.Malformed, result.UnavailableReason);
    }

    [Theory]
    [InlineData(403, 10, FollowStateUnavailableReason.PermissionUnavailable)]
    [InlineData(429, 4, FollowStateUnavailableReason.Transient)]
    [InlineData(503, 2, FollowStateUnavailableReason.Transient)]
    [InlineData(401, 190, FollowStateUnavailableReason.ConsentUnavailable)]
    public async Task ProviderFailuresMapToBoundedUnavailableReasons(int status, int code, FollowStateUnavailableReason expected)
    {
        var body = $"{{\"error\":{{\"code\":{code},\"message\":\"provider rejected {Token}\"}}}}";
        var client = NewClient((_, _) => Task.FromResult(Json((HttpStatusCode)status, body)));

        var result = await client.GetFollowStateAsync(Token, "user-1");

        Assert.Equal(FollowState.UnknownUnavailable, result.State);
        Assert.Equal(expected, result.UnavailableReason);
    }

    [Fact]
    public async Task CallerCancellationPropagatesAndCannotLaunchLaterCalls()
    {
        var calls = 0;
        var client = NewClient(async (_, ct) =>
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Json(HttpStatusCode.OK, "{\"is_user_follow_business\":true}");
        });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetFollowStateAsync(Token, "user-1", cts.Token));
        Assert.Equal(1, calls);
    }

    private static GraphInstagramRelationshipClient NewClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        MetaGraphOptions? options = null) =>
        new(new HttpClient(new DelegateHandler(send)), Options.Create(options ?? new MetaGraphOptions()));

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
