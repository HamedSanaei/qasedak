using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qasedak.Modules.Contacts.Infrastructure.Persistence;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

/// <summary>
/// Contact CRM HTTP surface: authenticated, paginated, searchable list; detail with notes;
/// tag and note mutations; foreign/unknown contacts are 404.
/// </summary>
[Collection(ApiTestEnvironment.Name)]
public sealed class ContactEndpointTests(ApiPostgreSqlFixture fixture)
{
    private const string AccountProviderId = "17841400000000000";

    private static readonly Guid SeededWorkspaceId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static readonly DateTimeOffset SentAt = DateTimeOffset.FromUnixTimeSeconds(1771901000);

    private sealed record LoginResponse([property: JsonPropertyName("accessToken")] string AccessToken);

    private sealed record PageResponse(
        [property: JsonPropertyName("totalCount")] int TotalCount,
        [property: JsonPropertyName("items")] List<ItemResponse> Items);

    private sealed record ItemResponse(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("displayName")] string DisplayName,
        [property: JsonPropertyName("tags")] List<string> Tags);

    private static byte[] Body(string mid, string sender) => Encoding.UTF8.GetBytes(
        "{\"object\":\"instagram\",\"entry\":[{\"id\":\"" + AccountProviderId + "\",\"messaging\":[" +
        "{\"sender\":{\"id\":\"" + sender + "\"},\"recipient\":{\"id\":\"" + AccountProviderId + "\"}," +
        "\"timestamp\":" + SentAt.ToUnixTimeSeconds() + "," +
        "\"message\":{\"mid\":\"" + mid + "\",\"text\":\"hi\"}}]}]}");

    [Fact]
    public async Task ContactsListIsAuthenticatedSearchableAndWorkspaceScoped()
    {
        await SeedBoundAccountAsync();
        foreach (var sender in new[] { "ep-alpha", "ep-alphabet", "ep-zulu" })
        {
            await PostWebhookAsync("mid-" + sender, sender);
        }

        using var client = AuthedClient(await TokenAsync("contacts-list@example.com"));

        var all = await client.GetFromJsonAsync<PageResponse>($"/api/v1/workspaces/{SeededWorkspaceId}/contacts");
        Assert.True(all!.TotalCount >= 3);

        var search = await client.GetFromJsonAsync<PageResponse>(
            $"/api/v1/workspaces/{SeededWorkspaceId}/contacts?search=ep-alph");
        Assert.Equal(2, search!.TotalCount);
        Assert.All(search.Items, item => Assert.StartsWith("ep-alph", item.DisplayName));

        // Anonymous is rejected.
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Client.GetAsync(
            $"/api/v1/workspaces/{SeededWorkspaceId}/contacts")).StatusCode);

        // A workspace the caller does not belong to is denied outright (membership policy).
        var other = await client.GetAsync(
            $"/api/v1/workspaces/{Guid.CreateVersion7()}/contacts");
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
    }

    [Fact]
    public async Task TagsAndNotesFlowThroughEndpoints()
    {
        await SeedBoundAccountAsync();
        await PostWebhookAsync("mid-tags-1", "ep-tagger");

        using var client = AuthedClient(await TokenAsync("contacts-crm@example.com"));
        var scope = fixture.Factory.Services.CreateScope();
        await using var context = scope.ServiceProvider.GetRequiredService<ContactsDbContext>();
        var contactId = await context.ContactIdentities
            .Where(i => i.WorkspaceId == SeededWorkspaceId && i.ProviderIdentity == "ep-tagger")
            .Select(i => i.ContactId)
            .SingleAsync();
        var baseUri = $"/api/v1/workspaces/{SeededWorkspaceId}/contacts/{contactId}";

        // Tag it.
        var tagResponse = await client.PostAsJsonAsync($"{baseUri}/tags", new { tag = "Hot Lead" });
        Assert.True(tagResponse.IsSuccessStatusCode, await tagResponse.Content.ReadAsStringAsync());

        // Note it.
        var noteResponse = await client.PostAsJsonAsync($"{baseUri}/notes", new { body = "Interested in the annual plan." });
        Assert.True(noteResponse.IsSuccessStatusCode, await noteResponse.Content.ReadAsStringAsync());

        // Detail shows both.
        var detail = await client.GetFromJsonAsync<JsonElement>(baseUri);
        Assert.Equal("hot lead", detail.GetProperty("tags").EnumerateArray().Single().GetString());
        Assert.Single(detail.GetProperty("notes").EnumerateArray());

        // Unknown contact is 404 with the rule code.
        var missing = await client.GetAsync($"/api/v1/workspaces/{SeededWorkspaceId}/contacts/{Guid.CreateVersion7()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        // A workspace the caller does not belong to cannot resolve or mutate this contact
        // (denied by the membership policy before any lookup).
        var foreignBase = $"/api/v1/workspaces/{Guid.CreateVersion7()}/contacts/{contactId}";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(foreignBase)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"{foreignBase}/tags", new { tag = "x" })).StatusCode);
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(string mid, string sender)
    {
        var body = Body(mid, sender);
        using var content = new ByteArrayContent(body);
        content.Headers.Add("X-Hub-Signature-256", "sha256=" + Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(ApiPostgreSqlFixture.MetaAppSecret), body)).ToLowerInvariant());
        return await fixture.Client.PostAsync("/api/v1/webhooks/instagram", content);
    }

    private async Task SeedBoundAccountAsync()
    {
        var scope = fixture.Factory.Services.CreateScope();
        await using var context = scope.ServiceProvider.GetRequiredService<InstagramDbContext>();
        if (await context.Accounts.AnyAsync(a => a.ProviderUserId == AccountProviderId))
        {
            return;
        }

        await context.Accounts.AddAsync(ConnectedAccount.Create(
            Guid.CreateVersion7(),
            SeededWorkspaceId,
            AccountProviderId,
            ConnectionPath.InstagramLogin,
            ["instagram_business_manage_messages"],
            DateTimeOffset.UtcNow.AddDays(30),
            DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
    }

    private async Task<string> TokenAsync(string email)
    {
        await fixture.Client.PostAsJsonAsync("/api/v1/identity/register", new { email, password = "Passw0rd!23", displayName = "Contacts Tester" });
        var login = await fixture.Client.PostAsJsonAsync("/api/v1/identity/login", new { email, password = "Passw0rd!23" });
        var payload = await login.Content.ReadFromJsonAsync<LoginResponse>();
        var token = payload!.AccessToken;

        // The tester must be a member of the seeded workspace now that the API enforces
        // workspace membership at the authorization layer.
        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/identity/me");
        me.Headers.Authorization = new("Bearer", token);
        using var meResponse = await fixture.Client.SendAsync(me);
        meResponse.EnsureSuccessStatusCode();
        var userId = Guid.Parse((await meResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetString()!);
        await fixture.EnsureWorkspaceMemberAsync(SeededWorkspaceId, userId);

        return token;
    }

    private HttpClient AuthedClient(string token)
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }
}
