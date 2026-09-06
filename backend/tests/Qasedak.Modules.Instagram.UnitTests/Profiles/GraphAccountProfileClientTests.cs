using System.Net;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Infrastructure.Graph;
using Qasedak.Modules.Instagram.Infrastructure.Profiles;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests;

/// <summary>
/// Deterministic contract tests for the professional-profile adapter: versioned
/// identity endpoint, identity-match enforcement, optional-field tolerance and
/// failure mapping. No live Meta calls.
/// </summary>
public sealed class GraphAccountProfileClientTests
{
    private const string AccessToken = "PROFILE-TOKEN-material";

    private static GraphAccountProfileClient NewClient(HttpResponseMessage response, Action<string>? onUrl = null)
    {
        var handler = new ScriptedProfileHandler(request =>
        {
            onUrl?.Invoke(request.RequestUri!.ToString());
            return response;
        });
        return new GraphAccountProfileClient(new HttpClient(handler), Options.Create(new MetaGraphOptions()));
    }

    [Fact]
    public async Task ValidProfileWithMatchingIdentitySucceeds()
    {
        string? url = null;
        var client = NewClient(
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"17841400000000001","username":"shop","name":"Shop","profile_picture_url":"https://pic.example/a.jpg"}"""),
            },
            onUrl: u => url = u);

        var outcome = await client.GetProfileAsync(AccessToken, "17841400000000001", default);

        var ok = Assert.IsType<AccountProfileOutcome.Ok>(outcome);
        Assert.Equal("17841400000000001", ok.Profile.ProviderAccountId);
        Assert.Equal("shop", ok.Profile.Username);
        Assert.Equal("Shop", ok.Profile.DisplayName);
        Assert.Equal("https://pic.example/a.jpg", ok.Profile.ProfilePictureUrl);
        // No verified account-type field exists on the official node yet.
        Assert.Null(ok.Profile.AccountType);
        Assert.StartsWith("https://graph.instagram.com/v26.0/me?fields=", url);
        Assert.Contains("fields=id%2Cusername%2Cname%2Cprofile_picture_url", url);
    }

    [Fact]
    public async Task IdentityMismatchFailsClosed()
    {
        var client = NewClient(
            new(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"99999999999999999","username":"shop"}"""),
            });

        var outcome = await client.GetProfileAsync(AccessToken, "17841400000000001", default);

        var mismatch = Assert.IsType<AccountProfileOutcome.IdentityMismatch>(outcome);
        Assert.Equal("17841400000000001", mismatch.ExpectedAccountId);
        Assert.Equal("99999999999999999", mismatch.ActualAccountId);
    }

    [Fact]
    public async Task MissingIdentityOrUsernameIsUnavailable()
    {
        var noId = NewClient(new(HttpStatusCode.OK) { Content = new StringContent("""{"username":"shop"}""") });
        var noName = NewClient(new(HttpStatusCode.OK) { Content = new StringContent("""{"id":"1"}""") });

        Assert.IsType<AccountProfileOutcome.Unavailable>(await noId.GetProfileAsync(AccessToken, "1", default));
        Assert.IsType<AccountProfileOutcome.Unavailable>(await noName.GetProfileAsync(AccessToken, "1", default));
    }

    [Fact]
    public async Task MalformedPayloadIsUnavailable()
    {
        var client = NewClient(new(HttpStatusCode.OK) { Content = new StringContent("""{"unexpected":true}""") });

        var outcome = await client.GetProfileAsync(AccessToken, "1", default);

        var unavailable = Assert.IsType<AccountProfileOutcome.Unavailable>(outcome);
        Assert.False(unavailable.Transient);
    }

    [Fact]
    public async Task TransientAndPermanentFailuresAreDistinguished()
    {
        var transient = NewClient(new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("") });
        var forbidden = NewClient(new(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{"error":{"code":10,"error_subcode":999,"type":"OAuthException","message":"denied"}}"""),
        });

        var transientOutcome = Assert.IsType<AccountProfileOutcome.Unavailable>(
            await transient.GetProfileAsync(AccessToken, "1", default));
        var permanentOutcome = Assert.IsType<AccountProfileOutcome.Unavailable>(
            await forbidden.GetProfileAsync(AccessToken, "1", default));

        Assert.True(transientOutcome.Transient);
        Assert.False(permanentOutcome.Transient);
        Assert.DoesNotContain(AccessToken, permanentOutcome.FailureCode);
    }

    private sealed class ScriptedProfileHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
