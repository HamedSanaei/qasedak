using System.Net;
using System.Text;
using Qasedak.Modules.Instagram.Application.OAuth;
using Qasedak.Modules.Instagram.Infrastructure.OAuth;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests;

public sealed class InstagramAuthorizationUrlBuilderTests
{
    private static InstagramAuthorizationUrlBuilder NewBuilder(string? scopes = null) =>
        new(Microsoft.Extensions.Options.Options.Create(new MetaOAuthOptions
        {
            ClientId = "990602627938098",
            Scopes = scopes ?? "instagram_business_basic,instagram_business_content_publish",
        }));

    [Fact]
    public void BuildsDocumentedUrlWithAllRequiredParameters()
    {
        var url = NewBuilder().Build(new(
            "https://api.qasedak.example/oauth/instagram/callback",
            "csrf-state-123"));

        Assert.StartsWith("https://www.instagram.com/oauth/authorize?", url.Value);
        Assert.Contains("client_id=990602627938098", url.Value);
        Assert.Contains("response_type=code", url.Value);
        Assert.Contains(
            "redirect_uri=https%3A%2F%2Fapi.qasedak.example%2Foauth%2Finstagram%2Fcallback",
            url.Value);
        // Comma-separated scope list is a valid form per the documented parameter table.
        Assert.Contains("scope=instagram_business_basic%2Cinstagram_business_content_publish", url.Value);
    }

    [Fact]
    public void StateParameterIsIncludedAndUrlEncoded()
    {
        var url = NewBuilder().Build(new("https://cb.example/", "a b&c=d"));

        Assert.Contains("state=a%20b%26c%3Dd", url.Value);
    }

    [Fact]
    public void OmittedStateOmitsTheParameter()
    {
        var url = NewBuilder().Build(new("https://cb.example/", string.Empty));

        Assert.DoesNotContain("state", url.Value);
    }

    [Fact]
    public void DefaultScopeSetMatchesTheVerifiedContract()
    {
        Assert.Equal(
            new[]
            {
                InstagramAuthorizationScopes.Basic,
                InstagramAuthorizationScopes.ContentPublish,
                InstagramAuthorizationScopes.ManageMessages,
                InstagramAuthorizationScopes.ManageComments,
            },
            InstagramAuthorizationScopes.Default);
    }
}

/// <summary>Scripted HttpClient handler for deterministic OAuth contract tests.</summary>
internal sealed class ScriptedHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    public string? LastBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        if (request.Content is not null)
        {
            LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        return respond(request);
    }
}

public sealed class GraphInstagramOAuthClientTests
{
    private const string AppSecret = "ig-app-secret-0123456789abcdef";

    private static GraphInstagramOAuthClient NewClient(
        ScriptedHttpHandler handler,
        Action<MetaOAuthOptions>? configure = null)
    {
        var options = new MetaOAuthOptions
        {
            ClientId = "990602627938098",
            ClientSecret = AppSecret,
        };
        configure?.Invoke(options);
        return new GraphInstagramOAuthClient(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(options));
    }

