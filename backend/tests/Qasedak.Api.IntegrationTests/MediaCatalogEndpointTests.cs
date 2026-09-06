using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.Media;
using Qasedak.Modules.Instagram.Infrastructure.Media;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

/// <summary>
/// M13-006 media catalog surface: exact-account authorization, workspace isolation
/// (zero token reads + zero provider calls on foreign accounts), page bounds,
/// cursor validation/roundtrip and redaction. Meta edges are scripted in the
/// fixture; no live Meta call.
/// </summary>
[Collection(ApiTestEnvironment.Name)]
public sealed class MediaCatalogEndpointTests(ApiPostgreSqlFixture fixture)
{
    private const string Callback = "https://app.test.local/cb";

    private static Guid FreshWorkspace() => Guid.CreateVersion7();

    private sealed record LoginResponse([property: JsonPropertyName("accessToken")] string AccessToken);

    private async Task<(HttpClient Client, Guid Workspace, Guid AccountId, string ProviderIdentity)> ConnectedAccountAsync(
        string email, Guid workspace, string providerSuffix)
    {
        // Scripted client state is per-test; never leak across tests.
        fixture.Media.Reset();
        var client = await AuthedClientAsync(email, workspace);
        var state = await AuthorizeAsync(client, workspace);
        var connect = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace}/instagram/connections",
            new { authorizationCode = $"code-{providerSuffix}", redirectUri = Callback, state });
        Assert.Equal(HttpStatusCode.Created, connect.StatusCode);
        var accountId = Guid.Parse((await connect.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accountId").GetString()!);

        var listed = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/workspaces/{workspace}/instagram/connections");
        // Workspaces may hold several accounts (cross-account tests); locate by id.
        var item = listed.GetProperty("items").EnumerateArray()
            .Single(i => Guid.Parse(i.GetProperty("accountId").GetString()!) == accountId);
        var providerIdentity = item.GetProperty("providerIdentity").GetString()!;
        return (client, workspace, accountId, providerIdentity);
    }

    private async Task<HttpClient> AuthedClientAsync(string email, params Guid[] workspaces)
    {
        await fixture.Client.PostAsJsonAsync("/api/v1/identity/register", new { email, password = "Passw0rd!23", displayName = "Media Tester" });
        var login = await fixture.Client.PostAsJsonAsync("/api/v1/identity/login", new { email, password = "Passw0rd!23" });
        var payload = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        var token = payload.AccessToken;

        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/identity/me");
        me.Headers.Authorization = new("Bearer", token);
        using var meResponse = await fixture.Client.SendAsync(me);
        meResponse.EnsureSuccessStatusCode();
        var userId = Guid.Parse((await meResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetString()!);
        foreach (var workspace in workspaces)
        {
            await fixture.EnsureWorkspaceMemberAsync(workspace, userId);
        }

        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static async Task<string> AuthorizeAsync(HttpClient client, Guid workspace)
    {
        var response = await client.GetAsync(
            $"/api/v1/workspaces/{workspace}/instagram/authorize-url?redirectUri={Uri.EscapeDataString(Callback)}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString()!;
    }

    private static string MediaUrl(Guid workspace, Guid accountId, string query = "") =>
        $"/api/v1/workspaces/{workspace}/instagram/connections/{accountId}/media{query}";

    [Fact]
    public async Task UnauthenticatedMediaRequestIs401()
    {
        var response = await fixture.Client.GetAsync(MediaUrl(FreshWorkspace(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WorkspaceMemberListsOwnAccountMediaWithSafeProjection()
    {
        var (client, workspace, accountId, providerIdentity) = await ConnectedAccountAsync("media-own@example.com", FreshWorkspace(), "own");
        fixture.Media.PageFor = (after, _) => new MediaCatalogPage(
            [ScriptedMediaCatalogClient.Item("m1", "Image"), ScriptedMediaCatalogClient.Item("m2", "Reel")],
            NextCursor: null,
            HasMore: false);

        var response = await client.GetAsync(MediaUrl(workspace, accountId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, payload.GetProperty("items").GetArrayLength());
        Assert.Equal("m1", payload.GetProperty("items")[0].GetProperty("mediaId").GetString());
        Assert.Equal("Image", payload.GetProperty("items")[0].GetProperty("kind").GetString());
        Assert.Equal("Reel", payload.GetProperty("items")[1].GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.False, payload.GetProperty("hasMore").ValueKind);
        // The exact account's provider identity was used, never a guessed one.
        Assert.Equal(providerIdentity, fixture.Media.Calls.Single().ProviderAccountId);

        var raw = payload.GetRawText();
        Assert.DoesNotContain("LONG-E2E", raw);
        Assert.DoesNotContain("SHORT-E2E", raw);
        Assert.DoesNotContain("accessToken", raw);
        Assert.DoesNotContain("access_token", raw);
        Assert.DoesNotContain("paging", raw);
        Assert.DoesNotContain("cursors", raw);
    }

    [Fact]
    public async Task CursorPaginationRoundTripsThroughOpaqueEnvelope()
    {
        var (client, workspace, accountId, _) = await ConnectedAccountAsync("media-cursor@example.com", FreshWorkspace(), "cur");
        var codec = new MediaCatalogCursorCodec();
        fixture.Media.PageFor = (after, _) => after is null
            ? new MediaCatalogPage(
                [ScriptedMediaCatalogClient.Item("p1-1"), ScriptedMediaCatalogClient.Item("p1-2")],
                codec.Encode(accountId, "provider-after-1"),
                HasMore: true)
            : new MediaCatalogPage(
                [ScriptedMediaCatalogClient.Item("p2-1")],
                NextCursor: null,
                HasMore: false);

        var page1 = await client.GetFromJsonAsync<JsonElement>(MediaUrl(workspace, accountId));
        var nextCursor = page1.GetProperty("nextCursor").GetString();
        Assert.NotNull(nextCursor);
        Assert.Equal(JsonValueKind.True, page1.GetProperty("hasMore").ValueKind);
        Assert.Equal("p1-1", page1.GetProperty("items")[0].GetProperty("mediaId").GetString());
        Assert.DoesNotContain("provider-after-1", nextCursor); // opaque: provider token not readable
        Assert.DoesNotContain("LONG-E2E", nextCursor);

        var page2 = await client.GetFromJsonAsync<JsonElement>(
            MediaUrl(workspace, accountId, $"?cursor={Uri.EscapeDataString(nextCursor)}"));
        Assert.Equal("p2-1", page2.GetProperty("items")[0].GetProperty("mediaId").GetString());
        Assert.Equal(JsonValueKind.Null, page2.GetProperty("nextCursor").ValueKind);
        Assert.Equal(JsonValueKind.False, page2.GetProperty("hasMore").ValueKind);
        // The provider call used the decoded component — never a URL.
        Assert.Equal("provider-after-1", fixture.Media.Calls[1].AfterCursor);
    }

    [Fact]
    public async Task CrossAccountCursorIsRejectedWithoutProviderCall()
    {
        var workspace = FreshWorkspace();
        var (client, _, accountA, _) = await ConnectedAccountAsync("media-crossa@example.com", workspace, "a");
        var (_, _, accountB, _) = await ConnectedAccountAsync("media-crossb@example.com", workspace, "b");
        fixture.Media.PageFor = (after, accountId) => new MediaCatalogPage(
            [ScriptedMediaCatalogClient.Item("xA")],
            new MediaCatalogCursorCodec().Encode(accountId, "after-A"),
            HasMore: true);

        var page1 = await client.GetFromJsonAsync<JsonElement>(MediaUrl(workspace, accountA));
        var cursorForA = page1.GetProperty("nextCursor").GetString()!;
        var before = fixture.Media.CallCount;

        // Account B receives account A's cursor → rejected as invalid, zero provider calls.
        var rejected = await client.GetAsync(MediaUrl(workspace, accountB, $"?cursor={Uri.EscapeDataString(cursorForA)}"));

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(MediaCatalogFailures.InvalidCursor,
            (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(before, fixture.Media.CallCount);
    }

    [Fact]
    public async Task ForeignWorkspaceAccountIs404WithZeroTokenReadsAndZeroProviderCalls()
    {
        var workspaceA = FreshWorkspace();
        var workspaceB = FreshWorkspace();
        var (_, _, accountB, _) = await ConnectedAccountAsync("media-b@example.com", workspaceB, "b");
        var clientA = await AuthedClientAsync("media-a@example.com", workspaceA);
        var tokenGetsBefore = fixture.Tokens.TokenGets.Count;
        var callsBefore = fixture.Media.CallCount;

        var response = await clientA.GetAsync(MediaUrl(workspaceA, accountB));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("account.notFound",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        // Proof of isolation: no token read for the foreign account, no provider call.
        Assert.Equal(tokenGetsBefore, fixture.Tokens.TokenGets.Count);
        Assert.DoesNotContain(accountB, fixture.Tokens.TokenGets);
        Assert.Equal(callsBefore, fixture.Media.CallCount);
    }

    [Fact]
    public async Task UnknownAccountIs404WithoutProviderCall()
    {
        var (client, workspace, _, _) = await ConnectedAccountAsync("media-unknown@example.com", FreshWorkspace(), "u");
        var callsBefore = fixture.Media.CallCount;

        var response = await client.GetAsync(MediaUrl(workspace, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(callsBefore, fixture.Media.CallCount);
    }

    [Fact]
    public async Task DisconnectedAccountFailsLocallyWithZeroProviderCalls()
    {
        var (client, workspace, accountId, _) = await ConnectedAccountAsync("media-disc@example.com", FreshWorkspace(), "d");
        var disconnected = await client.DeleteAsync(
            $"/api/v1/workspaces/{workspace}/instagram/connections/{accountId}");
        Assert.Equal(HttpStatusCode.NoContent, disconnected.StatusCode);
        var callsBefore = fixture.Media.CallCount;

        var response = await client.GetAsync(MediaUrl(workspace, accountId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("account.alreadyDisconnected",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(callsBefore, fixture.Media.CallCount);
    }

    [Fact]
    public async Task ConnectedAccountWithoutTokenFailsSafelyWithoutProviderCall()
    {
        var (client, workspace, accountId, _) = await ConnectedAccountAsync("media-notoken@example.com", FreshWorkspace(), "nt");
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IProtectedTokenStore>().DeleteAsync(accountId);
        }
        var callsBefore = fixture.Media.CallCount;
        var getsBefore = fixture.Tokens.TokenGets.Count;

        var response = await client.GetAsync(MediaUrl(workspace, accountId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("account.tokenMissing",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(callsBefore, fixture.Media.CallCount);
        // Exactly one token read (this account's, returning null) — never a sibling's.
        Assert.Equal(getsBefore + 1, fixture.Tokens.TokenGets.Count);
        Assert.Contains(accountId, fixture.Tokens.TokenGets);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1000)]
    public async Task InvalidAndOversizedLimitsAreRejectedBeforeProvider(int limit)
    {
        var (client, workspace, accountId, _) = await ConnectedAccountAsync($"media-limit{limit}@example.com", FreshWorkspace(), "l");
        var callsBefore = fixture.Media.CallCount;

        var response = await client.GetAsync(MediaUrl(workspace, accountId, $"?limit={limit}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(MediaCatalogFailures.InvalidLimit,
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(callsBefore, fixture.Media.CallCount);
    }

    [Fact]
    public async Task MalformedCursorIsRejectedBeforeProvider()
    {
        var (client, workspace, accountId, _) = await ConnectedAccountAsync("media-badcur@example.com", FreshWorkspace(), "bc");
        var callsBefore = fixture.Media.CallCount;

        var response = await client.GetAsync(MediaUrl(workspace, accountId, "?cursor=%%%not-a-cursor%%%"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(MediaCatalogFailures.InvalidCursor,
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(callsBefore, fixture.Media.CallCount);
    }

    [Fact]
    public async Task RateLimitedProviderFailureMapsTo503WithoutLeakingProviderBody()
    {
        var (client, workspace, accountId, _) = await ConnectedAccountAsync("media-rate@example.com", FreshWorkspace(), "r");
        fixture.Media.ResultOverride = new MediaCatalogResult.Failed(MediaCatalogFailures.RateLimited, Transient: true);

        var response = await client.GetAsync(MediaUrl(workspace, accountId));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(MediaCatalogFailures.RateLimited,
            (JsonDocument.Parse(body)).RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("LONG-E2E", body);
        Assert.DoesNotContain("SHORT-E2E", body);
    }
}
