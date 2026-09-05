using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;
using Qasedak.Modules.Automations.Infrastructure.Persistence;
using Qasedak.Modules.Conversations.Domain.Conversations;
using Qasedak.Modules.Conversations.Infrastructure.Persistence;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

/// <summary>
/// M13-002 exact-account routing end to end over real PostgreSQL:
/// two connected Instagram accounts in one workspace share one participant
/// without thread merging, wrong-account sends, cross-account automation
/// execution or any first-active-account fallback. Unknown, foreign, disconnected,
/// token-less and legacy bindings refuse safely with stable codes and zero sends.
/// </summary>
[Collection(ApiTestEnvironment.Name)]
public sealed class ExactAccountRoutingTests(ApiPostgreSqlFixture fixture)
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
        // 200 proves inline normalization+dispatch ran; 202 would mean processing was
        // deferred (e.g. an exception left entries pending) and assertions below would lie.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response;
    }

    private sealed record LoginResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("accessToken")] string AccessToken);

    private async Task<string> TokenAsync(string email, Guid workspaceId)
    {
        await fixture.Client.PostAsJsonAsync("/api/v1/identity/register", new { email, password = "Passw0rd!23", displayName = "Exact Tester" });
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

    private async Task<(Guid AccountId, string ProviderId)> SeedAccountAsync(Guid workspaceId, string providerId, string? token)
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

        return (account.Id, providerId);
    }

    private async Task<Guid> SeedAutomationAsync(Guid workspaceId, ChannelAccountId? accountId, string name, string keyword = "price")
    {
        var scope = fixture.Factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAutomationRepository>();
        var definition = AutomationDefinition.Create(
            AutomationTrigger.CommentCreated(keyword), [],
            [new AutomationAction(ActionKind.SendDirectMessage, "DM: exact " + name)]);
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

    private List<(string AccessToken, string RecipientId, string Text)> SendsTo(string recipientId) =>
        fixture.Messaging.Sends.Where(s => s.RecipientId == recipientId).ToList();

    private static async Task<string?> FailureCodeAsync(HttpResponseMessage response)
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
            return payload.TryGetProperty("code", out var code) ? code.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    [Fact]
    public async Task SameParticipantOnTwoAccountsProjectsTwoSeparateThreads()
    {
        var tag = Guid.CreateVersion7().ToString("N");
        var workspaceId = Guid.CreateVersion7();
        var (accountA, providerA) = await SeedAccountAsync(workspaceId, "17841409" + tag + "01", "exact-token-A-" + tag);
        var (accountB, providerB) = await SeedAccountAsync(workspaceId, "17841409" + tag + "02", "exact-token-B-" + tag);
        var participant = "participant-" + tag;

        Assert.True((await PostSignedAsync(MessageBody(providerA, participant, "mid-" + tag + "-a", "hello A"))).IsSuccessStatusCode);
        Assert.True((await PostSignedAsync(MessageBody(providerB, participant, "mid-" + tag + "-b", "hello B"))).IsSuccessStatusCode);

        await using var conversations = NewConversationsContext();
        var threads = await conversations.Conversations.Include(c => c.Messages)
            .Where(c => c.WorkspaceId == workspaceId && c.ParticipantId == participant)
            .ToListAsync();
        Assert.Equal(2, threads.Count);
        var threadA = Assert.Single(threads, t => t.ChannelAccountId == ChannelAccountId.From(accountA));
        var threadB = Assert.Single(threads, t => t.ChannelAccountId == ChannelAccountId.From(accountB));
        Assert.Equal("hello A", Assert.Single(threadA.Messages).Body);
        Assert.Equal("hello B", Assert.Single(threadB.Messages).Body);

        // The inbox list surfaces the exact account per thread (additive, non-breaking).
        using var client = AuthedClient(await TokenAsync("exact-list-" + tag + "@example.com", workspaceId));
        var page = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workspaces/{workspaceId}/conversations?page=1&pageSize=10");
        var ids = page.GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("participantId").GetString() == participant)
            .Select(i => i.GetProperty("channelAccountId").GetString())
            .ToHashSet();
        Assert.Contains(accountA.ToString("D"), ids);
        Assert.Contains(accountB.ToString("D"), ids);
    }

    [Fact]
    public async Task OutboundRepliesUseExactAccountTokens()
    {
        var tag = Guid.CreateVersion7().ToString("N");
        var workspaceId = Guid.CreateVersion7();
        var (accountA, providerA) = await SeedAccountAsync(workspaceId, "17841409" + tag + "11", "exact-token-A-" + tag);
        var (accountB, providerB) = await SeedAccountAsync(workspaceId, "17841409" + tag + "12", "exact-token-B-" + tag);
        var participant = "replier-" + tag;
        await PostSignedAsync(MessageBody(providerA, participant, "mid-" + tag + "-a", "hi A"));
        await PostSignedAsync(MessageBody(providerB, participant, "mid-" + tag + "-b", "hi B"));

        await using var conversations = NewConversationsContext();
        var threadA = await conversations.Conversations
            .SingleAsync(c => c.WorkspaceId == workspaceId && c.ParticipantId == participant && c.ChannelAccountId == ChannelAccountId.From(accountA));
        var threadB = await conversations.Conversations
            .SingleAsync(c => c.WorkspaceId == workspaceId && c.ParticipantId == participant && c.ChannelAccountId == ChannelAccountId.From(accountB));

        using var client = AuthedClient(await TokenAsync("exact-send-" + tag + "@example.com", workspaceId));
        var replyA = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/conversations/{threadA.Id}/replies", new { text = "answer A" });
        var replyB = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/conversations/{threadB.Id}/replies", new { text = "answer B" });
        Assert.True(replyA.IsSuccessStatusCode, await replyA.Content.ReadAsStringAsync());
        Assert.True(replyB.IsSuccessStatusCode, await replyB.Content.ReadAsStringAsync());

        var sendA = Assert.Single(SendsTo(participant), s => s.Text == "answer A");
        var sendB = Assert.Single(SendsTo(participant), s => s.Text == "answer B");
        Assert.Equal("exact-token-A-" + tag, sendA.AccessToken);
        Assert.Equal("exact-token-B-" + tag, sendB.AccessToken);
    }

    [Fact]
    public async Task ForeignWorkspaceAccountReplyIsRejectedWithoutSend()
    {
        var tag = Guid.CreateVersion7().ToString("N");
        var workspaceW = Guid.CreateVersion7();
        var workspaceX = Guid.CreateVersion7();
        var (foreignAccount, _) = await SeedAccountAsync(workspaceX, "17841409" + tag + "21", "foreign-token-" + tag);

        Guid threadId;
        await using (var conversations = NewConversationsContext())
        {
            var thread = Conversation.Create(
                Guid.CreateVersion7(), workspaceW, "instagram", "victim-" + tag,
                DateTimeOffset.UtcNow, ChannelAccountId.From(foreignAccount));
            thread.AppendMessage(Guid.CreateVersion7(), MessageDirection.Inbound, "mid-" + tag, "victim-" + tag, "hi", DateTimeOffset.UtcNow);
            await conversations.Conversations.AddAsync(thread);
            await conversations.SaveChangesAsync();
            threadId = thread.Id;
        }

        using var client = AuthedClient(await TokenAsync("exact-foreign-" + tag + "@example.com", workspaceW));
        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceW}/conversations/{threadId}/replies", new { text = "cross attempt" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("instagram.accountWorkspaceMismatch", await FailureCodeAsync(response));
        Assert.Empty(SendsTo("victim-" + tag));
    }

    [Fact]
    public async Task DisconnectedMissingUnknownAndLegacyBindingsRefuseWithoutFallback()
    {
        var tag = Guid.CreateVersion7().ToString("N");
        var workspaceId = Guid.CreateVersion7();
        var (staleAccount, staleProvider) = await SeedAccountAsync(workspaceId, "17841409" + tag + "31", "stale-token-" + tag);
        var (bareAccount, bareProvider) = await SeedAccountAsync(workspaceId, "17841409" + tag + "32", token: null);
        var unknownAccount = Guid.CreateVersion7();
        var participant = "refused-" + tag;

        async Task<Guid> SeedThreadAsync(string suffix, ChannelAccountId? account)
        {
            await using var conversations = NewConversationsContext();
            var thread = Conversation.Create(
                Guid.CreateVersion7(), workspaceId, "instagram", participant + "-" + suffix,
                DateTimeOffset.UtcNow, account);
            thread.AppendMessage(Guid.CreateVersion7(), MessageDirection.Inbound, "mid-" + tag + suffix, participant + "-" + suffix, "hi", DateTimeOffset.UtcNow);
            await conversations.Conversations.AddAsync(thread);
            await conversations.SaveChangesAsync();
            return thread.Id;
        }

        var disconnectedThread = await SeedThreadAsync("dc", ChannelAccountId.From(staleAccount));
        var tokenlessThread = await SeedThreadAsync("nt", ChannelAccountId.From(bareAccount));
        var unknownThread = await SeedThreadAsync("uk", new ChannelAccountId(unknownAccount));
        var legacyThread = await SeedThreadAsync("lg", null);
        _ = (staleProvider, bareProvider);

        // Disconnect the stale account through its own aggregate path.
        var disconnectScope = fixture.Factory.Services.CreateScope();
        await using (var instagram = disconnectScope.ServiceProvider.GetRequiredService<InstagramDbContext>())
        {
            var account = await instagram.Accounts.SingleAsync(a => a.Id == staleAccount);
            account.Disconnect(DateTimeOffset.UtcNow);
            await instagram.SaveChangesAsync();
        }

        using var client = AuthedClient(await TokenAsync("exact-refuse-" + tag + "@example.com", workspaceId));
        var cases = new (Guid ThreadId, string Suffix, string Code)[]
        {
            (disconnectedThread, "dc", "instagram.accountDisconnected"),
            (tokenlessThread, "nt", "instagram.tokenMissing"),
            (unknownThread, "uk", "instagram.unknownAccount"),
            (legacyThread, "lg", "reply.accountUnresolved"),
        };

        foreach (var (threadId, suffix, code) in cases)
        {
            var response = await client.PostAsJsonAsync(
                $"/api/v1/workspaces/{workspaceId}/conversations/{threadId}/replies", new { text = "must not send " + suffix });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(code, await FailureCodeAsync(response));
        }

        Assert.DoesNotContain(fixture.Messaging.Sends, s => s.RecipientId.StartsWith(participant, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AutomationExecutesOnlyForItsBoundAccount()
    {
        var tag = Guid.CreateVersion7().ToString("N");
        var workspaceId = Guid.CreateVersion7();
        var (accountA, providerA) = await SeedAccountAsync(workspaceId, "17841409" + tag + "41", "auto-token-A-" + tag);
        var (accountB, providerB) = await SeedAccountAsync(workspaceId, "17841409" + tag + "42", "auto-token-B-" + tag);
        var automationA = await SeedAutomationAsync(workspaceId, ChannelAccountId.From(accountA), "auto-a-" + tag);
        var automationB = await SeedAutomationAsync(workspaceId, ChannelAccountId.From(accountB), "auto-b-" + tag);
        var commenterA = "commenter-a-" + tag;
        var commenterB = "commenter-b-" + tag;

        Assert.True((await PostSignedAsync(CommentBody(providerA, "c-" + tag + "-a", commenterA, "what is the price?"))).IsSuccessStatusCode);

        Assert.Single(SendsTo(commenterA));
        Assert.Empty(SendsTo(commenterB));
        Assert.NotNull(await FindRunAsync(automationA));
        Assert.Null(await FindRunAsync(automationB));

        Assert.True((await PostSignedAsync(CommentBody(providerB, "c-" + tag + "-b", commenterB, "what is the price?"))).IsSuccessStatusCode);
        Assert.Single(SendsTo(commenterB));
        Assert.NotNull(await FindRunAsync(automationB));
        // Still exactly one send per commenter: no cross-execution, no redelivery echo.
        Assert.Single(SendsTo(commenterA));
    }

    [Fact]
    public async Task LegacyUnboundAutomationNeverExecutesOnExactEvents()
    {
        var tag = Guid.CreateVersion7().ToString("N");
        var workspaceId = Guid.CreateVersion7();
        var (_, providerA) = await SeedAccountAsync(workspaceId, "17841409" + tag + "51", "legacy-auto-token-" + tag);
        var legacyAutomation = await SeedAutomationAsync(workspaceId, null, "legacy-auto-" + tag);
        var commenter = "commenter-legacy-" + tag;

        Assert.True((await PostSignedAsync(CommentBody(providerA, "c-" + tag + "-legacy", commenter, "what is the price?"))).IsSuccessStatusCode);

        Assert.Empty(SendsTo(commenter));
        Assert.Null(await FindRunAsync(legacyAutomation));
    }

    [Fact]
    public async Task DisconnectedAccountInboundIsDropped()
    {
        var tag = Guid.CreateVersion7().ToString("N");
        var workspaceId = Guid.CreateVersion7();
        var (staleAccount, staleProvider) = await SeedAccountAsync(workspaceId, "17841409" + tag + "61", "drop-token-" + tag);
        var participant = "dropped-" + tag;

        var disconnectScope = fixture.Factory.Services.CreateScope();
        await using (var instagram = disconnectScope.ServiceProvider.GetRequiredService<InstagramDbContext>())
        {
            var account = await instagram.Accounts.SingleAsync(a => a.Id == staleAccount);
            account.Disconnect(DateTimeOffset.UtcNow);
            await instagram.SaveChangesAsync();
        }

        var response = await PostSignedAsync(MessageBody(staleProvider, participant, "mid-" + tag, "hello?"));
        Assert.True(response.IsSuccessStatusCode);

        await using var conversations = NewConversationsContext();
        Assert.Empty(await conversations.Conversations
            .Where(c => c.WorkspaceId == workspaceId && c.ParticipantId == participant)
            .ToListAsync());
    }

    private async Task<AutomationRunRow?> FindRunAsync(Guid automationId)
    {
        var scope = fixture.Factory.Services.CreateScope();
        await using var automations = scope.ServiceProvider.GetRequiredService<AutomationsDbContext>();
        return await automations.AutomationRuns.SingleOrDefaultAsync(r => r.AutomationId == automationId);
    }
}