    private static ScriptedHttpHandler HandlerFor(HttpStatusCode statusCode, string json) =>
        new(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    [Fact]
    public async Task ExchangeCodePostsFormFieldsToTheDocumentedEndpoint()
    {
        var handler = HandlerFor(HttpStatusCode.OK, """
            {"data":[{"access_token":"SHORT-TOKEN","user_id":"1020",
              "permissions":"instagram_business_basic,instagram_business_manage_messages"}]}
            """);
        var client = NewClient(handler);

        var result = await client.ExchangeCodeAsync(new("AQBx-hBsH3-code", "https://cb.example/"));

        Assert.True(result.Success is not null, result.Failure?.ToString());
        Assert.Equal("SHORT-TOKEN", result.Success!.AccessToken);
        Assert.Equal("1020", result.Success.InstagramUserId);
        Assert.Equal(["instagram_business_basic", "instagram_business_manage_messages"], result.Success.GrantedPermissions);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal("https://api.instagram.com/oauth/access_token", handler.LastRequest!.RequestUri!.ToString());
        Assert.Contains("grant_type=authorization_code", handler.LastBody);
        Assert.Contains("client_id=990602627938098", handler.LastBody);
        Assert.Contains($"client_secret={Uri.EscapeDataString(AppSecret)}", handler.LastBody);
        Assert.Contains("code=AQBx-hBsH3-code", handler.LastBody);
        Assert.Contains($"redirect_uri={Uri.EscapeDataString("https://cb.example/")}", handler.LastBody);
    }

    [Fact]
    public async Task RejectedCodeExchangeMapsMetaErrorWithoutThrowing()
    {
        var handler = HandlerFor(HttpStatusCode.BadRequest, """
            {"error_type":"OAuthException","code":400,
             "error_message":"Matching code was not found or was already used"}
            """);
        var client = NewClient(handler);

        var result = await client.ExchangeCodeAsync(new("used-code", "https://cb.example/"));

        Assert.True(result.Failure is not null);
        Assert.Equal(MetaOAuthFailureReason.RejectedByMeta, result.Failure!.Reason);
        Assert.Contains("400 OAuthException", result.Failure.Detail);
        // The documented message carries no credential material and may be surfaced.
        Assert.Contains("already used", result.Failure.Detail);
    }

    [Fact]
    public async Task ShortLivedTokenIsExchangedViaIgExchangeTokenGrant()
    {
        var handler = HandlerFor(HttpStatusCode.OK, """
            {"access_token":"LONG-TOKEN","token_type":"bearer","expires_in":5183944}
            """);
        var client = NewClient(handler);

        var result = await client.ExchangeShortLivedForLongLivedAsync("SHORT-TOKEN");

        Assert.True(result.Success is not null, result.Failure?.ToString());
        Assert.Equal("LONG-TOKEN", result.Success!.AccessToken);
        Assert.Equal(5183944, result.Success.ExpiresInSeconds);

        var uri = handler.LastRequest!.RequestUri!;
        Assert.Equal("https://graph.instagram.com/access_token", $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}");
        Assert.Contains("grant_type=ig_exchange_token", uri.Query);
        Assert.Contains($"client_secret={Uri.EscapeDataString(AppSecret)}", uri.Query);
        Assert.Contains("access_token=SHORT-TOKEN", uri.Query);
    }

    [Fact]
    public async Task RefreshUsesIgRefreshTokenGrant()
    {
        var handler = HandlerFor(HttpStatusCode.OK, """
            {"access_token":"REFRESHED-TOKEN","token_type":"bearer","expires_in":5184000}
            """);
        var client = NewClient(handler);

        var result = await client.RefreshLongLivedAsync("OLD-LONG-TOKEN");

        Assert.True(result.Success is not null, result.Failure?.ToString());
        Assert.Equal("REFRESHED-TOKEN", result.Success!.AccessToken);

        var uri = handler.LastRequest!.RequestUri!;
        Assert.Equal("https://graph.instagram.com/refresh_access_token", $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}");
        Assert.Contains("grant_type=ig_refresh_token", uri.Query);
        Assert.Contains("access_token=OLD-LONG-TOKEN", uri.Query);
    }

    [Fact]
    public async Task TransportFailureIsReportedNotThrown()
    {
        var handler = new ScriptedHttpHandler(_ => throw new HttpRequestException("socket down"));
        var client = NewClient(handler);

        var exchange = await client.ExchangeCodeAsync(new("c", "https://cb.example/"));
        var refresh = await client.RefreshLongLivedAsync("t");

        Assert.Equal(MetaOAuthFailureReason.TransportFailure, exchange.Failure!.Reason);
        Assert.Equal(MetaOAuthFailureReason.TransportFailure, refresh.Failure!.Reason);
        Assert.DoesNotContain(AppSecret, refresh.Failure.Detail);
    }

    [Fact]
    public async Task NonJsonRejectionYieldsStructuredFailure()
    {
        var handler = new ScriptedHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("<html>denied</html>", Encoding.UTF8, "text/html"),
        });
        var client = NewClient(handler);

        var result = await client.RefreshLongLivedAsync("SOME-TOKEN");

        Assert.True(result.Failure is not null);
        Assert.Equal(MetaOAuthFailureReason.RejectedByMeta, result.Failure!.Reason);
        Assert.DoesNotContain("SOME-TOKEN", result.Failure.Detail);
    }

    [Fact]
    public async Task FailureDetailsNeverContainSecretOrTokenValues()
    {
        var handler = HandlerFor(HttpStatusCode.BadRequest, """
            {"error_type":"OAuthException","code":400,"error_message":"client_secret=ig-app-secret-0123456789abcdef leaked"}
            """);
        var client = NewClient(handler);

        var result = await client.ExchangeCodeAsync(new("c", "https://cb.example/"));

        Assert.True(result.Failure is not null);
        Assert.DoesNotContain(AppSecret, result.Failure!.Detail);
    }
}
