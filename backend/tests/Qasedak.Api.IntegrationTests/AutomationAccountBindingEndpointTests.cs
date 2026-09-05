using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

/// <summary>
/// Automation account-binding API contract: bindings are set at creation, surface in
/// reads, and are immutable afterwards (rebind means a new automation).
/// </summary>
[Collection(ApiTestEnvironment.Name)]
public sealed class AutomationAccountBindingEndpointTests(ApiPostgreSqlFixture fixture)
{
    private sealed record LoginResponse(
        [property: JsonPropertyName("accessToken")] string AccessToken);

    private static object Definition() => new
    {
        triggerKind = "CommentCreated",
        keywordFilters = new[] { "price" },
        conditions = Array.Empty<object>(),
        actions = new[] { new { kind = "SendDirectMessage", messageText = "bound hello" } },
    };

    private async Task<string> TokenAsync(string email, Guid workspaceId)
    {
        await fixture.Client.PostAsJsonAsync("/api/v1/identity/register", new { email, password = "Passw0rd!23", displayName = "Binding Tester" });
        var login = await fixture.Client.PostAsJsonAsync("/api/v1/identity/login", new { email, password = "Passw0rd!23" });
        var payload = await login.Content.ReadFromJsonAsync<LoginResponse>();
        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/identity/me");
        me.Headers.Authorization = new("Bearer", payload!.AccessToken);
        using var meResponse = await fixture.Client.SendAsync(me);
        meResponse.EnsureSuccessStatusCode();
        var userId = Guid.Parse((await meResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetString()!);
        await fixture.EnsureWorkspaceMemberAsync(workspaceId, userId);
        return payload.AccessToken;
    }

    private HttpClient AuthedClient(string token)
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    [Fact]
    public async Task CreatePersistsBindingAndReadsSurfaceIt()
    {
        var tag = Guid.CreateVersion7().ToString("N");
        var workspaceId = Guid.CreateVersion7();
        var accountId = Guid.CreateVersion7();
        using var client = AuthedClient(await TokenAsync("binding-create-" + tag + "@example.com", workspaceId));

        var created = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/automations",
            new { name = "bound flow " + tag, definition = Definition(), channelAccountId = accountId });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var createdPayload = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(accountId.ToString("D"), createdPayload.GetProperty("channelAccountId").GetString());

        var automationId = createdPayload.GetProperty("id").GetString()!;
        var fetched = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/workspaces/{workspaceId}/automations/{automationId}");
        Assert.Equal(accountId.ToString("D"), fetched.GetProperty("channelAccountId").GetString());

        // Unbound creation stays legacy (null), and empty account ids are rejected.
        var legacy = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/automations",
            new { name = "legacy flow " + tag, definition = Definition() });
        Assert.True(legacy.IsSuccessStatusCode, await legacy.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, (await legacy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("channelAccountId").ValueKind);

        var empty = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/automations",
            new { name = "empty flow " + tag, definition = Definition(), channelAccountId = Guid.Empty });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task BindingChangeThroughPutIsRejectedAsImmutable()
    {
        var tag = Guid.CreateVersion7().ToString("N");
        var workspaceId = Guid.CreateVersion7();
        var accountId = Guid.CreateVersion7();
        using var client = AuthedClient(await TokenAsync("binding-put-" + tag + "@example.com", workspaceId));

        var created = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/automations",
            new { name = "immutable flow " + tag, definition = Definition(), channelAccountId = accountId });
        created.EnsureSuccessStatusCode();
        var automationId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        // Same binding echoes through fine (no-op); a different binding is refused.
        var same = await client.PutAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/automations/{automationId}",
            new { name = "immutable flow " + tag, definition = Definition(), channelAccountId = accountId });
        Assert.True(same.IsSuccessStatusCode, await same.Content.ReadAsStringAsync());

        var changed = await client.PutAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/automations/{automationId}",
            new { name = "immutable flow " + tag, definition = Definition(), channelAccountId = Guid.CreateVersion7() });
        Assert.Equal(HttpStatusCode.BadRequest, changed.StatusCode);
        var payload = await changed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("automation.bindingImmutable", payload.GetProperty("code").GetString());
    }
}
