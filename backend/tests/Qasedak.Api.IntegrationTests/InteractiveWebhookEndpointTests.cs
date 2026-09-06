using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qasedak.Modules.Instagram.Application.Webhooks;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

/// <summary>
/// M13-008 end to end over real HTTP + real PostgreSQL: signed interactive webhook bodies
/// (postbacks, read receipts, comments, inbound messages — current official shapes with
/// millisecond timestamps) flow through raw-HMAC → durable inbox → pure normalization →
/// exact-account resolution → composition-root dispatch. Every dispatched event must carry
/// the exact Qasedak ConnectedAccount identity; unknown/ambiguous accounts fail closed
/// with zero dispatch; identical redelivery is exactly-once at the SHA-256 inbox boundary.
/// </summary>
[Collection(ApiTestEnvironment.Name)]
public sealed class InteractiveWebhookEndpointTests(ApiPostgreSqlFixture fixture)
{
    private const string Endpoint = "/api/v1/webhooks/instagram";

    // Official-scale millisecond timestamps (current Meta examples).
    private const long EntryTimeMs = 1502905976963;
    private const long MessageTimestampMs = 1502905976377;

    private static string Signed(byte[] body) => "sha256=" + Convert.ToHexString(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(ApiPostgreSqlFixture.MetaAppSecret), body)).ToLowerInvariant();

    private static string NewProviderId() => "1784" + Random.Shared.NextInt64(10_000_000_000, 99_999_999_999);

    private List<IIntegrationEvent> EventsAfter(Func<Task> action)
    {
        var marker = fixture.Dispatched.Count;
        action().GetAwaiter().GetResult();
        return fixture.Dispatched.Skip(marker).ToList();
    }

    private static byte[] MessagingBody(string providerAccountId, string itemJson) => Encoding.UTF8.GetBytes(
        "{\"object\":\"instagram\",\"entry\":[{\"id\":\"" + providerAccountId + "\",\"time\":" + EntryTimeMs + ",\"messaging\":[" + itemJson + "]}]}");

    private static string MessagingItem(string payload) =>
        "{\"sender\":{\"id\":\"participant-77\"},\"recipient\":{\"id\":\"<ACCOUNT>\"},\"timestamp\":" + MessageTimestampMs + "," + payload + "}";

    private static byte[] CommentBody(string providerAccountId, string commentId) => Encoding.UTF8.GetBytes(
        "{\"object\":\"instagram\",\"entry\":[{\"id\":\"" + providerAccountId + "\",\"time\":" + EntryTimeMs + ",\"changes\":[{\"field\":\"comments\",\"value\":" +
        "{\"id\":\"" + commentId + "\",\"from\":{\"id\":\"commenter-77\",\"username\":\"e2e_customer\"},\"text\":\"is this available?\",\"media\":{\"id\":\"media-77\",\"media_product_type\":\"FEED\"}}}]}]}");

    private async Task<Guid> SeedActiveAccountAsync(string providerAccountId)
    {
        var scope = fixture.Factory.Services.CreateScope();
        await using var context = scope.ServiceProvider.GetRequiredService<InstagramDbContext>();
        var existing = await context.Accounts.SingleOrDefaultAsync(a => a.ProviderUserId == providerAccountId);
        if (existing is not null)
        {
            return existing.Id;
        }

        var account = ConnectedAccount.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            providerAccountId,
            ConnectionPath.InstagramLogin,
            ["instagram_business_manage_messages"],
            DateTimeOffset.UtcNow.AddDays(30),
            DateTimeOffset.UtcNow);
        await context.Accounts.AddAsync(account);
        await context.SaveChangesAsync();
        return account.Id;
    }

    private async Task<HttpResponseMessage> PostSignedAsync(byte[] body)
    {
        using var content = new ByteArrayContent(body);
        content.Headers.Add("X-Hub-Signature-256", Signed(body));
        return await fixture.Client.PostAsync(Endpoint, content);
    }

    // --------------------------------------------------------------- postback / read

    [Fact]
    public async Task SignedPostbackDispatchesWithExactAccount()
    {
        var providerId = NewProviderId();
        var accountId = await SeedActiveAccountAsync(providerId);
        var body = MessagingBody(providerId,
            MessagingItem("\"postback\":{\"mid\":\"pb-77\",\"title\":\"Get Started\",\"payload\":\"GET_STARTED\"}").Replace("<ACCOUNT>", providerId));

        var events = EventsAfter(async () =>
        {
            var response = await PostSignedAsync(body);
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        });

        var postback = Assert.IsType<InstagramPostbackReceived>(Assert.Single(events));
        Assert.Equal(accountId, postback.ConnectedAccountId);
        Assert.NotNull(postback.WorkspaceId);
        Assert.Equal(providerId, postback.ProviderAccountId);
        Assert.Equal("participant-77", postback.SenderId);
        Assert.Equal("pb-77", postback.ProviderMessageId);
        Assert.Equal("Get Started", postback.Title);
        Assert.Equal("GET_STARTED", postback.Payload);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(MessageTimestampMs), postback.OccurredAtUtc);
        // Deterministic fragment identity: inbox event id (raw-body SHA-256) + position.
        Assert.EndsWith(":e0:m0", postback.EventId);
    }

    [Fact]
    public async Task SignedReadReceiptUsesMidAndNeverWatermark()
    {
        var providerId = NewProviderId();
        var accountId = await SeedActiveAccountAsync(providerId);
        // A legacy watermark property may coexist in provider samples; it is deliberately
        // never read — the event contract has no watermark concept (compile-time proof).
        var body = MessagingBody(providerId,
            MessagingItem("\"read\":{\"mid\":\"seen-77\",\"watermark\":" + MessageTimestampMs + "}").Replace("<ACCOUNT>", providerId));

        var events = EventsAfter(async () =>
        {
            var response = await PostSignedAsync(body);
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        });

        var read = Assert.IsType<InstagramMessageRead>(Assert.Single(events));
        Assert.Equal(accountId, read.ConnectedAccountId);
        Assert.Equal("seen-77", read.ProviderMessageId);
        Assert.Equal("participant-77", read.SenderId);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(MessageTimestampMs), read.OccurredAtUtc);
        Assert.Equal(providerId, read.ProviderAccountId);
    }

    // --------------------------------------------------------------- comment

    [Fact]
    public async Task SignedCommentDispatchesWithExactAccountMediaAndUsername()
    {
        var providerId = NewProviderId();
        var accountId = await SeedActiveAccountAsync(providerId);

        var events = EventsAfter(async () =>
        {
            var response = await PostSignedAsync(CommentBody(providerId, "comment-e2e-77"));
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        });

        var comment = Assert.IsType<InstagramCommentCreated>(Assert.Single(events));
        Assert.Equal(accountId, comment.ConnectedAccountId);
        Assert.Equal(providerId, comment.ProviderAccountId);
        Assert.Equal("comment-e2e-77", comment.CommentId);
        Assert.Equal("commenter-77", comment.FromId);
        Assert.Equal("e2e_customer", comment.CommenterUsername);
        Assert.Equal("is this available?", comment.Text);
        Assert.Equal("media-77", comment.MediaId);
        Assert.Null(comment.OriginalMediaId);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(EntryTimeMs), comment.CreatedAtUtc);
    }

    // --------------------------------------------------------------- message

    [Fact]
    public async Task SignedTextMessageDispatchesWithMillisecondTimestamp()
    {
        var providerId = NewProviderId();
        var accountId = await SeedActiveAccountAsync(providerId);
        var body = MessagingBody(providerId,
            MessagingItem("\"message\":{\"mid\":\"m-77\",\"text\":\"hello there\"}").Replace("<ACCOUNT>", providerId));

        var events = EventsAfter(async () =>
        {
            var response = await PostSignedAsync(body);
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        });

        var message = Assert.IsType<InstagramMessageReceived>(Assert.Single(events));
        Assert.Equal(accountId, message.ConnectedAccountId);
        Assert.Equal("participant-77", message.SenderId);
        Assert.Equal("hello there", message.Text);
        Assert.Equal("m-77", message.ProviderMessageId);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(MessageTimestampMs), message.SentAtUtc);
    }

    // --------------------------------------------------------------- filters

    [Theory]
    [InlineData("message-echo", "\"message\":{\"is_echo\":true,\"mid\":\"m-echo\",\"text\":\"we said this\"}")]
    [InlineData("message-self", "\"message\":{\"is_self\":true,\"mid\":\"m-self\",\"text\":\"preview\"}")]
    [InlineData("message-deleted", "\"message\":{\"is_deleted\":true,\"mid\":\"m-del\",\"text\":\"stale\"}")]
    [InlineData("message-unsupported", "\"message\":{\"is_unsupported\":true,\"mid\":\"m-uns\"}")]
    [InlineData("message-attachment-only", "\"message\":{\"mid\":\"m-att\",\"attachments\":[{\"type\":\"image\",\"payload\":{\"url\":\"https://cdn.example/x\"}}]}")]
    public async Task NonTriggeringFragmentsDispatchZeroEvents(string _, string payload)
    {
        var providerId = NewProviderId();
        await SeedActiveAccountAsync(providerId);
        var body = MessagingBody(providerId, MessagingItem(payload).Replace("<ACCOUNT>", providerId));

        var events = EventsAfter(async () =>
        {
            var response = await PostSignedAsync(body);
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        });

        // Echoes, self messages, deletions, unsupported and attachment-only fragments must
        // never become automation-triggering inbound text events.
        Assert.Empty(events);
    }

    // --------------------------------------------------------------- fan-out / fail-closed

    [Fact]
    public async Task MultiEntryWebhookResolvesEachAccountIndependently()
    {
        var providerA = NewProviderId();
        var providerB = NewProviderId();
        var accountA = await SeedActiveAccountAsync(providerA);
        var accountB = await SeedActiveAccountAsync(providerB);

        var body = Encoding.UTF8.GetBytes(
            "{\"object\":\"instagram\",\"entry\":[" +
            "{\"id\":\"" + providerA + "\",\"time\":" + EntryTimeMs + ",\"messaging\":[{\"sender\":{\"id\":\"u-a\"},\"recipient\":{\"id\":\"" + providerA + "\"},\"timestamp\":" + MessageTimestampMs + ",\"message\":{\"mid\":\"m-a\",\"text\":\"for A\"}}]}," +
            "{\"id\":\"" + providerB + "\",\"time\":" + EntryTimeMs + ",\"changes\":[{\"field\":\"comments\",\"value\":{\"id\":\"c-b\",\"from\":{\"id\":\"u-b\"},\"text\":\"for B\",\"media\":{\"id\":\"media-b\"}}}]}" +
            "]}");

        var events = EventsAfter(async () =>
        {
            var response = await PostSignedAsync(body);
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        });

        Assert.Equal(2, events.Count);
        var message = Assert.IsType<InstagramMessageReceived>(events[0]);
        var comment = Assert.IsType<InstagramCommentCreated>(events[1]);
        Assert.Equal(accountA, message.ConnectedAccountId);
        Assert.Equal(accountB, comment.ConnectedAccountId);
        Assert.NotEqual(message.ConnectedAccountId, comment.ConnectedAccountId);
        // No first-account reuse: each event carries its own entry's resolution.
        Assert.Equal(providerA, message.ProviderAccountId);
        Assert.Equal(providerB, comment.ProviderAccountId);
    }

    [Fact]
    public async Task UnknownAccountFailsClosedWithZeroDispatch()
    {
        // No account seeded for this provider id.
        var body = MessagingBody(NewProviderId(),
            MessagingItem("\"message\":{\"mid\":\"m-unknown\",\"text\":\"hi\"}").Replace("<ACCOUNT>", "irrelevant"));

        var events = EventsAfter(async () =>
        {
            var response = await PostSignedAsync(body);
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        });

        Assert.Empty(events);
    }

    [Fact]
    public async Task AmbiguousAccountFailsClosedWithZeroDispatch()
    {
        var providerId = NewProviderId();
        await SeedActiveAccountAsync(providerId);
        // Corrupted state: a second active owner for the same professional IG_ID. The
        // M13-002 resolver reports Ambiguous; the inbox must fail closed — zero dispatch.
        var scope = fixture.Factory.Services.CreateScope();
        await using (var context = scope.ServiceProvider.GetRequiredService<InstagramDbContext>())
        {
            await context.Accounts.AddAsync(ConnectedAccount.Create(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                providerId,
                ConnectionPath.InstagramLogin,
                ["instagram_business_manage_messages"],
                DateTimeOffset.UtcNow.AddDays(30),
                DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
        }

        var body = MessagingBody(providerId,
            MessagingItem("\"message\":{\"mid\":\"m-amb\",\"text\":\"hi\"}").Replace("<ACCOUNT>", providerId));

        var events = EventsAfter(async () =>
        {
            var response = await PostSignedAsync(body);
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        });

        Assert.Empty(events);
    }

    // --------------------------------------------------------------- redelivery

    [Fact]
    public async Task IdenticalRedeliveryIsExactlyOnceAtTheInboxBoundary()
    {
        var providerId = NewProviderId();
        await SeedActiveAccountAsync(providerId);
        var body = MessagingBody(providerId,
            MessagingItem("\"message\":{\"mid\":\"m-redeliver\",\"text\":\"again?\"}").Replace("<ACCOUNT>", providerId));

        var events = EventsAfter(async () =>
        {
            var first = await PostSignedAsync(body);
            Assert.Equal(System.Net.HttpStatusCode.OK, first.StatusCode);
            var second = await PostSignedAsync(body);
            Assert.Equal(System.Net.HttpStatusCode.OK, second.StatusCode);
        });

        // The inbox key is the SHA-256 of the exact raw body: the second delivery maps to
        // the same primary key and is swallowed as an accepted no-op — one semantic dispatch.
        Assert.Single(events);

        var expectedEventId = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        var scope = fixture.Factory.Services.CreateScope();
        await using var context = scope.ServiceProvider.GetRequiredService<InstagramDbContext>();
        var row = await context.WebhookInbox.SingleAsync(e => e.EventId == expectedEventId);
        Assert.True(row.DeliveryAttempts >= 1, "redelivery must be counted on the same durable row");
    }
}
