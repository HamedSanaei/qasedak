using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qasedak.Modules.Contacts.Infrastructure.Persistence;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

/// <summary>
/// Cross-module end-to-end: signed Instagram webhooks maintain workspace contacts through
/// the composition-root ContactsInteractionBridge — message senders and comment authors
/// become social identities; redelivery never double-counts an interaction.
/// </summary>
[Collection(ApiTestEnvironment.Name)]
public sealed class WebhookToContactProjectionTests(ApiPostgreSqlFixture fixture)
{
    private const string AccountProviderId = "17841400000000000";

    private const string Endpoint = "/api/v1/webhooks/instagram";

    private static readonly Guid SeededWorkspaceId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static readonly DateTimeOffset SentAt = DateTimeOffset.FromUnixTimeSeconds(1771900500);

    private static string Signed(byte[] body) => "sha256=" + Convert.ToHexString(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(ApiPostgreSqlFixture.MetaAppSecret), body)).ToLowerInvariant();

    private static byte[] MessageBody(string mid) => Encoding.UTF8.GetBytes(
        "{\"object\":\"instagram\",\"entry\":[{\"id\":\"" + AccountProviderId + "\",\"messaging\":[" +
        "{\"sender\":{\"id\":\"contact-customer-9\"},\"recipient\":{\"id\":\"" + AccountProviderId + "\"}," +
        "\"timestamp\":" + SentAt.ToUnixTimeSeconds() + "," +
        "\"message\":{\"mid\":\"" + mid + "\",\"text\":\"hello again\"}}]}]}");

    private static byte[] CommentBody(string commentId) => Encoding.UTF8.GetBytes(
        "{\"object\":\"instagram\",\"entry\":[{\"id\":\"" + AccountProviderId + "\",\"changes\":[{" +
        "\"field\":\"comments\",\"value\":{\"id\":\"" + commentId + "\",\"from\":{\"id\":\"comment-author-3\",\"username\":\"Ada L.\"}," +
        "\"media_id\":\"m-1\",\"text\":\"nice post\",\"created_time\":" + SentAt.ToUnixTimeSeconds() + "}}]}]}");

    [Fact]
    public async Task SignedMessageWebhookCreatesContactAndRedeliveryDoesNotDoubleCount()
    {
        await SeedBoundAccountAsync();
        var body = MessageBody("mid-contacts-1");

        var first = await PostSignedAsync(body);
        Assert.Equal(System.Net.HttpStatusCode.OK, first.StatusCode);
        Guid contactId;
        await using (var contacts = NewContactsContext())
        {
            var identity = await contacts.ContactIdentities.SingleAsync(i =>
                i.WorkspaceId == SeededWorkspaceId && i.ProviderIdentity == "contact-customer-9");
            contactId = identity.ContactId;
            // The ledger is scoped to this contact: exactly the one founding event.
            var interactions = await contacts.ContactInteractions
                .Where(i => i.ContactId == contactId).ToListAsync();
            Assert.Single(interactions);
        }

        // Redelivery: accepted, ledger absorbs it, still exactly one interaction.
        var second = await PostSignedAsync(body);
        Assert.Equal(System.Net.HttpStatusCode.OK, second.StatusCode);
        await using (var reloaded = NewContactsContext())
        {
            Assert.Equal(1, await reloaded.ContactInteractions.CountAsync(i => i.ContactId == contactId));
        }

        // A distinct event from the same sender accumulates on the same contact.
        await PostSignedAsync(MessageBody("mid-contacts-2"));
        await using var final = NewContactsContext();
        Assert.Equal(2, await final.ContactInteractions.CountAsync(i => i.ContactId == contactId));
    }

    [Fact]
    public async Task SignedCommentWebhookProjectsCommentAuthorAsIdentity()
    {
        await SeedBoundAccountAsync();

        var response = await PostSignedAsync(CommentBody("cmt-contacts-1"));
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        await using var contacts = NewContactsContext();
        var identity = await contacts.ContactIdentities.SingleOrDefaultAsync(i =>
            i.WorkspaceId == SeededWorkspaceId && i.ProviderIdentity == "comment-author-3");
        Assert.NotNull(identity);
        Assert.Equal("instagram", identity!.Channel);
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

    private ContactsDbContext NewContactsContext()
    {
        var scope = fixture.Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ContactsDbContext>();
    }
}
