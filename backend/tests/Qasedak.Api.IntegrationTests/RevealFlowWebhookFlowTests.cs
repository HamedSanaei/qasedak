using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qasedak.Api.CrossModule;
using Qasedak.Modules.Instagram.Application.Messaging;
using Qasedak.Modules.Instagram.Application.RevealFlow;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

/// <summary>
/// The full M13-011 reveal flow driven through the REAL signed-webhook pipeline (real
/// PostgreSQL, real normalizer, real enrichment, real fan-out, real reveal store +
/// coordinator): comment → PlainText Private Reply → user reply (reply_to.mid) → Direct
/// button-template gate prompt → validated postback → optional follow check → ONE Direct
/// reveal. Provider HTTP is replaced by the fixture's recording edges; the durable
/// one-shot semantics are enforced by the real store (single reveal authority).
/// </summary>
[Collection(ApiTestEnvironment.Name)]
public sealed class RevealFlowWebhookFlowTests(ApiPostgreSqlFixture fixture)
{
    private const string Endpoint = "/api/v1/webhooks/instagram";

    private static readonly DateTimeOffset CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1771900500);

    private static readonly RevealFlowContent Content = new(
        OpeningPrivateReplyText: "Thanks for commenting — reply here to continue.",
        GatePromptText: "Almost there — tap Continue.",
        PostbackButtonTitle: "Continue",
        FollowUrl: null,
        FollowButtonTitle: null,
        RevealText: "Here is your reveal.");

    private static string Signed(byte[] body) => "sha256=" + Convert.ToHexString(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(ApiPostgreSqlFixture.MetaAppSecret), body)).ToLowerInvariant();

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static byte[] CommentBody(string eventId, string accountProviderId, string commentId) => Encoding.UTF8.GetBytes(
        "{\"object\":\"instagram\",\"entry\":[{\"id\":\"" + accountProviderId + "\",\"time\":" + NowMs() + ",\"changes\":[{" +
        "\"field\":\"comments\",\"value\":{\"id\":\"" + commentId + "\",\"text\":\"what is the price?\"," +
        "\"media\":{\"id\":\"media-" + eventId + "\",\"media_product_type\":\"FEED\"}}}]}]}");

    private static byte[] MessageBody(string accountProviderId, string senderId, string mid, string text, string? replyToMid) => Encoding.UTF8.GetBytes(
        "{\"object\":\"instagram\",\"entry\":[{\"id\":\"" + accountProviderId + "\",\"time\":" + NowMs() + ",\"messaging\":[{" +
        "\"sender\":{\"id\":\"" + senderId + "\"},\"recipient\":{\"id\":\"" + accountProviderId + "\"},\"timestamp\":" + NowMs() + "," +
        "\"message\":{\"mid\":\"" + mid + "\",\"text\":\"" + text + "\"" +
        (replyToMid is null ? string.Empty : ",\"reply_to\":{\"mid\":\"" + replyToMid + "\"}") + "}}]}]}");

    private static byte[] PostbackBody(string accountProviderId, string senderId, string payload) => Encoding.UTF8.GetBytes(
        "{\"object\":\"instagram\",\"entry\":[{\"id\":\"" + accountProviderId + "\",\"time\":" + NowMs() + ",\"messaging\":[{" +
        "\"sender\":{\"id\":\"" + senderId + "\"},\"recipient\":{\"id\":\"" + accountProviderId + "\"},\"timestamp\":" + NowMs() + "," +
        "\"postback\":{\"mid\":\"pb-" + NowMs() + "\",\"title\":\"Continue\",\"payload\":\"" + payload + "\"}}]}]}");

    [Fact]
    public async Task FullRevealFlowThroughSignedWebhooksSendsExactlyOneOfEachEffect()
    {
        var seeded = await SeedAsync("401");
        var participant = "526-recipient-comment-rf-401";
        fixture.RevealFlowStarts.Script = _ => new RevealFlowStartRequest(Content, FollowGateMode.Disabled);
        fixture.Relationships.Clear();
        try
        {
            // 1. Comment webhook → durable flow + PlainText Private Reply (comment_id addressing).
            Assert.True((await PostSignedAsync(CommentBody("rf-401", seeded.ProviderId, "comment-rf-401"))).IsSuccessStatusCode);
            var opening = Assert.Single(fixture.PrivateReplies.Sends, s => s.CommentId == "comment-rf-401");
            Assert.Equal(seeded.ProviderId, opening.ProviderAccountId);
            Assert.Equal("test-access-token-401", opening.AccessToken);
            Assert.Equal(Content.OpeningPrivateReplyText, opening.Text);
            Assert.DoesNotContain(fixture.Messaging.Sends, s => s.RecipientId == participant);

            // 2. User replies to the opening message (exact reply_to.mid correlation) →
            //    ONE Direct button-template gate prompt carrying the rv1 token.
            Assert.True((await PostSignedAsync(MessageBody(seeded.ProviderId, participant, "mid-user-401",
                "i want it", replyToMid: "mid-comment-rf-401"))).IsSuccessStatusCode);
            var gate = Assert.Single(fixture.Messaging.TypedSends, s => s.RecipientId == participant);
            var template = Assert.IsType<InstagramMessageContent.ButtonTemplate>(gate.Content);
            Assert.Equal(Content.GatePromptText, template.Text);
            var postback = Assert.IsType<InstagramMessageButton.Postback>(Assert.Single(template.Buttons));
            Assert.Equal(Content.PostbackButtonTitle, postback.Title);
            Assert.StartsWith(RevealCorrelation.TokenPurpose + ".", postback.Payload, StringComparison.Ordinal);

            // 3. Valid postback → single Direct reveal (PlainText, recipient.id).
            Assert.True((await PostSignedAsync(PostbackBody(seeded.ProviderId, participant, postback.Payload))).IsSuccessStatusCode);
            var reveals = fixture.Messaging.TypedSends.Where(s => s.RecipientId == participant).ToList();
            Assert.Equal(2, reveals.Count);
            var reveal = Assert.IsType<InstagramMessageContent.PlainText>(reveals[1].Content);
            Assert.Equal(Content.RevealText, reveal.Text);
            Assert.Equal("test-access-token-401", reveals[1].AccessToken);

            // 4. Durable flow row converged to Revealed with the provider message identity.
            var flow = await SingleFlowAsync(seeded.AccountId, "comment-rf-401");
            Assert.NotNull(flow);
            Assert.Equal(RevealFlowState.Revealed, flow!.State);
            Assert.Equal(RevealSendStatus.Succeeded, flow.RevealStatus);
            Assert.Equal("mid-" + participant, flow.RevealProviderMessageId);
            Assert.Equal(participant, flow.ParticipantIGSID);
            // Follow gate disabled: zero relationship reads anywhere in the flow.
            Assert.Empty(fixture.Relationships.Calls);
        }
        finally
        {
            fixture.RevealFlowStarts.Clear();
        }
    }

    [Fact]
    public async Task PostbackRedeliveryNeverSendsTheRevealTwice()
    {
        var seeded = await SeedAsync("402");
        var participant = "526-recipient-comment-rf-402";
        fixture.RevealFlowStarts.Script = _ => new RevealFlowStartRequest(Content, FollowGateMode.Disabled);
        fixture.Relationships.Clear();
        try
        {
            Assert.True((await PostSignedAsync(CommentBody("rf-402", seeded.ProviderId, "comment-rf-402"))).IsSuccessStatusCode);
            Assert.True((await PostSignedAsync(MessageBody(seeded.ProviderId, participant, "mid-user-402",
                "i want it", replyToMid: "mid-comment-rf-402"))).IsSuccessStatusCode);
            var gate = Assert.Single(fixture.Messaging.TypedSends, s => s.RecipientId == participant);
            var payload = Assert.IsType<InstagramMessageButton.Postback>(
                Assert.Single(Assert.IsType<InstagramMessageContent.ButtonTemplate>(gate.Content).Buttons)).Payload;

            Assert.True((await PostSignedAsync(PostbackBody(seeded.ProviderId, participant, payload))).IsSuccessStatusCode);
            Assert.Equal(2, fixture.Messaging.TypedSends.Count(s => s.RecipientId == participant));

            // Redelivery of the SAME semantic postback under a new envelope: zero second reveal.
            Assert.True((await PostSignedAsync(PostbackBody(seeded.ProviderId, participant, payload))).IsSuccessStatusCode);
            Assert.Equal(2, fixture.Messaging.TypedSends.Count(s => s.RecipientId == participant));

            var flow = await SingleFlowAsync(seeded.AccountId, "comment-rf-402");
            Assert.Equal(RevealFlowState.Revealed, flow!.State);
        }
        finally
        {
            fixture.RevealFlowStarts.Clear();
        }
    }

    [Fact]
    public async Task TamperedOrWrongSenderPostbackMakesZeroProviderCalls()
    {
        var seeded = await SeedAsync("403");
        var participant = "526-recipient-comment-rf-403";
        fixture.RevealFlowStarts.Script = _ => new RevealFlowStartRequest(Content, FollowGateMode.Disabled);
        fixture.Relationships.Clear();
        try
        {
            Assert.True((await PostSignedAsync(CommentBody("rf-403", seeded.ProviderId, "comment-rf-403"))).IsSuccessStatusCode);
            Assert.True((await PostSignedAsync(MessageBody(seeded.ProviderId, participant, "mid-user-403",
                "i want it", replyToMid: "mid-comment-rf-403"))).IsSuccessStatusCode);
            var gate = Assert.Single(fixture.Messaging.TypedSends, s => s.RecipientId == participant);
            var payload = Assert.IsType<InstagramMessageButton.Postback>(
                Assert.Single(Assert.IsType<InstagramMessageContent.ButtonTemplate>(gate.Content).Buttons)).Payload;

            // Single-bit tamper: zero progression, zero provider calls.
            var tampered = payload[..^1] + (payload[^1] == 'A' ? 'B' : 'A');
            Assert.True((await PostSignedAsync(PostbackBody(seeded.ProviderId, participant, tampered))).IsSuccessStatusCode);
            Assert.Equal(1, fixture.Messaging.TypedSends.Count(s => s.RecipientId == participant));

            // The same valid token replayed by a DIFFERENT participant: zero reveal.
            Assert.True((await PostSignedAsync(PostbackBody(seeded.ProviderId, "526-recipient-other", payload))).IsSuccessStatusCode);
            Assert.Equal(1, fixture.Messaging.TypedSends.Count(s => s.RecipientId == participant));
            Assert.Empty(fixture.Relationships.Calls);

            var flow = await SingleFlowAsync(seeded.AccountId, "comment-rf-403");
            Assert.Equal(RevealFlowState.AwaitingPostback, flow!.State);
        }
        finally
        {
            fixture.RevealFlowStarts.Clear();
        }
    }

    [Fact]
    public async Task FollowGateBlocksUntilTheParticipantActuallyFollows()
    {
        var seeded = await SeedAsync("404");
        var participant = "526-recipient-comment-rf-404";
        fixture.RevealFlowStarts.Script = _ => new RevealFlowStartRequest(Content, FollowGateMode.EnabledWhenSupported);
        fixture.Relationships.Clear();
        fixture.Relationships.Script = () => FollowStateResult.DoesNotFollow();
        try
        {
            Assert.True((await PostSignedAsync(CommentBody("rf-404", seeded.ProviderId, "comment-rf-404"))).IsSuccessStatusCode);
            Assert.True((await PostSignedAsync(MessageBody(seeded.ProviderId, participant, "mid-user-404",
                "i want it", replyToMid: "mid-comment-rf-404"))).IsSuccessStatusCode);
            var gate = Assert.Single(fixture.Messaging.TypedSends, s => s.RecipientId == participant);
            var payload = Assert.IsType<InstagramMessageButton.Postback>(
                Assert.Single(Assert.IsType<InstagramMessageContent.ButtonTemplate>(gate.Content).Buttons)).Payload;

            // Valid postback, but the participant does not follow: NO reveal, NO new prompt.
            Assert.True((await PostSignedAsync(PostbackBody(seeded.ProviderId, participant, payload))).IsSuccessStatusCode);
            Assert.Equal(1, fixture.Messaging.TypedSends.Count(s => s.RecipientId == participant));
            Assert.Single(fixture.Relationships.Calls);
            Assert.Equal(participant, fixture.Relationships.Calls[0].ParticipantIGSId);
            var blocked = await SingleFlowAsync(seeded.AccountId, "comment-rf-404");
            Assert.Equal(RevealFlowState.AwaitingPostback, blocked!.State);
            Assert.Equal(FollowState.DoesNotFollow, blocked.LastFollowState);

            // The participant follows; the SAME postback re-check reveals exactly once.
            fixture.Relationships.Script = () => FollowStateResult.Follows();
            Assert.True((await PostSignedAsync(PostbackBody(seeded.ProviderId, participant, payload))).IsSuccessStatusCode);
            Assert.Equal(2, fixture.Messaging.TypedSends.Count(s => s.RecipientId == participant));
            Assert.Equal(2, fixture.Relationships.Calls.Count);
        }
        finally
        {
            fixture.RevealFlowStarts.Clear();
            fixture.Relationships.Script = () => FollowStateResult.Follows();
        }
    }

    [Fact]
    public async Task DefaultNoProviderStartsNoFlowsAndNoOpeningReplies()
    {
        // Regression: without a configured start provider, comment webhooks behave exactly
        // as before M13-011 — zero reveal flows, zero opening Private Replies.
        var seeded = await SeedAsync("405");
        Assert.True((await PostSignedAsync(CommentBody("rf-405", seeded.ProviderId, "comment-rf-405"))).IsSuccessStatusCode);

        Assert.DoesNotContain(fixture.PrivateReplies.Sends, s => s.CommentId == "comment-rf-405");
        Assert.Null(await SingleFlowAsync(seeded.AccountId, "comment-rf-405"));
    }

    private async Task<HttpResponseMessage> PostSignedAsync(byte[] body)
    {
        using var content = new ByteArrayContent(body);
        content.Headers.Add("X-Hub-Signature-256", Signed(body));
        return await fixture.Client.PostAsync(Endpoint, content);
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

    private sealed record Seeded(Guid AccountId, string ProviderId);

    /// <summary>Each test gets its own bound account (with a stored access token) so the shared database cannot bleed.</summary>
    private async Task<Seeded> SeedAsync(string suffix)
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

        var tokens = scope.ServiceProvider.GetRequiredService<Qasedak.Modules.Instagram.Application.Accounts.IProtectedTokenStore>();
        await tokens.StoreAsync(account.Id, "test-access-token-" + suffix);
        await instagram.SaveChangesAsync();

        scope.Dispose();
        return new Seeded(account.Id, providerId);
    }
}
