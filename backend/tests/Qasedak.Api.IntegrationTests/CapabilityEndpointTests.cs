using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Qasedak.Modules.Instagram.Application.Capabilities;
using Qasedak.Modules.Instagram.Application.OAuth;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

[Collection(ApiTestEnvironment.Name)]
public sealed class CapabilityEndpointTests(ApiPostgreSqlFixture fixture)
{
    private const string Callback = "https://app.test.local/capability-cb";

    private sealed record LoginResponse([property: JsonPropertyName("accessToken")] string AccessToken);

    private async Task<string> TokenAsync(string email, params Guid[] workspaces)
    {
        await fixture.Client.PostAsJsonAsync("/api/v1/identity/register",
            new { email, password = "Passw0rd!23", displayName = "Capability Tester" });
        var login = await fixture.Client.PostAsJsonAsync("/api/v1/identity/login",
            new { email, password = "Passw0rd!23" });
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;

        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/identity/me");
        me.Headers.Authorization = new("Bearer", token);
        using var meResponse = await fixture.Client.SendAsync(me);
        meResponse.EnsureSuccessStatusCode();
        var userId = Guid.Parse((await meResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("userId").GetString()!);
        foreach (var workspace in workspaces)
        {
            await fixture.EnsureWorkspaceMemberAsync(workspace, userId);
        }
        return token;
    }

    private HttpClient AuthedClient(string token)
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static async Task<string> IssueStateAsync(HttpClient client, Guid workspace)
    {
        var response = await client.GetAsync(
            $"/api/v1/workspaces/{workspace}/instagram/authorize-url?redirectUri={Uri.EscapeDataString(Callback)}");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.GetProperty("state").GetString()!;
    }

    private static async Task<Guid> ConnectAsync(HttpClient client, Guid workspace, string state)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace}/instagram/connections",
            new { authorizationCode = "capability-code", redirectUri = Callback, state });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(payload.GetProperty("accountId").GetString()!);
    }

    [Fact]
    public async Task AnonymousCallerIsRejected()
    {
        var workspace = Guid.CreateVersion7();
        var account = Guid.CreateVersion7();

        var response = await fixture.Client.GetAsync(
            $"/api/v1/workspaces/{workspace}/instagram/connections/{account}/capabilities");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExactAccountReturnsTokenFreeLocalProjectionAndForeignWorkspaceFailsClosed()
    {
        var home = Guid.CreateVersion7();
        var foreign = Guid.CreateVersion7();
        using var client = AuthedClient(await TokenAsync(
            $"capability-{Guid.NewGuid():N}@example.com", home, foreign));

        var previousCodeResult = fixture.OAuth.CodeResult;
        fixture.OAuth.CodeResult = () => CodeExchangeResult.Ok(new(
            "SHORT-CAPABILITY",
            "ig-cap-" + Guid.NewGuid().ToString("N"),
            [
                InstagramAuthorizationScopes.Basic,
                InstagramAuthorizationScopes.ManageMessages,
                InstagramAuthorizationScopes.ManageComments,
                InstagramCapabilityPolicy.ManageInsightsScope,
            ]));

        Guid accountId;
        try
        {
            accountId = await ConnectAsync(client, home, await IssueStateAsync(client, home));
        }
        finally
        {
            fixture.OAuth.CodeResult = previousCodeResult;
        }

        fixture.Media.Reset();
        fixture.Insights.Reset();
        fixture.Tokens.TokenGets.Clear();
        fixture.Relationships.Clear();
        fixture.Messaging.Sends.Clear();
        fixture.Messaging.TypedSends.Clear();
        fixture.PrivateReplies.Sends.Clear();
        fixture.PublicReplies.Sends.Clear();

        var tokenReadsBefore = fixture.Tokens.TokenGets.Count;
        var response = await client.GetAsync(
            $"/api/v1/workspaces/{home}/instagram/connections/{accountId}/capabilities");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        var payload = JsonDocument.Parse(raw).RootElement;
        Assert.Equal(accountId.ToString(), payload.GetProperty("accountId").GetString());
        var capabilities = payload.GetProperty("capabilities").EnumerateArray().ToArray();
        Assert.Equal(Enum.GetValues<InstagramCapability>().Length, capabilities.Length);

        foreach (var item in capabilities)
        {
            var name = item.GetProperty("capability").GetString();
            var state = item.GetProperty("state").GetString();
            Assert.Equal(name == nameof(InstagramCapability.FollowGate) ? "Unsupported" : "Available", state);
        }

        Assert.DoesNotContain("accessToken", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SHORT-CAPABILITY", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("fbtrace_id", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error_user_msg", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(tokenReadsBefore, fixture.Tokens.TokenGets.Count);
        Assert.Equal(0, fixture.Media.CallCount);
        Assert.Equal(0, fixture.Insights.CallCount);
        Assert.Empty(fixture.Relationships.Calls);
        Assert.Empty(fixture.Messaging.TypedSends);
        Assert.Empty(fixture.PrivateReplies.Sends);
        Assert.Empty(fixture.PublicReplies.Sends);

        var foreignResponse = await client.GetAsync(
            $"/api/v1/workspaces/{foreign}/instagram/connections/{accountId}/capabilities");
        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        Assert.Equal("account.notFound",
            (await foreignResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(tokenReadsBefore, fixture.Tokens.TokenGets.Count);
        Assert.Equal(0, fixture.Media.CallCount);
        Assert.Equal(0, fixture.Insights.CallCount);
        Assert.Empty(fixture.Relationships.Calls);
        Assert.Empty(fixture.Messaging.TypedSends);
        Assert.Empty(fixture.PrivateReplies.Sends);
        Assert.Empty(fixture.PublicReplies.Sends);

        var unknownResponse = await client.GetAsync(
            $"/api/v1/workspaces/{home}/instagram/connections/{Guid.CreateVersion7()}/capabilities");
        Assert.Equal(HttpStatusCode.NotFound, unknownResponse.StatusCode);
    }
}
