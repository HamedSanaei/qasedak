using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;
using Qasedak.Modules.Automations.Infrastructure.Persistence;
using Qasedak.Modules.Conversations.Infrastructure.Persistence;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

/// <summary>
/// M13-002 correction regressions: inbound routing must resolve the one active
/// connected account deterministically. Disconnected reconnect history must never
/// shadow the new active row, and a duplicate active routing identity across
/// workspaces must fail closed instead of picking the first row.
/// Identity semantics (proven against current official Meta documentation):
/// the OAuth code-exchange user_id IS the professional IG_ID carried by webhook
/// entry.id for Instagram Login, so the stored ProviderUserId is the canonical
/// routing identity — tests use one shared synthetic value deliberately, not by
/// accident (see meta-instagram-platform-contract.md §2/Outcome A).
/// </summary>
[Collection(ApiTestEnvironment.Name)]
public sealed class InboundAccountResolutionTests(ApiPostgreSqlFixture fixture)
{
    private const string WebhookEndpoint = "/api/v1/webhooks/instagram";

    private static string Signed(byte[] body) => "sha256=" + Convert.ToHexString(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(ApiPostgreSqlFixture.MetaAppSecret), body)).ToLowerInvariant();

    private static byte[] MessageBody(string accountProviderId, string participantId, string mid, string text)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return Encoding.UTF8.GetBytes(
            "{\"object\":\"instagram\",\"entry\":[{\"id\":\"" + accountProviderId + "\",\"messaging\":[" +
            "{\"sender\":{\"id\":\"" + participantId + "\"},\"recipient\":{\"id\":\"" + accountProviderId + "\"}," +
            "\"timestamp\":" + timestamp + "," +
            "\"message\":{\"mid\":\"" + mid + "\",\"text\":\"" + text + "\"}}]}]}");
    }

    private static byte[] CommentBody(string accountProviderId, string commentId, string commenterId, string text)
    {
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return Encoding.UTF8.GetBytes(
            "{\"object\":\"instagram\",\"entry\":[{\"id\":\"" + accountProviderId + "\",\"changes\":[" +
            "{\"field\":\"comments\",\"value\":{\"id\":\"" + commentId + "\",\"from\":{\"id\":\"" + commenterId + "\"}," +
            "\"text\":\"" + text + "\",\"created_time\":" + created + "}}]}]}");
    }

    private async Task<HttpResponseMessage> PostSignedAsync(byte[] body)
    {
        using var content = new ByteArrayContent(body);
        content.Headers.Add("X-Hub-Signature-256", Signed(body));
        var response = await fixture.Client.PostAsync(WebhookEndpoint, content);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return response;
    }

    private sealed record LoginResponse(
        [property: JsonPropertyName("accessToken")] string AccessToken);

    private async Task<string> TokenAsync(string email, Guid workspaceId)
    {
        await fixture.Client.PostAsJsonAsync("/api/v1/identity/register", new { email, password = "Passw0rd!23", displayName = "Resolution Tester" });
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

    private async Task<Guid> SeedAccountAsync(Guid workspaceId, string providerId, string? token)
    {
        var scope = fixture.Factory.Services.CreateScope();
        var instagram = scope.ServiceProvider.GetRequiredService<InstagramDbContext>();
        var account = ConnectedAccount.Create(
            Guid.CreateVersion7(), workspaceId, providerId, ConnectionPath.InstagramLogin,
            ["instagram_business_manage_messages"], DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow);
        await instagram.Accounts.AddAsync(account);
        await instagram.SaveChangesAsync();
        if (token is not null)
        {
            var tokens = scope.ServiceProvider.GetRequiredService<IProtectedTokenStore>();
            await tokens.StoreAsync(account.Id, token);
            await instagram.SaveChangesAsync();
        }

        return account.Id;
    }

    private async Task DisconnectAccountAsync(Guid accountId)
    {
        var scope = fixture.Factory.Services.CreateScope();
        await using var instagram = scope.ServiceProvider.GetRequiredService<InstagramDbContext>();
        var account = await instagram.Accounts.SingleAsync(a => a.Id == accountId);
        account.Disconnect(DateTimeOffset.UtcNow);
        await instagram.SaveChangesAsync();
    }

    private async Task<Guid> SeedAutomationAsync(Guid workspaceId, ChannelAccountId? accountId, string name)
    {
        var scope = fixture.Factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAutomationRepository>();
        var definition = AutomationDefinition.Create(
            AutomationTrigger.CommentCreated("price"), [],
            [new AutomationAction(ActionKind.SendDirectMessage, "DM: resolved " + name)]);
        var automation = Automation.Create(Guid.CreateVersion7(), workspaceId, name, definition, DateTimeOffset.UtcNow, accountId);
        automation.Activate(DateTimeOffset.UtcNow);
        await repository.SaveChangesAsync(automation);
        return automation.Id;
    }

    private ConversationsDbContext NewConversationsContext()
    {
        var scope = fixture.Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ConversationsDbContext>();
    }

    private async Task<AutomationRunRow?> FindRunAsync(Guid automationId)
    {
        var scope = fixture.Factory.Services.CreateScope();
        await using var automations = scope.ServiceProvider.GetRequiredService<AutomationsDbContext>();
        return await automations.AutomationRuns.SingleOrDefaultAsync(r => r.AutomationId == automationId);
    }

    [Fact]
    public async Task ReconnectResolvesNewActiveAccountForMessagesAndAutomations()
    {
        var tag = Guid.CreateVersion7().ToString("N");
        var workspaceId = Guid.CreateVersion7();
        // Canonical routing identity shared deliberately: OAuth user_id == webhook IG_ID.
        var providerId = "17841409" + tag + "71";

        var oldAccount = await SeedAccountAsync(workspaceId, providerId, "old-token-" + tag);
        await DisconnectAccountAsync(oldAccount);
        var newAccount = await SeedAccountAsync(workspaceId, providerId, "new-token-" + tag);
        var oldAutomation = await SeedAutomationAsync(workspaceId, ChannelAccountId.From(oldAccount), "stale-auto-" + tag);
        var newAutomation = await SeedAutomationAsync(workspaceId, ChannelAccountId.From(newAccount), "fresh-auto-" + tag);
        var participant = "reconnect-user-" + tag;
        var commenter = "reconnect-fan-" + tag;

        await PostSignedAsync(MessageBody(providerId, participant, "mid-" + tag, "hello again"));
        await PostSignedAsync(CommentBody(providerId, "comment-" + tag, commenter, "what is the price?"));

        // The conversation carries the NEW account identity, never the disconnected row.
        await using var conversations = NewConversationsContext();
        var threads = await conversations.Conversations
            .Where(c => c.WorkspaceId == workspaceId && c.ParticipantId == participant)
            .ToListAsync();
        var thread = Assert.Single(threads);
        Assert.Equal(ChannelAccountId.From(newAccount), thread.ChannelAccountId);

        // Only the automation bound to the NEW account executes, through the NEW token.
        Assert.NotNull(await FindRunAsync(newAutomation));
        Assert.Null(await FindRunAsync(oldAutomation));
        var send = Assert.Single(fixture.Messaging.Sends, s => s.RecipientId == commenter);
        Assert.Equal("new-token-" + tag, send.AccessToken);

        // The inbox list exposes the new account for the thread.
        using var client = AuthedClient(await TokenAsync("resolution-" + tag + "@example.com", workspaceId));
        var page = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workspaces/{workspaceId}/conversations?page=1&pageSize=10");
        var row = page.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("participantId").GetString() == participant);
        Assert.Equal(newAccount.ToString("D"), row.GetProperty("channelAccountId").GetString());
    }

    [Fact]
    public async Task DisconnectedOnlyAccountWebhookIsDropped()
    {
        var tag = Guid.CreateVersion7().ToString("N");
        var workspaceId = Guid.CreateVersion7();
        var providerId = "17841409" + tag + "72";
        var account = await SeedAccountAsync(workspaceId, providerId, "gone-token-" + tag);
        await DisconnectAccountAsync(account);
        var participant = "ghost-" + tag;

        await PostSignedAsync(MessageBody(providerId, participant, "mid-" + tag, "anybody here?"));

        await using var conversations = NewConversationsContext();
        Assert.Empty(await conversations.Conversations
            .Where(c => c.WorkspaceId == workspaceId && c.ParticipantId == participant)
            .ToListAsync());
        Assert.DoesNotContain(fixture.Messaging.Sends, s => s.RecipientId == participant);
    }

    [Fact]
    public async Task DuplicateActiveIdentityAcrossWorkspacesFailsClosed()
    {
        var tag = Guid.CreateVersion7().ToString("N");
        var workspaceA = Guid.CreateVersion7();
        var workspaceB = Guid.CreateVersion7();
        // Same canonical routing identity actively owned by two workspaces: ambiguous.
        var providerId = "17841409" + tag + "73";
        await SeedAccountAsync(workspaceA, providerId, "token-a-" + tag);
        await SeedAccountAsync(workspaceB, providerId, "token-b-" + tag);
        var participant = "ambiguous-" + tag;

        await PostSignedAsync(MessageBody(providerId, participant, "mid-" + tag, "hello?"));

        // Neither workspace receives the conversation: no silent choice.
        await using var conversations = NewConversationsContext();
        Assert.Empty(await conversations.Conversations
            .Where(c => c.ParticipantId == participant)
            .ToListAsync());
        Assert.DoesNotContain(fixture.Messaging.Sends, s => s.RecipientId == participant);
    }
}
