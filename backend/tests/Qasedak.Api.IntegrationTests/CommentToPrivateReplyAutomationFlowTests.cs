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
using Qasedak.Modules.Instagram.Application.Effects;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.Infrastructure.Effects;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

/// <summary>
/// The full M06-005 comment-automation flow corrected to Meta Private Reply semantics
/// (M13-009): a signed comment webhook flows through the composition-root bridge into the
/// Automations engine; comment-origin actions are dispatched to the comment-ID-addressed
/// Private Reply operation — never a normal recipient.id DM — with the global semantic
/// claim (real PostgreSQL) allowing exactly ONE provider mutation across two matching
/// automations, redeliveries, envelope changes and crash-after-success recovery. Each test
/// uses its own commenter/account identities so shared-database state cannot bleed.
/// </summary>
[Collection(ApiTestEnvironment.Name)]
public sealed class CommentToPrivateReplyAutomationFlowTests(ApiPostgreSqlFixture fixture)
{
    private const string Endpoint = "/api/v1/webhooks/instagram";

    private static readonly DateTimeOffset CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1771900500);

    private static string Signed(byte[] body) => "sha256=" + Convert.ToHexString(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(ApiPostgreSqlFixture.MetaAppSecret), body)).ToLowerInvariant();

    private static byte[] Body(string eventId, string accountProviderId, string commenterId, string text = "what is the price?", bool includeFrom = true, string mediaProductType = "FEED", string? commentId = null, long timeMs = 1502905976963) => Encoding.UTF8.GetBytes(
        "{\"object\":\"instagram\",\"entry\":[{\"id\":\"" + accountProviderId + "\",\"time\":" + timeMs + ",\"changes\":[{" +
        "\"field\":\"comments\",\"value\":{\"id\":\"" + (commentId ?? "comment-" + eventId) + "\"," +
        (includeFrom ? "\"from\":{\"id\":\"" + commenterId + "\"}," : string.Empty) +
        "\"text\":\"" + text + "\",\"media\":{\"id\":\"media-" + eventId + "\",\"media_product_type\":\"" + mediaProductType + "\"}}}]}]}");

    [Fact]
    public async Task MatchingCommentSendsExactlyOnePrivateReplyAndRedeliveryDoesNotRepeat()
    {
        var seeded = await SeedAsync("101", activeAutomation: true);
        var body = Body("e2e-1", seeded.ProviderId, "commenter-1001");
        Assert.True((await PostSignedAsync(body)).IsSuccessStatusCode);

        var send = Assert.Single(PrivateRepliesTo("comment-e2e-1"));
        Assert.Equal(seeded.ProviderId, send.ProviderAccountId);
        Assert.Equal("test-access-token-101", send.AccessToken);
        Assert.Equal("DM: thanks for asking about price!", send.Text);
        // Comment-origin actions must NEVER become a normal recipient.id DM.
        Assert.DoesNotContain(fixture.Messaging.Sends, s => s.RecipientId == "commenter-1001");

        // Redelivery of the identical payload: accepted, but neither the inbox nor the
        // semantic claim permits a second Private Reply.
        Assert.True((await PostSignedAsync(body)).IsSuccessStatusCode);
        Assert.Single(PrivateRepliesTo("comment-e2e-1"));

        // The ledger shows one completed run pinned to version 1, and the global effect
        // claim records the provider success identity.
        var run = await SingleRunAsync(seeded.AutomationId, includeActions: true);
        Assert.NotNull(run);
        Assert.Equal(AutomationRunStatus.Completed, run!.Status);
        Assert.Equal(1, run.AutomationVersionNumber);

        var effect = await SingleEffectAsync(seeded.AccountId, "comment-e2e-1");
        Assert.NotNull(effect);
        Assert.Equal(InstagramEffectStatus.Succeeded, effect!.Status);
        Assert.Equal("526-recipient-comment-e2e-1", effect.ProviderRecipientId);
        Assert.Equal("mid-comment-e2e-1", effect.ProviderMessageId);
    }

    [Fact]
    public async Task TwoMatchingAutomationsProduceExactlyOnePrivateReply()
    {
        var seeded = await SeedAsync("102", activeAutomation: true, extraActiveAutomations: 1);
        var body = Body("e2e-2", seeded.ProviderId, "commenter-1002");

        Assert.True((await PostSignedAsync(body)).IsSuccessStatusCode);

        // Exactly ONE Meta Private Reply HTTP call — the global semantic claim is the
        // one-winner arbiter; the loser made zero provider mutations.
        var send = Assert.Single(PrivateRepliesTo("comment-e2e-2"));
        Assert.Equal("comment-e2e-2", send.CommentId);
        Assert.DoesNotContain(fixture.Messaging.Sends, s => s.RecipientId == "commenter-1002");

        var runA = await SingleRunAsync(seeded.AutomationId, includeActions: true);
        var runB = await SingleRunAsync(seeded.ExtraAutomationIds[0], includeActions: true);
        Assert.NotNull(runA);
        Assert.NotNull(runB);

        var delivered = new[] { runA!, runB! }.Single(r => r.Status == AutomationRunStatus.Completed);
        var suppressed = new[] { runA!, runB! }.Single(r => r.Status == AutomationRunStatus.Finished);
        Assert.Equal(AutomationActionStatus.Succeeded, Assert.Single(delivered.Actions).Status);
        // Truthful suppression: this automation did NOT deliver; it records the conflict.
        var suppressedSlot = Assert.Single(suppressed.Actions);
        Assert.Equal(AutomationActionStatus.Suppressed, suppressedSlot.Status);
        Assert.Equal("privateReply.alreadyClaimed", suppressedSlot.FailureCode);
    }

    [Fact]
    public async Task SemanticallySameCommentUnderNewEnvelopeNeverSendsTwice()
    {
        var seeded = await SeedAsync("103", activeAutomation: true);
        Assert.True((await PostSignedAsync(Body("e2e-3a", seeded.ProviderId, "commenter-1003"))).IsSuccessStatusCode);
        Assert.Single(PrivateRepliesTo("comment-e2e-3a"));

        // The SAME CommentId arriving under a different event identity (new inbox body):
        // the semantic claim — not the inbox — is the final backstop.
        Assert.True((await PostSignedAsync(Body("e2e-3b", seeded.ProviderId, "commenter-1003", commentId: "comment-e2e-3a"))).IsSuccessStatusCode);

        Assert.Single(PrivateRepliesTo("comment-e2e-3a"));
    }

    [Fact]
    public async Task CrashAfterProviderSuccessConvergesWithoutSecondSend()
    {
        var seeded = await SeedAsync("104", activeAutomation: true);
        var body = Body("e2e-4", seeded.ProviderId, "commenter-1004");
        Assert.True((await PostSignedAsync(body)).IsSuccessStatusCode);
        Assert.Single(PrivateRepliesTo("comment-e2e-4"));

        // Simulate the crash window: provider success was persisted in the effect ledger,
        // but the AutomationRun row is lost before it was saved.
        var scope = fixture.Factory.Services.CreateScope();
        var automations = scope.ServiceProvider.GetRequiredService<AutomationsDbContext>();
        var runRow = await automations.AutomationRuns.Include(r => r.Actions).SingleAsync(r => r.AutomationId == seeded.AutomationId);
        automations.AutomationRunActions.RemoveRange(runRow.Actions);
        await automations.SaveChangesAsync();
        automations.AutomationRuns.Remove(runRow);
        await automations.SaveChangesAsync();
        scope.Dispose();

        // And the inbox entry is re-queued for processing (as a queue replay would).
        var instagramScope = fixture.Factory.Services.CreateScope();
        var instagram = instagramScope.ServiceProvider.GetRequiredService<InstagramDbContext>();
        var eventId = EventIdOf(body);
        _ = await instagram.WebhookInbox.SingleAsync(e => e.EventId == eventId);
        await instagram.Database.ExecuteSqlRawAsync(
            "UPDATE instagram.webhook_inbox SET \"Status\" = 'pending' WHERE \"EventId\" = {0}", eventId);
        instagramScope.Dispose();

        // Any new webhook POST triggers pending reprocessing (the real queue path); the
        // trigger event itself is non-matching so it cannot create its own run/send.
        Assert.True((await PostSignedAsync(Body("e2e-4b", seeded.ProviderId, "commenter-1099", text: "just saying hi"))).IsSuccessStatusCode);

        // Recovery: ZERO second Meta call — the replay reuses the stored provider
        // success identity and the fresh run converges to Completed.
        var send = Assert.Single(PrivateRepliesTo("comment-e2e-4"));
        Assert.Equal("comment-e2e-4", send.CommentId);
        var recovered = await SingleRunAsync(seeded.AutomationId, includeActions: true);
        Assert.NotNull(recovered);
        Assert.Equal(AutomationRunStatus.Completed, recovered!.Status);
        Assert.Equal(AutomationActionStatus.Succeeded, Assert.Single(recovered.Actions).Status);
    }

    [Fact]
    public async Task LiveCommentIsAttemptedExactlyOnce()
    {
        var seeded = await SeedAsync("105", activeAutomation: true);
        // Live policy: the 7-day rule never applies; a fresh notification timestamp is
        // used so the notification guard does not reject it (2017 fixtures would).
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var body = Body("e2e-5", seeded.ProviderId, "commenter-1005", mediaProductType: "LIVE", timeMs: nowMs);

        Assert.True((await PostSignedAsync(body)).IsSuccessStatusCode);
        Assert.Single(PrivateRepliesTo("comment-e2e-5"));

        // Redelivery: still exactly one — Live never retries after the attempt.
        Assert.True((await PostSignedAsync(body)).IsSuccessStatusCode);
        Assert.Single(PrivateRepliesTo("comment-e2e-5"));
    }

    [Fact]
    public async Task FromIdNullCommentIsStillAddressedByCommentId()
    {
        var seeded = await SeedAsync("106", activeAutomation: true);
        // Official payload without value.from: Private Reply addressing is comment-ID
        // based; a null FromId must NOT block the reply.
        var body = Body("e2e-6", seeded.ProviderId, "commenter-1006", includeFrom: false);

        Assert.True((await PostSignedAsync(body)).IsSuccessStatusCode);

        var send = Assert.Single(PrivateRepliesTo("comment-e2e-6"));
        Assert.Equal("comment-e2e-6", send.CommentId);
        Assert.DoesNotContain(fixture.Messaging.Sends, s => s.RecipientId == "commenter-1006");
    }

    [Fact]
    public async Task DefinitelyExpiredCommentIsRejectedBeforeAnyProviderCall()
    {
        var seeded = await SeedAsync("107", activeAutomation: true);
        fixture.CommentReferences.Script = () => new CommentReferenceReadResult.Found(DateTimeOffset.UtcNow.AddDays(-10));
        try
        {
            Assert.True((await PostSignedAsync(Body("e2e-7", seeded.ProviderId, "commenter-1007"))).IsSuccessStatusCode);

            Assert.Empty(PrivateRepliesTo("comment-e2e-7"));
            Assert.Null(await SingleEffectAsync(seeded.AccountId, "comment-e2e-7"));
            var run = await SingleRunAsync(seeded.AutomationId, includeActions: true);
            Assert.NotNull(run);
            Assert.Equal(AutomationRunStatus.Finished, run!.Status);
            Assert.Equal("privateReply.policyRejected.DefinitelyExpired", Assert.Single(run.Actions).FailureCode);
        }
        finally
        {
            fixture.CommentReferences.Script = () => new CommentReferenceReadResult.Found(DateTimeOffset.UtcNow.AddDays(-1));
        }
    }

    [Fact]
    public async Task CommentAutomationNeverTouchesTheDmWindowPath()
    {
        var seeded = await SeedAsync("108", activeAutomation: true);
        // Even when the commenter would be outside the DM window, the corrected flow must
        // not attempt a normal recipient.id send at all.
        fixture.Messaging.RejectRecipientsOutsideWindow.Add("commenter-1008");
        try
        {
            Assert.True((await PostSignedAsync(Body("e2e-8", seeded.ProviderId, "commenter-1008"))).IsSuccessStatusCode);

            Assert.DoesNotContain(fixture.Messaging.Sends, s => s.RecipientId == "commenter-1008");
            Assert.Single(PrivateRepliesTo("comment-e2e-8"));
        }
        finally
        {
            fixture.Messaging.RejectRecipientsOutsideWindow.Clear();
        }
    }

    [Fact]
    public async Task WrongAccountAutomationMakesZeroCallsAndZeroClaims()
    {
        // The automation is bound to account B; the comment event belongs to account A.
        var seededB = await SeedAsync("109", activeAutomation: true);
        var commentProvider = "1784140000000OTHER";
        var body = Body("e2e-9", commentProvider, "commenter-1009");

        Assert.True((await PostSignedAsync(body)).IsSuccessStatusCode);

        Assert.Empty(PrivateRepliesTo("comment-e2e-9"));
        Assert.Null(await SingleRunAsync(seededB.AutomationId, includeActions: false));
    }

    [Fact]
    public async Task NonMatchingCommentLeavesNoSendAndNoRun()
    {
        var seeded = await SeedAsync("110", activeAutomation: true);
        var body = Body("e2e-10", seeded.ProviderId, "commenter-1010", text: "just saying hi");
        Assert.True((await PostSignedAsync(body)).IsSuccessStatusCode);

        Assert.Empty(PrivateRepliesTo("comment-e2e-10"));
        Assert.Null(await SingleRunAsync(seeded.AutomationId, includeActions: false));
    }

    [Fact]
    public async Task DisabledAutomationsNeverDispatch()
    {
        var seeded = await SeedAsync("111", activeAutomation: false);
        var body = Body("e2e-11", seeded.ProviderId, "commenter-1011");

        Assert.True((await PostSignedAsync(body)).IsSuccessStatusCode);
        Assert.Empty(PrivateRepliesTo("comment-e2e-11"));
        Assert.Null(await SingleRunAsync(seeded.AutomationId, includeActions: false));
    }

    private List<(string AccessToken, string ProviderAccountId, string CommentId, string Text)> PrivateRepliesTo(string commentId) =>
        fixture.PrivateReplies.Sends.Where(s => s.CommentId == commentId).ToList();

    private async Task<HttpResponseMessage> PostSignedAsync(byte[] body)
    {
        using var content = new ByteArrayContent(body);
        content.Headers.Add("X-Hub-Signature-256", Signed(body));
        return await fixture.Client.PostAsync(Endpoint, content);
    }

    private static string EventIdOf(byte[] body)
    {
        var canonical = JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(body)).GetRawText();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private async Task<AutomationRunRow?> SingleRunAsync(Guid automationId, bool includeActions)
    {
        var scope = fixture.Factory.Services.CreateScope();
        try
        {
            await using var automations = scope.ServiceProvider.GetRequiredService<AutomationsDbContext>();
            var query = automations.AutomationRuns.AsQueryable();
            if (includeActions)
            {
                query = query.Include(r => r.Actions);
            }

            return await query.SingleOrDefaultAsync(r => r.AutomationId == automationId);
        }
        finally
        {
            scope.Dispose();
        }
    }

    private async Task<CommentEffectRow?> SingleEffectAsync(Guid accountId, string commentId)
    {
        var scope = fixture.Factory.Services.CreateScope();
        try
        {
            await using var instagram = scope.ServiceProvider.GetRequiredService<InstagramDbContext>();
            return await instagram.CommentEffects.AsNoTracking()
                .SingleOrDefaultAsync(e => e.ConnectedAccountId == accountId && e.ProviderCommentId == commentId
                    && e.EffectType == InstagramEffectType.PrivateReply);
        }
        finally
        {
            scope.Dispose();
        }
    }

    private sealed record SeededFlow(Guid AutomationId, Guid AccountId, string ProviderId, IReadOnlyList<Guid> ExtraAutomationIds);

    /// <summary>Each test gets its own bound account (with a stored access token), workspace
    /// and automation(s) so the shared database cannot bleed state between scenarios.</summary>
    private async Task<SeededFlow> SeedAsync(string suffix, bool activeAutomation, int extraActiveAutomations = 0)
    {
        var providerId = "1784140000000" + suffix;
        var workspaceId = Guid.CreateVersion7();
        var scope = fixture.Factory.Services.CreateScope();

        var instagram = scope.ServiceProvider.GetRequiredService<InstagramDbContext>();
        var account = ConnectedAccount.Create(
            Guid.CreateVersion7(),
            workspaceId,
            providerId,
            ConnectionPath.InstagramLogin,
            ["instagram_business_basic", "instagram_business_manage_comments", "instagram_business_manage_messages"],
            DateTimeOffset.UtcNow.AddDays(30),
            DateTimeOffset.UtcNow);
        await instagram.Accounts.AddAsync(account);
        await instagram.SaveChangesAsync();
        var accountId = account.Id;

        var tokens = scope.ServiceProvider.GetRequiredService<Qasedak.Modules.Instagram.Application.Accounts.IProtectedTokenStore>();
        await tokens.StoreAsync(accountId, "test-access-token-" + suffix);
        await instagram.SaveChangesAsync();

        var repository = scope.ServiceProvider.GetRequiredService<IAutomationRepository>();
        var definition = AutomationDefinition.Create(
            AutomationTrigger.CommentCreated("price"),
            [],
            [new AutomationAction(ActionKind.SendDirectMessage, "DM: thanks for asking about price!")]);
        var automation = Automation.Create(
            Guid.CreateVersion7(), workspaceId, "comment welcome " + suffix, definition, CreatedAt,
            ChannelAccountId.From(accountId));
        if (activeAutomation)
        {
            automation.Activate(CreatedAt);
        }

        await repository.SaveChangesAsync(automation);

        var extraIds = new List<Guid>();
        for (var i = 0; i < extraActiveAutomations; i++)
        {
            var extra = Automation.Create(
                Guid.CreateVersion7(), workspaceId, "comment welcome " + suffix + "-extra-" + i, definition, CreatedAt,
                ChannelAccountId.From(accountId));
            if (activeAutomation)
            {
                extra.Activate(CreatedAt);
            }

            await repository.SaveChangesAsync(extra);
            extraIds.Add(extra.Id);
        }

        scope.Dispose();
        return new SeededFlow(automation.Id, accountId, providerId, extraIds);
    }
}
