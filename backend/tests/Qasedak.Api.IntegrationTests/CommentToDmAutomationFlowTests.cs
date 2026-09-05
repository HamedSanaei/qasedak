using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;
using Qasedak.Modules.Automations.Infrastructure.Persistence;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

/// <summary>
/// The first full automation flow end to end: a signed Instagram comment webhook flows
/// through the composition-root bridge into the Automations engine; the matched active
/// definition sends exactly one DM through the outbound channel gateway (24h-window
/// policy enforced there), the ledger records the run, and webhook redelivery never
/// re-sends. Policy restrictions and unsupported scenarios are covered explicitly.
/// Each test uses its own commenter identity so shared-database state cannot bleed.
/// </summary>
[Collection(ApiTestEnvironment.Name)]
public sealed class CommentToDmAutomationFlowTests(ApiPostgreSqlFixture fixture)
{
    private const string Endpoint = "/api/v1/webhooks/instagram";

    private static readonly DateTimeOffset CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1771900500);

    private static string Signed(byte[] body) => "sha256=" + Convert.ToHexString(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(ApiPostgreSqlFixture.MetaAppSecret), body)).ToLowerInvariant();

    private static byte[] Body(string eventId, string accountProviderId, string commenterId, string text = "what is the price?") => Encoding.UTF8.GetBytes(
        "{\"object\":\"instagram\",\"entry\":[{\"id\":\"" + accountProviderId + "\",\"changes\":[" +
        "{\"field\":\"comments\",\"value\":{\"id\":\"comment-" + eventId + "\",\"from\":{\"id\":\"" + commenterId + "\"}," +
        "\"text\":\"" + text + "\",\"created_time\":" + CreatedAt.ToUnixTimeSeconds() + "}}]}]}");

    [Fact]
    public async Task MatchingCommentSendsExactlyOneDmAndRedeliveryDoesNotRepeat()
    {
        var (automationId, providerId) = await SeedAsync("101", activeAutomation: true);
        var body = Body("e2e-flow-1", providerId, "commenter-1001");
        var first = await PostSignedAsync(body);
        Assert.True(first.IsSuccessStatusCode);

        var send = Assert.Single(SendsTo("commenter-1001"));
        Assert.Equal("DM: thanks for asking about price!", send.Text);
        Assert.Equal("test-access-token-101", send.AccessToken);

        // Redelivery of the identical payload: accepted, but the ledger prevents a second DM.
        var second = await PostSignedAsync(body);
        Assert.True(second.IsSuccessStatusCode);
        Assert.Single(SendsTo("commenter-1001"));

        // The ledger shows one completed run pinned to version 1.
        var run = await SingleRunAsync(automationId, includeActions: false);
        Assert.NotNull(run);
        Assert.Equal(AutomationRunStatus.Completed, run!.Status);
        Assert.Equal(1, run.AutomationVersionNumber);
        Assert.NotNull(run.FinishedAtUtc);
    }

    [Fact]
    public async Task NonMatchingCommentLeavesNoSendAndNoRun()
    {
        var (automationId, providerId) = await SeedAsync("102", activeAutomation: true);
        var body = Body("e2e-flow-2", providerId, "commenter-1002", text: "just saying hi");
        var response = await PostSignedAsync(body);
        Assert.True(response.IsSuccessStatusCode);

        Assert.Empty(SendsTo("commenter-1002"));
        Assert.Null(await SingleRunAsync(automationId, includeActions: false));
    }

    [Fact]
    public async Task RecipientOutsideMessagingWindowRecordsFailedSlotWithStableCode()
    {
        var (automationId, providerId) = await SeedAsync("103", activeAutomation: true);
        fixture.Messaging.RejectRecipientsOutsideWindow.Add("commenter-1003");
        try
        {
            var response = await PostSignedAsync(Body("e2e-flow-3", providerId, "commenter-1003"));
            Assert.True(response.IsSuccessStatusCode);

            var run = await SingleRunAsync(automationId, includeActions: true);
            Assert.NotNull(run);
            Assert.Equal(AutomationRunStatus.Failed, run!.Status);
            var action = Assert.Single(run.Actions);
            Assert.Equal(AutomationActionStatus.Failed, action.Status);
            Assert.Equal("instagram.windowExpired", action.FailureCode);
        }
        finally
        {
            fixture.Messaging.RejectRecipientsOutsideWindow.Clear();
        }
    }

    [Fact]
    public async Task DisabledAutomationsNeverDispatch()
    {
        var (automationId, providerId) = await SeedAsync("104", activeAutomation: false);
        var response = await PostSignedAsync(Body("e2e-flow-4", providerId, "commenter-1004"));

        Assert.True(response.IsSuccessStatusCode);
        Assert.Empty(SendsTo("commenter-1004"));
        Assert.Null(await SingleRunAsync(automationId, includeActions: false));
    }

    private async Task<HttpResponseMessage> PostSignedAsync(byte[] body)
    {
        using var content = new ByteArrayContent(body);
        content.Headers.Add("X-Hub-Signature-256", Signed(body));
        return await fixture.Client.PostAsync(Endpoint, content);
    }

    private List<(string AccessToken, string RecipientId, string Text)> SendsTo(string recipientId) =>
        fixture.Messaging.Sends.Where(s => s.RecipientId == recipientId).ToList();

    private async Task<AutomationRunRow?> SingleRunAsync(Guid automationId, bool includeActions)
    {
        await using var automations = NewAutomationsContext();
        var query = automations.AutomationRuns.AsQueryable();
        if (includeActions)
        {
            query = query.Include(r => r.Actions);
        }

        return await query.SingleOrDefaultAsync(r => r.AutomationId == automationId);
    }

    /// <summary>Each test gets its own bound account (with a stored access token), workspace
    /// and automation so the shared database cannot bleed state between scenarios.</summary>
    private async Task<(Guid AutomationId, string ProviderId)> SeedAsync(string suffix, bool activeAutomation)
    {
        var providerId = "1784140000000" + suffix;
        var workspaceId = Guid.CreateVersion7();
        var scope = fixture.Factory.Services.CreateScope();

        Guid accountId;
        var instagram = scope.ServiceProvider.GetRequiredService<InstagramDbContext>();
        var account = ConnectedAccount.Create(
            Guid.CreateVersion7(),
            workspaceId,
            providerId,
            ConnectionPath.InstagramLogin,
            ["instagram_business_manage_messages"],
            DateTimeOffset.UtcNow.AddDays(30),
            DateTimeOffset.UtcNow);
        await instagram.Accounts.AddAsync(account);
        await instagram.SaveChangesAsync();
        accountId = account.Id;

        // The outbound gateway needs decryptable token material for the DM send. The token
        // store shares the scoped context — no early disposal here.
        var tokens = scope.ServiceProvider.GetRequiredService<Qasedak.Modules.Instagram.Application.Accounts.IProtectedTokenStore>();
        await tokens.StoreAsync(accountId, "test-access-token-" + suffix);
        await instagram.SaveChangesAsync();

        // Persisted through the module's own aggregate-mapping path, bound to the
        // exact seeded account (M13-002: unbound automations never execute).
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
        return (automation.Id, providerId);
    }

    private AutomationsDbContext NewAutomationsContext()
    {
        var scope = fixture.Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AutomationsDbContext>();
    }
}
