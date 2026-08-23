using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qasedak.Modules.Conversations.Domain.Conversations;
using Qasedak.Modules.Conversations.Infrastructure.Persistence;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

/// <summary>
/// Cross-module end-to-end: a signed Instagram messaging webhook flows through the
/// composition-root bridge into the Conversations inbox — and duplicate deliveries stay
/// idempotent at the persistence boundary. Unbound provider identities must not fabricate
/// conversations.
/// </summary>
[Collection(ApiTestEnvironment.Name)]
public sealed class WebhookToConversationProjectionTests(ApiPostgreSqlFixture fixture)
{
    private const string AccountProviderId = "17841400000000000";

    private const string Endpoint = "/api/v1/webhooks/instagram";

    private static readonly Guid SeededWorkspaceId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static readonly DateTimeOffset SentAt = DateTimeOffset.FromUnixTimeSeconds(1771900000);

    private static string Signed(byte[] body) => "sha256=" + Convert.ToHexString(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(ApiPostgreSqlFixture.MetaAppSecret), body)).ToLowerInvariant();

    private static byte[] Body(string mid) => Encoding.UTF8.GetBytes(
        "{\"object\":\"instagram\",\"entry\":[{\"id\":\"" + AccountProviderId + "\",\"messaging\":[" +
        "{\"sender\":{\"id\":\"customer-77\"},\"recipient\":{\"id\":\"" + AccountProviderId + "\"}," +
        "\"timestamp\":" + SentAt.ToUnixTimeSeconds() + "," +
        "\"message\":{\"mid\":\"" + mid + "\",\"text\":\"hello from the customer\"}}]}]}");

    [Fact]
    public async Task SignedMessagingWebhookProjectsConversationAndIsIdempotent()
    {
        await SeedBoundAccountAsync();
        var body = Body("mid-e2e-1");

        var first = await PostSignedAsync(body);
        Assert.Equal(System.Net.HttpStatusCode.OK, first.StatusCode);

        await using var conversations = NewConversationsContext();
        var thread = await conversations.Conversations.Include(c => c.Messages).SingleAsync(
            c => c.WorkspaceId == SeededWorkspaceId && c.ParticipantId == "customer-77");
        Assert.Equal(ConversationStatus.Open, thread.Status);
        Assert.Equal(1, thread.UnreadCount);
        var message = Assert.Single(thread.Messages);
        Assert.Equal("mid-e2e-1", message.ProviderMessageId);
        Assert.Equal("hello from the customer", message.Body);
        Assert.Equal(MessageDirection.Inbound, message.Direction);

        // Redelivery of the identical payload: accepted, nothing appended twice.
        var second = await PostSignedAsync(body);
        Assert.Equal(System.Net.HttpStatusCode.OK, second.StatusCode);
        await using var reloadedContext = NewConversationsContext();
        var reloaded = await reloadedContext.Conversations.Include(c => c.Messages).SingleAsync(
            c => c.WorkspaceId == SeededWorkspaceId && c.ParticipantId == "customer-77");
        Assert.Equal(1, reloaded.Messages.Count(m => m.ProviderMessageId == "mid-e2e-1"));
    }

    [Fact]
    public async Task UnboundProviderAccountsDoNotFabricateConversations()
    {
        var response = await PostSignedAsync(Body("mid-unbound-1"));

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        await using var conversations = NewConversationsContext();
        Assert.Empty(await conversations.Conversations
            .Where(c => c.WorkspaceId != SeededWorkspaceId && c.ParticipantId == "customer-77")
            .ToListAsync());
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

    private async Task<HttpResponseMessage> PostSignedAsync(byte[] body)
    {
        using var content = new ByteArrayContent(body);
        content.Headers.Add("X-Hub-Signature-256", Signed(body));
        return await fixture.Client.PostAsync(Endpoint, content);
    }

    private ConversationsDbContext NewConversationsContext()
    {
        var scope = fixture.Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ConversationsDbContext>();
    }
}
