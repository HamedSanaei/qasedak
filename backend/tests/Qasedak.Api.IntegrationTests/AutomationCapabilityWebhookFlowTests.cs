using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qasedak.BuildingBlocks.Application.Scheduling;
using Qasedak.BuildingBlocks.Domain;
using Qasedak.BuildingBlocks.Infrastructure.Scheduling;
using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;
using Qasedak.Modules.Automations.Infrastructure.Persistence;
using Qasedak.Modules.Instagram.Application.Effects;
using Qasedak.Modules.Instagram.Application.Messaging;
using Qasedak.Modules.Instagram.Application.RevealFlow;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.Infrastructure.Effects;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

/// <summary>
/// M13-012 automation capabilities through the REAL signed-webhook pipeline (real
/// PostgreSQL, normalizer, enrichment, fan-out): inbound-DM triggers with semantic
/// message-id idempotency, legacy origin-aware routing, post/source scope, public replies,
/// reveal-flow mapping and durable delayed follow-ups through platform scheduled work —
/// all with the run ledger as the external-attempt authority.
/// </summary>
[Collection(ApiTestEnvironment.Name)]
public sealed class AutomationCapabilityWebhookFlowTests(ApiPostgreSqlFixture fixture)
{
    private const string Endpoint = "/api/v1/webhooks/instagram";

