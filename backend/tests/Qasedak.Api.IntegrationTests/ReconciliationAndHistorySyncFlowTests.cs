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
using Qasedak.Modules.Conversations.Infrastructure.Persistence;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.Effects;
using Qasedak.Modules.Instagram.Application.HistorySync;
using Qasedak.Modules.Instagram.Application.Reconciliation;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.Infrastructure.Effects;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

/// <summary>
/// M13-013 end-to-end over real PostgreSQL: recovered comments enter the EXISTING
/// automation path (same evaluator, run ledger and M13-009 effect ledger) and converge
/// with signed webhook deliveries on one logical trigger; conversation history imports
/// through the channel-neutral gateway and NEVER triggers DM automations; the manual
/// resync endpoint authorizes exact-account ownership before enqueueing only; initial
/// sync is ensured at connect without provider I/O in the OAuth callback.
/// </summary>
[Collection(ApiTestEnvironment.Name)]
public sealed class ReconciliationAndHistorySyncFlowTests(ApiPostgreSqlFixture fixture)
{
    private const string Endpoint = "/api/v1/webhooks/instagram";

    private static readonly DateTimeOffset CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1771900500);

    // Recovered-comment rows must sit INSIDE the 14-day reconciliation horizon when
    // the sweep's real clock runs; webhook deliveries are not horizon-gated.
    private static DateTimeOffset RecentCommentTime() => DateTimeOffset.UtcNow.AddMinutes(-5);

    private static string Signed(byte[] body) => "sha256=" + Convert.ToHexString(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(ApiPostgreSqlFixture.MetaAppSecret), body)).ToLowerInvariant();

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static byte[] CommentBody(string accountProviderId, string commentId, string text, string mediaId, string? fromId) => Encoding.UTF8.GetBytes(
        "{\"object\":\"instagram\",\"entry\":[{\"id\":\"" + accountProviderId + "\",\"time\":" + NowMs() + ",\"changes\":[{" +
        "\"field\":\"comments\",\"value\":{\"id\":\"" + commentId + "\",\"text\":\"" + text + "\"," +
        "\"from\":{\"id\":\"" + fromId + "\"},\"media\":{\"id\":\"" + mediaId + "\",\"media_product_type\":\"FEED\"}}}]}]}");

    [Fact]
    public async Task CommentReconciliationTriggersExistingAutomationPathExactlyOnce()
    {
        var seeded = await SeedCommentAutomationAsync("601", ActionKind.SendDirectMessage);
        fixture.CommentHistory.Reset();
        fixture.CommentHistory.ScriptMedia("media-601", new ProviderCommentRow(
            "comment-recon-601", "526-recipient-601", "follower", "what is the price?",
            RecentCommentTime(), "media-601", null, false));

        await RunSweepAsync(seeded.AccountId);

        var run = await SingleRunAsync(seeded.AutomationId);
        Assert.NotNull(run);
        Assert.Equal(AutomationRunStatus.Completed, run.Status);
        var send = Assert.Single(fixture.PrivateReplies.Sends, s => s.CommentId == "comment-recon-601");
        Assert.Equal("test-access-token-601", send.AccessToken);
        var effect = await SingleEffectAsync(seeded.AccountId, "comment-recon-601", InstagramEffectType.PrivateReply);
        Assert.NotNull(effect);

        // A second sweep of the same comment: zero new provider sends (semantic
        // run/effect idempotency), zero new runs.
        await RunSweepAsync(seeded.AccountId);
        Assert.Single(fixture.PrivateReplies.Sends, s => s.CommentId == "comment-recon-601");
        Assert.Equal(1, await CountRunsAsync(seeded.AutomationId));
    }

    [Fact]
    public async Task SignedWebhookThenReconciliationConvergesOnOneLogicalTrigger()
    {
        var seeded = await SeedCommentAutomationAsync("602", ActionKind.SendDirectMessage);

        // 1) Real signed webhook delivers comment C.
        var body = CommentBody(seeded.ProviderId, "comment-602", "what is the price?", "media-602", "526-recipient-602");
        var response = await fixture.Client.PostAsync(Endpoint, new ByteArrayContent(body)
        {
            Headers = { { "X-Hub-Signature-256", Signed(body) } },
        });
        Assert.True(response.IsSuccessStatusCode);

        // 2) Reconciliation later observes the SAME provider comment id.
        fixture.CommentHistory.Reset();
        fixture.CommentHistory.ScriptMedia("media-602", new ProviderCommentRow(
            "comment-602", "526-recipient-602", "follower", "what is the price?",
            RecentCommentTime(), "media-602", null, false));
        await RunSweepAsync(seeded.AccountId);

        // One logical run, one provider mutation, one effect.
        Assert.Equal(1, await CountRunsAsync(seeded.AutomationId));
        Assert.Single(fixture.PrivateReplies.Sends, s => s.CommentId == "comment-602");
        Assert.Single(await EffectsForAsync(seeded.AccountId, "comment-602"));
    }

    [Fact]
    public async Task ReconciliationThenSignedWebhookConvergesOnOneLogicalTrigger()
    {
        var seeded = await SeedCommentAutomationAsync("603", ActionKind.SendDirectMessage);

        fixture.CommentHistory.Reset();
        fixture.CommentHistory.ScriptMedia("media-603", new ProviderCommentRow(
            "comment-603", "526-recipient-603", "follower", "what is the price?",
            RecentCommentTime(), "media-603", null, false));
        await RunSweepAsync(seeded.AccountId);
        Assert.Single(fixture.PrivateReplies.Sends, s => s.CommentId == "comment-603");

        var body = CommentBody(seeded.ProviderId, "comment-603", "what is the price?", "media-603", "526-recipient-603");
        var response = await fixture.Client.PostAsync(Endpoint, new ByteArrayContent(body)
        {
            Headers = { { "X-Hub-Signature-256", Signed(body) } },
        });
        Assert.True(response.IsSuccessStatusCode);

        Assert.Equal(1, await CountRunsAsync(seeded.AutomationId));
        Assert.Single(fixture.PrivateReplies.Sends, s => s.CommentId == "comment-603");
    }

    [Fact]
    public async Task ReconciliationWithoutActiveAutomationMakesZeroProviderCalls()
    {
        var seeded = await SeedAccountOnlyAsync("604");
        fixture.CommentHistory.Reset();
        fixture.Media.Reset();

        await RunSweepAsync(seeded.AccountId);

        Assert.Equal(0, fixture.CommentHistory.CallCount);
        Assert.Equal(0, fixture.Media.CallCount);
    }

    [Fact]
    public async Task HistorySyncImportsConversationsButNeverTriggersDmAutomations()
    {
        var seeded = await SeedDmAutomationAsync("605");
        fixture.ConversationHistory.Reset();
        var conversationId = "conv-605";
        fixture.ConversationHistory.Conversations.Add(new ProviderConversationRow(conversationId, CreatedAt));
        var mids = Enumerable.Range(0, 20).Select(i => "mid-history-" + i).ToList();
        fixture.ConversationHistory.Messages[conversationId] = mids
            .Select((m, i) => new ProviderConversationMessageRow(m, CreatedAt.AddMinutes(-i), false))
            .ToList();
        foreach (var (m, i) in mids.Select((m, i) => (m, i)))
        {
            fixture.ConversationHistory.Details[m] = new ProviderMessageDetailRow(
                m, CreatedAt.AddMinutes(-i), "526-recipient-605", [seeded.ProviderId], "what is the price?", false, null);
        }


        await RunHistorySyncAsync(seeded.AccountId);

        // Conversations rows exist…
        var imported = await CountMessagesAsync(seeded.AccountId, "526-recipient-605");
        Assert.Equal(20, imported);

        // …but ZERO automation runs and ZERO outbound provider calls.
        Assert.Equal(0, await CountRunsAsync(seeded.AutomationId));
        Assert.DoesNotContain(fixture.Messaging.TypedSends, s => s.RecipientId == "526-recipient-605");
        Assert.DoesNotContain(fixture.PrivateReplies.Sends, s => s.CommentId.StartsWith("mid-history", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HistoryRepeatImportIsIdempotentWithoutUnreadInflation()
    {
        var seeded = await SeedDmAutomationAsync("606");
        fixture.ConversationHistory.Reset();
        var conversationId = "conv-606";
        fixture.ConversationHistory.Conversations.Add(new ProviderConversationRow(conversationId, CreatedAt));
        fixture.ConversationHistory.Messages[conversationId] = [new ProviderConversationMessageRow("mid-606", CreatedAt, false)];
        fixture.ConversationHistory.Details["mid-606"] = new ProviderMessageDetailRow(
            "mid-606", CreatedAt, "526-recipient-606", [seeded.ProviderId], "hello", false, null);

        await RunHistorySyncAsync(seeded.AccountId);
        await RunHistorySyncAsync(seeded.AccountId);

        Assert.Equal(1, await CountMessagesAsync(seeded.AccountId, "526-recipient-606"));
        var unread = await UnreadForAsync(seeded.AccountId, "526-recipient-606");
        Assert.Equal(0, unread);
        Assert.Equal(0, await CountRunsAsync(seeded.AutomationId));
    }

    [Fact]
    public async Task WebhookFirstThenHistoryImportKeepsOneRowAndOneUnread()
    {
        var seeded = await SeedAccountOnlyAsync("607");

        // Webhook projects MID X (real-time: unread 1).
        var messageBody = Encoding.UTF8.GetBytes(
            "{\"object\":\"instagram\",\"entry\":[{\"id\":\"" + seeded.ProviderId + "\",\"time\":" + NowMs() + ",\"messaging\":[{" +
            "\"sender\":{\"id\":\"526-recipient-607\"},\"recipient\":{\"id\":\"" + seeded.ProviderId + "\"},\"timestamp\":" + NowMs() + "," +
            "\"message\":{\"mid\":\"mid-607\",\"text\":\"hello\"}}]}]}");
        var response = await fixture.Client.PostAsync(Endpoint, new ByteArrayContent(messageBody)
        {
            Headers = { { "X-Hub-Signature-256", Signed(messageBody) } },
        });
        Assert.True(response.IsSuccessStatusCode);

        // History later imports the same MID: no duplicate, no unread change.
        fixture.ConversationHistory.Reset();
        var conversationId = "conv-607";
        fixture.ConversationHistory.Conversations.Add(new ProviderConversationRow(conversationId, CreatedAt));
        fixture.ConversationHistory.Messages[conversationId] = [new ProviderConversationMessageRow("mid-607", CreatedAt, false)];
        fixture.ConversationHistory.Details["mid-607"] = new ProviderMessageDetailRow(
            "mid-607", CreatedAt, "526-recipient-607", [seeded.ProviderId], "hello", false, null);
        await RunHistorySyncAsync(seeded.AccountId);

        Assert.Equal(1, await CountMessagesAsync(seeded.AccountId, "526-recipient-607"));
        Assert.Equal(1, await UnreadForAsync(seeded.AccountId, "526-recipient-607"));
    }

    [Fact]
    public async Task HistoryFirstThenWebhookAccountsUnreadExactlyOnce()
    {
        var seeded = await SeedAccountOnlyAsync("608");

        fixture.ConversationHistory.Reset();
        var conversationId = "conv-608";
        fixture.ConversationHistory.Conversations.Add(new ProviderConversationRow(conversationId, CreatedAt));
        fixture.ConversationHistory.Messages[conversationId] = [new ProviderConversationMessageRow("mid-608", CreatedAt, false)];
        fixture.ConversationHistory.Details["mid-608"] = new ProviderMessageDetailRow(
            "mid-608", CreatedAt, "526-recipient-608", [seeded.ProviderId], "hello", false, null);
        await RunHistorySyncAsync(seeded.AccountId);
        Assert.Equal(0, await UnreadForAsync(seeded.AccountId, "526-recipient-608"));

        var messageBody = Encoding.UTF8.GetBytes(
            "{\"object\":\"instagram\",\"entry\":[{\"id\":\"" + seeded.ProviderId + "\",\"time\":" + NowMs() + ",\"messaging\":[{" +
            "\"sender\":{\"id\":\"526-recipient-608\"},\"recipient\":{\"id\":\"" + seeded.ProviderId + "\"},\"timestamp\":" + NowMs() + "," +
            "\"message\":{\"mid\":\"mid-608\",\"text\":\"hello\"}}]}]}");
        var response = await fixture.Client.PostAsync(Endpoint, new ByteArrayContent(messageBody)
        {
            Headers = { { "X-Hub-Signature-256", Signed(messageBody) } },
        });
        Assert.True(response.IsSuccessStatusCode);

        Assert.Equal(1, await CountMessagesAsync(seeded.AccountId, "526-recipient-608"));
        Assert.Equal(1, await UnreadForAsync(seeded.AccountId, "526-recipient-608"));

        // Redelivery does not double-unread.
        var again = await fixture.Client.PostAsync(Endpoint, new ByteArrayContent(messageBody)
        {
            Headers = { { "X-Hub-Signature-256", Signed(messageBody) } },
        });
        Assert.True(again.IsSuccessStatusCode);
        Assert.Equal(1, await UnreadForAsync(seeded.AccountId, "526-recipient-608"));
    }

    [Fact]
    public async Task SameProviderMidOnTwoExactAccountsCoexists()
    {
        var seededA = await SeedAccountOnlyAsync("609");
        var seededB = await SeedAccountOnlyAsync("610");

        // The provider message-id uniqueness is scoped per (conversation, account):
        // the SAME provider mid string imported for two different exact accounts must
        // coexist (a global unique index would throw on the second import).
        fixture.ConversationHistory.Reset();
        fixture.ConversationHistory.Conversations.Add(new ProviderConversationRow("conv-609", CreatedAt));
        fixture.ConversationHistory.Messages["conv-609"] = [new ProviderConversationMessageRow("same-mid", CreatedAt, false)];
        fixture.ConversationHistory.Details["same-mid"] = new ProviderMessageDetailRow(
            "same-mid", CreatedAt, "526-recipient-609", [seededA.ProviderId], "hello", false, null);
        await RunHistorySyncAsync(seededA.AccountId);

        fixture.ConversationHistory.Reset();
        fixture.ConversationHistory.Conversations.Add(new ProviderConversationRow("conv-610", CreatedAt));
        fixture.ConversationHistory.Messages["conv-610"] = [new ProviderConversationMessageRow("same-mid", CreatedAt, false)];
        fixture.ConversationHistory.Details["same-mid"] = new ProviderMessageDetailRow(
            "same-mid", CreatedAt, "526-recipient-610", [seededB.ProviderId], "hello", false, null);
        await RunHistorySyncAsync(seededB.AccountId);

        Assert.Equal(1, await CountMessagesAsync(seededA.AccountId, "526-recipient-609"));
        Assert.Equal(1, await CountMessagesAsync(seededB.AccountId, "526-recipient-610"));
    }

    [Fact]
    public async Task ManualHistorySyncEndpointAuthorizesThenEnqueuesOnly()
    {
        var seeded = await SeedAccountOnlyAsync("611");
        fixture.ConversationHistory.Reset();
        var token = await TokenAsync("sync-" + Guid.NewGuid().ToString("N") + "@test.dev", seeded.WorkspaceId);
        var client = AuthedClient(token);

        // No auth → 401 (before any ownership/token read).
        var unauthenticated = await fixture.Client.PostAsync(
            $"/api/v1/workspaces/{seeded.WorkspaceId}/instagram/connections/{seeded.AccountId}/history-sync", null);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        // Authenticated member: enqueues only — 202 + operation identity, zero provider calls.
        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{seeded.WorkspaceId}/instagram/connections/{seeded.AccountId}/history-sync", new { });
        Assert.Equal(System.Net.HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(Guid.Empty, body.GetProperty("operationId").GetGuid());
        Assert.Equal(0, fixture.ConversationHistory.CallCount);

        // Duplicate manual request coalesces onto the same active operation.
        var second = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{seeded.WorkspaceId}/instagram/connections/{seeded.AccountId}/history-sync", new { });
        Assert.Equal(System.Net.HttpStatusCode.Accepted, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(body.GetProperty("operationId").GetGuid(), secondBody.GetProperty("operationId").GetGuid());
        Assert.True(secondBody.GetProperty("alreadyActive").GetBoolean());
        Assert.Equal(0, fixture.ConversationHistory.CallCount);

        // Foreign account → 404, zero provider calls.
        var foreign = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{seeded.WorkspaceId}/instagram/connections/{Guid.CreateVersion7()}/history-sync", new { });
        Assert.Equal(System.Net.HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(0, fixture.ConversationHistory.CallCount);
    }

    [Fact]
    public async Task ForeignWorkspaceHistorySyncGetAndPostAre404BeforeTokenOrProviderAccess()
    {
        var home = await SeedAccountOnlyAsync("612");
        var foreign = await SeedAccountOnlyAsync("613");
        var token = await TokenAsync("sync-foreign-" + Guid.NewGuid().ToString("N") + "@test.dev", home.WorkspaceId);
        using var client = AuthedClient(token);

        fixture.ConversationHistory.Reset();
        fixture.Tokens.TokenGets.Clear();

        var post = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{home.WorkspaceId}/instagram/connections/{foreign.AccountId}/history-sync", new { });
        var get = await client.GetAsync(
            $"/api/v1/workspaces/{home.WorkspaceId}/instagram/connections/{foreign.AccountId}/history-sync");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, post.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, get.StatusCode);
        Assert.Empty(fixture.Tokens.TokenGets);
        Assert.Equal(0, fixture.ConversationHistory.CallCount);
    }

    [Fact]
    public async Task InitialSyncIsEnsuredAtConnectWithoutProviderCallsInOauthCallback()
    {
        fixture.ConversationHistory.Reset();
        var workspaceId = Guid.CreateVersion7();
        var token = await TokenAsync("sync-" + Guid.NewGuid().ToString("N") + "@test.dev", workspaceId);
        var client = AuthedClient(token);

        var connectBody = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/workspaces/{workspaceId}/instagram/authorize-url?redirectUri={Uri.EscapeDataString("https://app.test/callback")}");
        var state = connectBody.GetProperty("state").GetString()!;
        var response = await client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/instagram/connections", new
        {
            authorizationCode = "code-" + Guid.NewGuid().ToString("N"),
            redirectUri = "https://app.test/callback",
            state,
        });
        Assert.True(response.IsSuccessStatusCode);

        // The connect HTTP path performed ZERO history provider calls.
        Assert.Equal(0, fixture.ConversationHistory.CallCount);

        // A durable initial-sync operation + job exist for the new account.
        using var scope = fixture.Factory.Services.CreateScope();
        var instagram = scope.ServiceProvider.GetRequiredService<InstagramDbContext>();
        var account = await instagram.Accounts.SingleAsync(a => a.WorkspaceId == workspaceId);
        var operation = await instagram.ProviderSyncOperations
            .SingleAsync(o => o.ConnectedAccountId == account.Id && o.Kind == SyncOperationKind.Initial);
        Assert.Equal(SyncOperationStatus.Queued, operation.Status);
    }

    // ---------- helpers ----------

    private async Task RunSweepAsync(Guid accountId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var sweep = scope.ServiceProvider.GetRequiredService<ICommentSweep>();
        var outcome = await sweep.ExecuteAsync(accountId, default);
        Assert.Null(outcome.FailureCode);
    }

    private async Task RunHistorySyncAsync(Guid accountId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var ensure = scope.ServiceProvider.GetRequiredService<EnsureConversationSyncUseCase>();
        var sync = scope.ServiceProvider.GetRequiredService<ConversationHistorySyncUseCase>();
        var result = await ensure.ExecuteAsync(accountId, (await AccountAsync(accountId))!.WorkspaceId, SyncOperationKind.Manual, default);
        var stage = await sync.ExecuteAsync(result.OperationId, default);
        Assert.Equal(SyncOperationStatus.CompletedWithinProviderLimits, stage.Status);
    }

    private async Task<ConnectedAccount?> AccountAsync(Guid accountId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var instagram = scope.ServiceProvider.GetRequiredService<InstagramDbContext>();
        return await instagram.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == accountId);
    }

    private async Task<int> CountRunsAsync(Guid automationId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var automations = scope.ServiceProvider.GetRequiredService<AutomationsDbContext>();
        return await automations.AutomationRuns.CountAsync(r => r.AutomationId == automationId);
    }

    private async Task<AutomationRunRow?> SingleRunAsync(Guid automationId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var automations = scope.ServiceProvider.GetRequiredService<AutomationsDbContext>();
        return await automations.AutomationRuns.AsNoTracking().SingleOrDefaultAsync(r => r.AutomationId == automationId);
    }

    private async Task<int> CountMessagesAsync(Guid accountId, string participantId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var conversations = scope.ServiceProvider.GetRequiredService<ConversationsDbContext>();
        return await conversations.Conversations
            .Where(c => c.ChannelAccountId == ChannelAccountId.From(accountId) && c.ParticipantId == participantId)
            .SelectMany(c => c.Messages)
            .CountAsync();
    }

    private async Task<int> UnreadForAsync(Guid accountId, string participantId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var conversations = scope.ServiceProvider.GetRequiredService<ConversationsDbContext>();
        var thread = await conversations.Conversations
            .FirstOrDefaultAsync(c => c.ChannelAccountId == ChannelAccountId.From(accountId) && c.ParticipantId == participantId);
        return thread?.UnreadCount ?? 0;
    }

    private async Task<List<CommentEffectRow>> EffectsForAsync(Guid accountId, string commentId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var instagram = scope.ServiceProvider.GetRequiredService<InstagramDbContext>();
        return await instagram.CommentEffects.AsNoTracking()
            .Where(e => e.ConnectedAccountId == accountId && e.ProviderCommentId == commentId)
            .ToListAsync();
    }

    private async Task<CommentEffectRow?> SingleEffectAsync(Guid accountId, string commentId, InstagramEffectType effectType)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var instagram = scope.ServiceProvider.GetRequiredService<InstagramDbContext>();
        return await instagram.CommentEffects.AsNoTracking()
            .SingleOrDefaultAsync(e => e.ConnectedAccountId == accountId && e.ProviderCommentId == commentId && e.EffectType == effectType);
    }

    private sealed record LoginResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("accessToken")] string AccessToken);

    private async Task<string> TokenAsync(string email, Guid workspaceId)
    {
        await fixture.Client.PostAsJsonAsync("/api/v1/identity/register", new { email, password = "Passw0rd!23", displayName = "Recon Tester" });
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

    private sealed record Seeded(Guid AutomationId, Guid AccountId, Guid WorkspaceId, string ProviderId);

    private async Task<Seeded> SeedCommentAutomationAsync(string suffix, ActionKind actionKind)
    {
        var workspaceId = Guid.CreateVersion7();
        var providerId = "1784140000000" + suffix;
        using var scope = fixture.Factory.Services.CreateScope();
        var instagram = scope.ServiceProvider.GetRequiredService<InstagramDbContext>();
        var account = ConnectedAccount.Create(
            Guid.CreateVersion7(), workspaceId, providerId, ConnectionPath.InstagramLogin,
            ["instagram_business_basic", "instagram_business_manage_comments", "instagram_business_manage_messages"],
            DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow);
        await instagram.Accounts.AddAsync(account);
        await instagram.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IProtectedTokenStore>().StoreAsync(account.Id, "test-access-token-" + suffix, default);
        await instagram.SaveChangesAsync();

        var trigger = new AutomationTrigger(
            TriggerKind.CommentCreated, ["price"], TextMatch: TextMatchMode.Keywords,
            Source: SourceScope.SpecificSource, SourceMediaId: "media-" + suffix);
        var actions = new List<AutomationAction>
        {
            new(actionKind, "DM: recon hello!"),
        };
        var repository = scope.ServiceProvider.GetRequiredService<IAutomationRepository>();
        var automation = Automation.Create(
            Guid.CreateVersion7(), workspaceId, "recon " + suffix,
            AutomationDefinition.Create(trigger, actions), CreatedAt, ChannelAccountId.From(account.Id));
        automation.Activate(CreatedAt);
        await repository.SaveChangesAsync(automation);

        return new Seeded(automation.Id, account.Id, workspaceId, providerId);
    }

    private async Task<Seeded> SeedDmAutomationAsync(string suffix)
    {
        var workspaceId = Guid.CreateVersion7();
        var providerId = "1784140000000" + suffix;
        using var scope = fixture.Factory.Services.CreateScope();
        var instagram = scope.ServiceProvider.GetRequiredService<InstagramDbContext>();
        var account = ConnectedAccount.Create(
            Guid.CreateVersion7(), workspaceId, providerId, ConnectionPath.InstagramLogin,
            ["instagram_business_basic", "instagram_business_manage_comments", "instagram_business_manage_messages"],
            DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow);
        await instagram.Accounts.AddAsync(account);
        await instagram.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IProtectedTokenStore>().StoreAsync(account.Id, "test-access-token-" + suffix, default);
        await instagram.SaveChangesAsync();

        var repository = scope.ServiceProvider.GetRequiredService<IAutomationRepository>();
        var automation = Automation.Create(
            Guid.CreateVersion7(), workspaceId, "dm " + suffix,
            AutomationDefinition.Create(AutomationTrigger.InboundDirectMessage("price"),
                [new AutomationAction(ActionKind.DirectMessage, "DM: recon hello!")]), CreatedAt, ChannelAccountId.From(account.Id));
        automation.Activate(CreatedAt);
        await repository.SaveChangesAsync(automation);

        return new Seeded(automation.Id, account.Id, workspaceId, providerId);
    }

    private async Task<Seeded> SeedAccountOnlyAsync(string suffix)
    {
        var workspaceId = Guid.CreateVersion7();
        var providerId = "1784140000000" + suffix;
        using var scope = fixture.Factory.Services.CreateScope();
        var instagram = scope.ServiceProvider.GetRequiredService<InstagramDbContext>();
        var account = ConnectedAccount.Create(
            Guid.CreateVersion7(), workspaceId, providerId, ConnectionPath.InstagramLogin,
            ["instagram_business_basic", "instagram_business_manage_comments", "instagram_business_manage_messages"],
            DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow);
        await instagram.Accounts.AddAsync(account);
        await instagram.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IProtectedTokenStore>().StoreAsync(account.Id, "test-access-token-" + suffix, default);
        await instagram.SaveChangesAsync();
        return new Seeded(Guid.Empty, account.Id, workspaceId, providerId);
    }
}
