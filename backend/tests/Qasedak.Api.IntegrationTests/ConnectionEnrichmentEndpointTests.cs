using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Qasedak.Modules.Instagram.Application.Subscriptions;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

/// <summary>
/// M13-005 connection enrichment surface: server-issued single-use OAuth state,
/// state-bound connect, token-free enriched listing, explicit subscription repair
/// and workspace/account ownership. Meta edges are scripted in the fixture; no
/// live Meta call.
/// </summary>
[Collection(ApiTestEnvironment.Name)]
public sealed class ConnectionEnrichmentEndpointTests(ApiPostgreSqlFixture fixture)
{
    private const string Callback = "https://app.test.local/cb";

    private static Guid FreshWorkspace() => Guid.CreateVersion7();

    private sealed record LoginResponse([property: JsonPropertyName("accessToken")] string AccessToken);

    private async Task<string> TokenAsync(string email, params Guid[] workspaces)
    {
        await fixture.Client.PostAsJsonAsync("/api/v1/identity/register", new { email, password = "Passw0rd!23", displayName = "Connection Tester" });
        var login = await fixture.Client.PostAsJsonAsync("/api/v1/identity/login", new { email, password = "Passw0rd!23" });
        var payload = await login.Content.ReadFromJsonAsync<LoginResponse>();
        var token = payload!.AccessToken;

        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/identity/me");
        me.Headers.Authorization = new("Bearer", token);
        using var meResponse = await fixture.Client.SendAsync(me);
        meResponse.EnsureSuccessStatusCode();
        var userId = Guid.Parse((await meResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetString()!);
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

    private static async Task<(string Url, string State)> AuthorizeAsync(HttpClient client, Guid workspace)
    {
        var response = await client.GetAsync(
            $"/api/v1/workspaces/{workspace}/instagram/authorize-url?redirectUri={Uri.EscapeDataString(Callback)}");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (payload.GetProperty("url").GetString()!, payload.GetProperty("state").GetString()!);
    }

    private static async Task<HttpResponseMessage> ConnectAsync(HttpClient client, Guid workspace, string code, string? state) =>
        await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace}/instagram/connections",
            new { authorizationCode = code, redirectUri = Callback, state });