    private static readonly DateTimeOffset CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1771900500);

    private static string Signed(byte[] body) => "sha256=" + Convert.ToHexString(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(ApiPostgreSqlFixture.MetaAppSecret), body)).ToLowerInvariant();

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static byte[] MessageBody(string accountProviderId, string senderId, string mid, string text, long? timestampMs = null) => Encoding.UTF8.GetBytes(
        "{\"object\":\"instagram\",\"entry\":[{\"id\":\"" + accountProviderId + "\",\"time\":" + NowMs() + ",\"messaging\":[{" +
        "\"sender\":{\"id\":\"" + senderId + "\"},\"recipient\":{\"id\":\"" + accountProviderId + "\"},\"timestamp\":" + (timestampMs ?? NowMs()) + "," +
        "\"message\":{\"mid\":\"" + mid + "\",\"text\":\"" + text + "\"}}]}]}");

    private static byte[] CommentBody(string eventId, string accountProviderId, string commentId, string text = "what is the price?", string? mediaId = null, string? originalMediaId = null, string? fromId = null) => Encoding.UTF8.GetBytes(
        "{\"object\":\"instagram\",\"entry\":[{\"id\":\"" + accountProviderId + "\",\"time\":" + NowMs() + ",\"changes\":[{" +
        "\"field\":\"comments\",\"value\":{\"id\":\"" + commentId + "\",\"text\":\"" + text + "\"," +
        (fromId is null ? string.Empty : "\"from\":{\"id\":\"" + fromId + "\"},") +
        "\"media\":{\"id\":\"" + (mediaId ?? "media-" + eventId) + "\"" +
        (originalMediaId is null ? string.Empty : ",\"original_media_id\":\"" + originalMediaId + "\"") +
        ",\"media_product_type\":\"FEED\"}}}]}]}");

    [Fact]
    public async Task InboundDmTriggerSendsDirectMessageOnceAndRedeliveryDoesNotRepeat()
    {
        var seeded = await SeedAsync("501", TriggerKind.InboundDirectMessage, ActionKind.DirectMessage);
        var participant = "526-recipient-501";
        var body = MessageBody(seeded.ProviderId, participant, "mid-inbound-501", "what is the price?");

        Assert.True((await PostSignedAsync(body)).IsSuccessStatusCode);

        // DM trigger ⇒ normal recipient.id Direct Message (PlainText) — never a Private Reply.
        var send = Assert.Single(fixture.Messaging.TypedSends, s => s.RecipientId == participant);
        Assert.IsType<InstagramMessageContent.PlainText>(send.Content);
        Assert.Equal("test-access-token-501", send.AccessToken);
        Assert.DoesNotContain(fixture.PrivateReplies.Sends, s => s.CommentId == "comment-x");

        // Redelivery of the identical payload: one run, one send.
        Assert.True((await PostSignedAsync(body)).IsSuccessStatusCode);
        Assert.Single(fixture.Messaging.TypedSends, s => s.RecipientId == participant);

        var run = await SingleRunAsync(seeded.AutomationId, includeActions: true);
        Assert.Equal(AutomationRunStatus.Completed, run!.Status);
        var slot = Assert.Single(run.Actions);
        Assert.Equal(AutomationActionStatus.Succeeded, slot.Status);
        Assert.Equal(participant, slot.ProviderRecipientId);
        Assert.Equal("mid-" + participant, slot.ProviderMessageId);
        Assert.NotNull(slot.AttemptedAtUtc);
    }

    [Fact]
    public async Task LegacySendDirectMessageOnDmTriggerRoutesToNormalDirect()
    {
        var seeded = await SeedAsync("502", TriggerKind.InboundDirectMessage, ActionKind.SendDirectMessage);
        var participant = "526-recipient-502";

        Assert.True((await PostSignedAsync(MessageBody(seeded.ProviderId, participant, "mid-inbound-502", "what is the price?"))).IsSuccessStatusCode);

        // Origin-aware legacy routing: DM origin ⇒ Direct Message; zero Private Reply traffic.
        Assert.Single(fixture.Messaging.TypedSends, s => s.RecipientId == participant);
        Assert.DoesNotContain(fixture.PrivateReplies.Sends, s => s.Text == "DM: hello!");
    }

    [Fact]
    public async Task DmWithoutProviderMessageIdFailsClosedWithZeroTraffic()
    {
        var seeded = await SeedAsync("503", TriggerKind.InboundDirectMessage, ActionKind.DirectMessage);
        var participant = "526-recipient-503";
        // No mid in the payload: the bridge must fail closed (no run, no send).
        var body = Encoding.UTF8.GetBytes(
            "{\"object\":\"instagram\",\"entry\":[{\"id\":\"" + seeded.ProviderId + "\",\"time\":" + NowMs() + ",\"messaging\":[{" +
            "\"sender\":{\"id\":\"" + participant + "\"},\"recipient\":{\"id\":\"" + seeded.ProviderId + "\"},\"timestamp\":" + NowMs() + "," +
            "\"message\":{\"text\":\"no mid here\"}}]}]}");

        Assert.True((await PostSignedAsync(body)).IsSuccessStatusCode);

        Assert.DoesNotContain(fixture.Messaging.TypedSends, s => s.RecipientId == participant);
        Assert.Null(await SingleRunAsync(seeded.AutomationId, includeActions: false));
    }

    [Fact]
    public async Task SpecificSourceMatchesConfiguredMediaOrOriginalMediaOnly()
    {
        var seeded = await SeedAsync("504", TriggerKind.CommentCreated, ActionKind.SendDirectMessage,
            sourceMediaId: "17841400000000000_post_504");

        // Comment on a different media: zero traffic.
        Assert.True((await PostSignedAsync(CommentBody("e2e-504a", seeded.ProviderId, "comment-504a", mediaId: "17841400000000000_other"))).IsSuccessStatusCode);
        Assert.DoesNotContain(fixture.PrivateReplies.Sends, s => s.CommentId == "comment-504a");

        // Comment on the configured media (direct media id): replies.
        Assert.True((await PostSignedAsync(CommentBody("e2e-504b", seeded.ProviderId, "comment-504b", mediaId: "17841400000000000_post_504"))).IsSuccessStatusCode);
        Assert.Single(fixture.PrivateReplies.Sends, s => s.CommentId == "comment-504b");

        // Comment on an ad media whose ORIGINAL media id matches: replies (never merged).
        Assert.True((await PostSignedAsync(CommentBody("e2e-504c", seeded.ProviderId, "comment-504c",
            mediaId: "17841400000000000_ad_504", originalMediaId: "17841400000000000_post_504"))).IsSuccessStatusCode);
        Assert.Single(fixture.PrivateReplies.Sends, s => s.CommentId == "comment-504c");

        Assert.DoesNotContain(fixture.PrivateReplies.Sends, s => s.CommentId == "comment-504a");
    }

    [Fact]
    public async Task PublicAndPrivateEffectsCoexistExactlyOnceEach()
    {
        var seeded = await SeedAsync("505", TriggerKind.CommentCreated, ActionKind.SendDirectMessage, extraPublicReply: true);
        var body = CommentBody("e2e-505", seeded.ProviderId, "comment-505");

        Assert.True((await PostSignedAsync(body)).IsSuccessStatusCode);

        // Independent effect identities: ONE Private Reply + ONE public comment reply.
        var privateSend = Assert.Single(fixture.PrivateReplies.Sends, s => s.CommentId == "comment-505");
        var publicSend = Assert.Single(fixture.PublicReplies.Sends, s => s.CommentId == "comment-505");
        Assert.Equal("comment-505", publicSend.CommentId);
        Assert.Equal("Public: see our guide!", publicSend.Text);

        // Redelivery: still exactly one of each.
        Assert.True((await PostSignedAsync(body)).IsSuccessStatusCode);
        Assert.Single(fixture.PrivateReplies.Sends, s => s.CommentId == "comment-505");
        Assert.Single(fixture.PublicReplies.Sends, s => s.CommentId == "comment-505");

        var run = await SingleRunAsync(seeded.AutomationId, includeActions: true);
        Assert.Equal(AutomationRunStatus.Completed, run!.Status);
        Assert.Equal(
            [AutomationActionStatus.Succeeded, AutomationActionStatus.Succeeded],
            run.Actions.OrderBy(a => a.ActionIndex).Select(a => a.Status));

        var effect = await SingleEffectAsync(seeded.AccountId, "comment-505", InstagramEffectType.PublicCommentReply);
        Assert.Equal(InstagramEffectStatus.Succeeded, effect!.Status);
        Assert.Equal("reply-comment-505", effect.ProviderMessageId);
    }

    [Fact]
    public async Task RevealFlowActionStartsFlowWithAutomationOwnedContent()
    {
        var seeded = await SeedAsync("506", TriggerKind.CommentCreated, ActionKind.StartRevealFlow, revealAction: true);
        fixture.RevealFlowStarts.Clear();
        try
        {
            Assert.True((await PostSignedAsync(CommentBody("e2e-506", seeded.ProviderId, "comment-506"))).IsSuccessStatusCode);

            // The opening Private Reply comes from the automation's own content.
            var opening = Assert.Single(fixture.PrivateReplies.Sends, s => s.CommentId == "comment-506");
            Assert.Equal("Opening from automation", opening.Text);

            // The durable flow row carries the automation owner metadata (M13-012 §37-38).
            var flow = await SingleFlowAsync(seeded.AccountId, "comment-506");
            Assert.NotNull(flow);
            Assert.Equal(seeded.AutomationId, flow!.AutomationId);
            Assert.Equal(1, flow.AutomationVersionNumber);
            // The owner metadata keys on the run-ledger semantic identity (the comment id).
            Assert.Equal("comment-506", flow.TriggerEventId);
            Assert.Equal(0, flow.ActionIndex);
            Assert.Equal("Gate from automation", flow.GatePromptText);
            Assert.Equal("Reveal from automation", flow.RevealText);
            Assert.Equal(RevealFlowState.AwaitingUserResponse, flow.State);

            // The run slot truthfully records a started continuation, not a delivery.
            var run = await SingleRunAsync(seeded.AutomationId, includeActions: true);
            Assert.Equal(AutomationRunStatus.Running, run!.Status);
            Assert.Equal(AutomationActionStatus.ContinuationStarted, Assert.Single(run.Actions).Status);
        }
        finally
        {
            fixture.RevealFlowStarts.Clear();
        }
    }

    [Fact]
    public async Task FollowUpSchedulesIdentifiersOnlyAndDueTimeSendsExactlyOnce()
    {
        var seeded = await SeedAsync("507", TriggerKind.InboundDirectMessage, ActionKind.ScheduleFollowUp, followUpMinutes: 30);
        var participant = "526-recipient-507";

        Assert.True((await PostSignedAsync(MessageBody(seeded.ProviderId, participant, "mid-inbound-507", "what is the price?"))).IsSuccessStatusCode);

        // The scheduling dispatch produced a Scheduled slot and ZERO provider sends.
        Assert.DoesNotContain(fixture.Messaging.TypedSends, s => s.RecipientId == participant);
        var run = await SingleRunAsync(seeded.AutomationId, includeActions: true);
        Assert.Equal(AutomationRunStatus.Running, run!.Status);
        var slot = Assert.Single(run.Actions);
        Assert.Equal(AutomationActionStatus.Scheduled, slot.Status);
        Assert.Equal(participant, slot.ProviderRecipientId);

        // The job exists with an identifier-only payload — never text/token material.
        var job = await SingleFollowUpJobAsync(run.Id, 0);
        Assert.NotNull(job);
        var payload = JsonSerializer.Deserialize<JsonElement>(job!.PayloadJson);
        Assert.Equal(run.Id, payload.GetProperty("RunId").GetGuid());
        Assert.Equal(0, payload.GetProperty("ActionIndex").GetInt32());
        Assert.False(job.PayloadJson.Contains("what is the price?", StringComparison.Ordinal));
        Assert.False(job.PayloadJson.Contains("test-access-token", StringComparison.Ordinal));

        // At due time the handler revalidates and sends exactly one Direct Message.
        var outcome = await InvokeFollowUpHandlerAsync(job);
        Assert.IsType<WorkOutcome.Succeeded>(outcome);
        var send = Assert.Single(fixture.Messaging.TypedSends, s => s.RecipientId == participant);
        Assert.Equal("Follow-up: still interested?", Assert.IsType<InstagramMessageContent.PlainText>(send.Content).Text);

        // Scheduler redelivery of the SAME job: zero second send.
        _ = await InvokeFollowUpHandlerAsync(job);
        Assert.Single(fixture.Messaging.TypedSends, s => s.RecipientId == participant);

        var settled = await SingleRunAsync(seeded.AutomationId, includeActions: true);
        Assert.Equal(AutomationRunStatus.Completed, settled!.Status);
        Assert.Equal(AutomationActionStatus.Succeeded, Assert.Single(settled.Actions).Status);
    }

    [Fact]
    public async Task FollowUpIsSuppressedWhenAutomationIsDisabledBeforeDueTime()
    {
        var seeded = await SeedAsync("508", TriggerKind.InboundDirectMessage, ActionKind.ScheduleFollowUp, followUpMinutes: 30);
        var participant = "526-recipient-508";
        Assert.True((await PostSignedAsync(MessageBody(seeded.ProviderId, participant, "mid-inbound-508", "what is the price?"))).IsSuccessStatusCode);

        var run = await SingleRunAsync(seeded.AutomationId, includeActions: true);
        var job = await SingleFollowUpJobAsync(run!.Id, 0);
        Assert.NotNull(job);

        // Lifecycle revalidation: the automation is disabled before due time.
        var scope = fixture.Factory.Services.CreateScope();
        try
        {
            var repository = scope.ServiceProvider.GetRequiredService<IAutomationRepository>();
            var automation = await repository.FindByIdAsync(seeded.AutomationId);
            automation!.Disable(DateTimeOffset.UtcNow);
            await repository.SaveChangesAsync(automation);
        }
        finally
        {
            scope.Dispose();
        }

        var outcome = await InvokeFollowUpHandlerAsync(job);

        Assert.IsType<WorkOutcome.Succeeded>(outcome);
        Assert.DoesNotContain(fixture.Messaging.TypedSends, s => s.RecipientId == participant);
        var settled = await SingleRunAsync(seeded.AutomationId, includeActions: true);
        Assert.Equal(AutomationActionStatus.Suppressed, Assert.Single(settled!.Actions).Status);
        Assert.Equal("automation.notActive", Assert.Single(settled.Actions).FailureCode);
    }

    [Fact]
    public async Task FollowUpOutsideWindowIsTerminalWithZeroSend()
    {
        var seeded = await SeedAsync("509", TriggerKind.InboundDirectMessage, ActionKind.ScheduleFollowUp, followUpMinutes: 30);
        var participant = "526-recipient-509";
        // The inbound message is locally projected 25 hours ago ⇒ the 24h window is expired.
        var oldTimestamp = DateTimeOffset.UtcNow.AddHours(-25).ToUnixTimeMilliseconds();
        Assert.True((await PostSignedAsync(MessageBody(seeded.ProviderId, participant, "mid-inbound-509", "what is the price?", oldTimestamp))).IsSuccessStatusCode);

        var run = await SingleRunAsync(seeded.AutomationId, includeActions: true);
        var job = await SingleFollowUpJobAsync(run!.Id, 0);
        Assert.NotNull(job);

        var outcome = await InvokeFollowUpHandlerAsync(job);

        Assert.IsType<WorkOutcome.Succeeded>(outcome);
        Assert.DoesNotContain(fixture.Messaging.TypedSends, s => s.RecipientId == participant);
        var settled = await SingleRunAsync(seeded.AutomationId, includeActions: true);
        Assert.Equal(AutomationActionStatus.TerminalFailed, Assert.Single(settled!.Actions).Status);
        Assert.Equal("direct.windowExpired", Assert.Single(settled.Actions).FailureCode);
    }

    [Fact]
    public async Task InterruptedFollowUpAttemptNeverResends()
    {
        var seeded = await SeedAsync("510", TriggerKind.InboundDirectMessage, ActionKind.ScheduleFollowUp, followUpMinutes: 30);
        var participant = "526-recipient-510";
        Assert.True((await PostSignedAsync(MessageBody(seeded.ProviderId, participant, "mid-inbound-510", "what is the price?"))).IsSuccessStatusCode);

        var run = await SingleRunAsync(seeded.AutomationId, includeActions: true);
        var job = await SingleFollowUpJobAsync(run!.Id, 0);
        Assert.NotNull(job);

        // Crash window: the handler marked the slot Attempting but died before the send.
        var scope = fixture.Factory.Services.CreateScope();
        try
        {
            var followUps = scope.ServiceProvider.GetRequiredService<FollowUpExecutionUseCase>();
            await followUps.MarkAttemptingAsync(run!.Id, 0, default);
        }
        finally
        {
            scope.Dispose();
        }

        var outcome = await InvokeFollowUpHandlerAsync(job);

        // The redelivered job NEVER re-sends: it settles Uncertain with zero traffic.
        Assert.IsType<WorkOutcome.Succeeded>(outcome);
        Assert.DoesNotContain(fixture.Messaging.TypedSends, s => s.RecipientId == participant);
        var settled = await SingleRunAsync(seeded.AutomationId, includeActions: true);
        Assert.Equal(AutomationActionStatus.Uncertain, Assert.Single(settled!.Actions).Status);
        Assert.Equal("followUp.attemptInterrupted", Assert.Single(settled.Actions).FailureCode);
    }

    [Fact]
    public async Task CommentTriggeredFollowUpNeverSendsWithoutUserReply()
    {
        // M13-012 §48: a comment alone can never authorize a delayed Direct follow-up —
        // only a real user response establishes the 24h Direct window. Even with a
        // commenter identity the job must settle terminally with zero provider traffic
        // (the eligibility anchor is the latest inbound USER MESSAGE, not the comment).
        var seeded = await SeedAsync("511", TriggerKind.CommentCreated, ActionKind.ScheduleFollowUp, followUpMinutes: 30);
        Assert.True((await PostSignedAsync(CommentBody("evt-511", seeded.ProviderId, "comment-511", fromId: "511-commenter"))).IsSuccessStatusCode);

        var run = await SingleRunAsync(seeded.AutomationId, includeActions: true);
        var job = await SingleFollowUpJobAsync(run!.Id, 0);
        Assert.NotNull(job);

        var outcome = await InvokeFollowUpHandlerAsync(job);

        Assert.IsType<WorkOutcome.Succeeded>(outcome);
        Assert.DoesNotContain(fixture.Messaging.TypedSends, s => s.Item1 == "test-access-token-" + "511");
        var settled = await SingleRunAsync(seeded.AutomationId, includeActions: true);
        var slot = Assert.Single(settled!.Actions);
        Assert.Equal(AutomationActionStatus.Suppressed, slot.Status);
        Assert.Equal("followUp.recipientUnavailable", slot.FailureCode);
    }

    [Fact]
    public async Task CommentWithoutAuthorIdentityFailsClosedAtScheduleTime()
    {
        // A comment webhook with no from.id cannot yield an authoritative participant
        // (M13-012 §47): the follow-up must fail closed at schedule time — zero job,
        // terminal slot, zero traffic. Never a doomed job and never a fabricated recipient.
        var seeded = await SeedAsync("512", TriggerKind.CommentCreated, ActionKind.ScheduleFollowUp, followUpMinutes: 30);
        Assert.True((await PostSignedAsync(CommentBody("evt-512", seeded.ProviderId, "comment-512"))).IsSuccessStatusCode);

        var run = await SingleRunAsync(seeded.AutomationId, includeActions: true);
        Assert.NotNull(run);
        var job = await SingleFollowUpJobAsync(run.Id, 0);
        Assert.Null(job);

        var slot = Assert.Single(run.Actions);
        Assert.Equal(AutomationActionStatus.TerminalFailed, slot.Status);
        Assert.Equal("followUp.recipientUnavailable", slot.FailureCode);
        Assert.DoesNotContain(fixture.Messaging.TypedSends, s => s.Item1 == "test-access-token-" + "512");
    }

    // ------------------------------------------------------------------ helpers

    private async Task<HttpResponseMessage> PostSignedAsync(byte[] body)
    {
        using var content = new ByteArrayContent(body);
        content.Headers.Add("X-Hub-Signature-256", Signed(body));
        return await fixture.Client.PostAsync(Endpoint, content);
    }

    private async Task<WorkOutcome> InvokeFollowUpHandlerAsync(ScheduledWorkItem item)
    {
        var scope = fixture.Factory.Services.CreateScope();
        try
        {
            var handlers = scope.ServiceProvider.GetServices<IScheduledWorkHandler>();
            var handler = handlers.Single(h => h.WorkType == FollowUpJobPolicy.WorkType);
            return await handler.HandleAsync(item, default);
        }
        finally
        {
            scope.Dispose();
        }
    }

    private async Task<ScheduledWorkItem?> SingleFollowUpJobAsync(Guid runId, int actionIndex)
    {
        var scope = fixture.Factory.Services.CreateScope();
        try
        {
            var store = scope.ServiceProvider.GetRequiredService<IScheduledWorkStore>();
            return await store.FindByIdempotencyKeyAsync(FollowUpJobPolicy.IdempotencyKey(runId, actionIndex));
        }
        finally
        {
            scope.Dispose();
        }
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

    private async Task<CommentEffectRow?> SingleEffectAsync(Guid accountId, string commentId, InstagramEffectType effectType)
    {
        var scope = fixture.Factory.Services.CreateScope();
        try
        {
            await using var instagram = scope.ServiceProvider.GetRequiredService<InstagramDbContext>();
            return await instagram.CommentEffects.AsNoTracking()
                .SingleOrDefaultAsync(e => e.ConnectedAccountId == accountId && e.ProviderCommentId == commentId && e.EffectType == effectType);
        }
        finally
        {
            scope.Dispose();
        }
    }

    private async Task<RevealFlowRow?> SingleFlowAsync(Guid accountId, string commentId)
    {
        var scope = fixture.Factory.Services.CreateScope();
        try
        {
            await using var instagram = scope.ServiceProvider.GetRequiredService<InstagramDbContext>();
            return await instagram.RevealFlows.AsNoTracking()
                .SingleOrDefaultAsync(r => r.ConnectedAccountId == accountId && r.ProviderCommentId == commentId);
        }
        finally
        {
            scope.Dispose();
        }
    }

    private sealed record Seeded(Guid AutomationId, Guid AccountId, string ProviderId);

    /// <summary>Each test gets its own bound account (with a stored access token), workspace
    /// and automation so the shared database cannot bleed state between scenarios.</summary>
    private async Task<Seeded> SeedAsync(
        string suffix,
        TriggerKind triggerKind,
        ActionKind actionKind,
        string? sourceMediaId = null,
        bool extraPublicReply = false,
        bool revealAction = false,
        int? followUpMinutes = null)
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

        var trigger = triggerKind == TriggerKind.CommentCreated
            ? new AutomationTrigger(TriggerKind.CommentCreated, ["price"],
                TextMatch: TextMatchMode.Keywords,
                Source: sourceMediaId is null ? SourceScope.AnySource : SourceScope.SpecificSource,
                SourceMediaId: sourceMediaId)
            : AutomationTrigger.InboundDirectMessage("price");

        var actions = new List<AutomationAction>
        {
            actionKind switch
            {
                ActionKind.DirectMessage => new AutomationAction(ActionKind.DirectMessage, "DM: hello!"),
                ActionKind.ScheduleFollowUp => new AutomationAction(
                    ActionKind.ScheduleFollowUp,
                    "Follow-up: still interested?",
                    new ActionExtras(Delay: TimeSpan.FromMinutes(followUpMinutes ?? 30))),
                ActionKind.StartRevealFlow => new AutomationAction(
                    ActionKind.StartRevealFlow,
                    "Opening from automation",
                    new ActionExtras(Reveal: new RevealActionContent(
                        "Gate from automation", "Continue", "Reveal from automation", FollowButtonTitle: "دنبال"))),
                _ => new AutomationAction(ActionKind.SendDirectMessage, "DM: hello!"),
            },
        };
        if (extraPublicReply)
        {
            actions.Add(new AutomationAction(ActionKind.SendPublicReply, "Public: see our guide!"));
        }

        var repository = scope.ServiceProvider.GetRequiredService<IAutomationRepository>();
        var automation = Automation.Create(
            Guid.CreateVersion7(), workspaceId, "capability " + suffix,
            AutomationDefinition.Create(trigger, actions), CreatedAt, ChannelAccountId.From(accountId));
        automation.Activate(CreatedAt);
        await repository.SaveChangesAsync(automation);

        scope.Dispose();
        return new Seeded(automation.Id, accountId, providerId);
    }
}