    [Fact]
    public async Task FullFlowIssuesStateConnectsListsEnrichedAndRejectsReplay()
    {
        var workspace = FreshWorkspace();
        using var client = AuthedClient(await TokenAsync("conn-full@example.com", workspace));

        var (url, state) = await AuthorizeAsync(client, workspace);
        Assert.Contains("state=", url);
        Assert.False(string.IsNullOrWhiteSpace(state));

        var connected = await ConnectAsync(client, workspace, "code-full", state);
        Assert.Equal(HttpStatusCode.Created, connected.StatusCode);
        var accountId = Guid.Parse((await connected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accountId").GetString()!);

        var listed = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/workspaces/{workspace}/instagram/connections");
        var item = Assert.Single(listed.GetProperty("items").EnumerateArray());
        Assert.Equal(accountId.ToString(), item.GetProperty("accountId").GetString());
        Assert.Equal("Connected", item.GetProperty("health").GetString());
        Assert.StartsWith("shop-ig-e2e-", item.GetProperty("username").GetString());
        Assert.Equal("E2E Shop", item.GetProperty("displayName").GetString());
        Assert.Equal("Business", item.GetProperty("accountType").GetString());
        Assert.Equal("Healthy", item.GetProperty("subscriptionHealth").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("profilePictureUrl").ValueKind);
        var raw = item.GetRawText();
        Assert.DoesNotContain("LONG-E2E", raw);
        Assert.DoesNotContain("SHORT-E2E", raw);
        Assert.DoesNotContain("accessToken", raw);

        // The same state is single-use: replay fails closed with a stable code.
        var replay = await ConnectAsync(client, workspace, "code-replay", state);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal("oauth.replayedState",
            (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task MissingAndTamperedStatesAreRejected()
    {
        var workspace = FreshWorkspace();
        using var client = AuthedClient(await TokenAsync("conn-state@example.com", workspace));

        var missing = await ConnectAsync(client, workspace, "code-missing", null);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("oauth.invalidState",
            (await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var (_, state) = await AuthorizeAsync(client, workspace);
        var tampered = await ConnectAsync(client, workspace, "code-tampered", state + "tampered");
        Assert.Equal(HttpStatusCode.BadRequest, tampered.StatusCode);
        Assert.Equal("oauth.invalidState",
            (await tampered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task FailedSubscribeConnectsDegradedAndRepairRecovers()
    {
        var workspace = FreshWorkspace();
        using var client = AuthedClient(await TokenAsync("conn-repair@example.com", workspace));

        fixture.Subscriptions.Result = _ => SubscriptionResult.Failed(SubscriptionFailures.Unavailable, transient: true);
        Guid accountId;
        try
        {
            var (_, state) = await AuthorizeAsync(client, workspace);
            var connected = await ConnectAsync(client, workspace, "code-degraded", state);
            Assert.Equal(HttpStatusCode.Created, connected.StatusCode);
            accountId = Guid.Parse((await connected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accountId").GetString()!);
        }
        finally
        {
            fixture.Subscriptions.Result = fields => SubscriptionResult.Subscribed(fields);
        }

        var degraded = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/workspaces/{workspace}/instagram/connections");
        Assert.Equal("NeedsRepair",
            Assert.Single(degraded.GetProperty("items").EnumerateArray()).GetProperty("subscriptionHealth").GetString());

        var repaired = await client.PostAsync(
            $"/api/v1/workspaces/{workspace}/instagram/connections/{accountId}/repair-subscription", null);
        Assert.Equal(HttpStatusCode.OK, repaired.StatusCode);
        Assert.Equal("Healthy",
            (await repaired.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("subscriptionHealth").GetString());

        var healed = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/workspaces/{workspace}/instagram/connections");
        Assert.Equal("Healthy",
            Assert.Single(healed.GetProperty("items").EnumerateArray()).GetProperty("subscriptionHealth").GetString());
    }

    [Fact]
    public async Task WorkspaceAndAccountOwnershipIsEnforced()
    {
        var home = FreshWorkspace();
        var foreign = FreshWorkspace();
        using var client = AuthedClient(await TokenAsync("conn-owner@example.com", home, foreign));

        var (_, state) = await AuthorizeAsync(client, home);
        var connected = await ConnectAsync(client, home, "code-owned", state);
        var accountId = Guid.Parse((await connected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accountId").GetString()!);

        // Sibling workspace sees nothing and cannot repair or disconnect the account.
        var listed = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/workspaces/{foreign}/instagram/connections");
        Assert.Equal(0, listed.GetProperty("items").GetArrayLength());

        var foreignRepair = await client.PostAsync(
            $"/api/v1/workspaces/{foreign}/instagram/connections/{accountId}/repair-subscription", null);
        Assert.Equal(HttpStatusCode.NotFound, foreignRepair.StatusCode);

        var foreignDelete = await client.DeleteAsync(
            $"/api/v1/workspaces/{foreign}/instagram/connections/{accountId}");
        Assert.Equal(HttpStatusCode.NotFound, foreignDelete.StatusCode);

        // Unknown accounts and anonymous callers fail closed.
        var unknownRepair = await client.PostAsync(
            $"/api/v1/workspaces/{home}/instagram/connections/{Guid.CreateVersion7()}/repair-subscription", null);
        Assert.Equal(HttpStatusCode.NotFound, unknownRepair.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Client.GetAsync(
            $"/api/v1/workspaces/{home}/instagram/connections")).StatusCode);
    }

    [Fact]
    public async Task DisconnectHidesConnectionAndSecondDeleteIsNotFound()
    {
        var workspace = FreshWorkspace();
        using var client = AuthedClient(await TokenAsync("conn-disc@example.com", workspace));

        var (_, state) = await AuthorizeAsync(client, workspace);
        var connected = await ConnectAsync(client, workspace, "code-disc", state);
        var accountId = Guid.Parse((await connected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accountId").GetString()!);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(
            $"/api/v1/workspaces/{workspace}/instagram/connections/{accountId}")).StatusCode);

        var listed = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/workspaces/{workspace}/instagram/connections");
        Assert.Equal(0, listed.GetProperty("items").GetArrayLength());

        var withDisconnected = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/workspaces/{workspace}/instagram/connections?includeDisconnected=true");
        Assert.NotNull(Assert.Single(withDisconnected.GetProperty("items").EnumerateArray())
            .GetProperty("disconnectedAtUtc").GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(
            $"/api/v1/workspaces/{workspace}/instagram/connections/{accountId}")).StatusCode);
    }
}
