using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qasedak.Modules.Conversations.Domain.Conversations;
using Qasedak.Modules.Conversations.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

/// <summary>
/// Workspace inbox query APIs: authenticated, paginated, filterable list plus per-thread
/// detail; threads outside the queried workspace are invisible (404).
/// </summary>
[Collection(ApiTestEnvironment.Name)]
public sealed class ConversationInboxEndpointTests(ApiPostgreSqlFixture fixture)
{
    // Fresh workspace per test keeps assertions independent of execution order and of
    // other classes sharing the collection database.
    private static Guid FreshWorkspace() => Guid.CreateVersion7();

    private sealed record LoginResponse([property: JsonPropertyName("accessToken")] string AccessToken);

    private async Task<string> TokenAsync(string email, Guid workspaceId)
    {
        await fixture.Client.PostAsJsonAsync("/api/v1/identity/register", new { email, password = "Passw0rd!23", displayName = "Inbox Tester" });
        var login = await fixture.Client.PostAsJsonAsync("/api/v1/identity/login", new { email, password = "Passw0rd!23" });
        var payload = await login.Content.ReadFromJsonAsync<LoginResponse>();
        var token = payload!.AccessToken;

        // The tester must be a member of the workspace the inbox belongs to.
        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/identity/me");
        me.Headers.Authorization = new("Bearer", token);
        using var meResponse = await fixture.Client.SendAsync(me);
        meResponse.EnsureSuccessStatusCode();
        var userId = Guid.Parse((await meResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetString()!);
        await fixture.EnsureWorkspaceMemberAsync(workspaceId, userId);

        return token;
    }

    private HttpClient AuthedClient(string token)
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private async Task SeedConversationAsync(Guid workspaceId, string participantId, ConversationStatus status, params (string mid, string text)[] messages)
    {
        var scope = fixture.Factory.Services.CreateScope();
        await using var context = scope.ServiceProvider.GetRequiredService<ConversationsDbContext>();
        var conversation = Conversation.Create(Guid.CreateVersion7(), workspaceId, "instagram", participantId, DateTimeOffset.UtcNow.AddDays(-2));
        foreach (var (mid, text) in messages)
        {
            conversation.AppendMessage(Guid.CreateVersion7(), MessageDirection.Inbound, mid, participantId, text, DateTimeOffset.UtcNow.AddMinutes(-30));
            conversation.AppendMessage(Guid.CreateVersion7(), MessageDirection.Outbound, null, "our-account", "reply: " + text, DateTimeOffset.UtcNow.AddMinutes(-29));
        }

        if (status == ConversationStatus.Archived)
        {
            conversation.Archive(DateTimeOffset.UtcNow.AddMinutes(-10));
        }
        else
        {
            conversation.MarkRead(DateTimeOffset.UtcNow.AddMinutes(-5));
        }

        await context.Conversations.AddAsync(conversation);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task InboxListIsPaginatedFilteredAndWorkspaceScoped()
    {
        var workspace = FreshWorkspace();
        await SeedConversationAsync(workspace, "p-open-1", ConversationStatus.Open, ("m-a1", "first open"));
        await SeedConversationAsync(workspace, "p-open-2", ConversationStatus.Open, ("m-a2", "second open"));
        await SeedConversationAsync(workspace, "p-arch", ConversationStatus.Archived, ("m-a3", "archived one"));

        using var client = AuthedClient(await TokenAsync("inbox-list@example.com", workspace));
        var page = await client.GetFromJsonAsync<InboxPageResponse>(
            $"/api/v1/workspaces/{workspace}/conversations?status=open&page=1&pageSize=10");

        Assert.NotNull(page);
        Assert.Equal(2, page!.TotalCount);
        Assert.All(page.Items, item => Assert.Equal("open", item.Status));
        // The preview is the latest message in the thread: our auto-reply.
        Assert.Contains(page.Items, item => item.LastMessagePreview == "reply: second open");

        // Page size is enforced; raw call so a server error surfaces its problem details.
        var tinyResponse = await client.GetAsync($"/api/v1/workspaces/{workspace}/conversations?page=1&pageSize=1");
        Assert.True(tinyResponse.IsSuccessStatusCode, await tinyResponse.Content.ReadAsStringAsync());
        var tinyPage = await tinyResponse.Content.ReadFromJsonAsync<InboxPageResponse>();
        Assert.Single(tinyPage!.Items);

        // A workspace the caller does not belong to is denied outright (membership policy).
        var untouched = FreshWorkspace();
        var otherResponse = await client.GetAsync($"/api/v1/workspaces/{untouched}/conversations");
        Assert.Equal(HttpStatusCode.Forbidden, otherResponse.StatusCode);

        // Anonymous access is rejected.
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Client.GetAsync(
            $"/api/v1/workspaces/{workspace}/conversations")).StatusCode);
    }

    [Fact]
    public async Task InboxDetailReturnsThreadWithOrderedMessages()
    {
        var workspace = FreshWorkspace();
        await SeedConversationAsync(workspace, "p-detail", ConversationStatus.Open,
            ("m-b1", "hello there"), ("m-b2", "second message"));

        using var client = AuthedClient(await TokenAsync("inbox-detail@example.com", workspace));
        var scope = fixture.Factory.Services.CreateScope();
        await using var context = scope.ServiceProvider.GetRequiredService<ConversationsDbContext>();
        var conversationId = await context.Conversations
            .Where(c => c.WorkspaceId == workspace && c.ParticipantId == "p-detail")
            .Select(c => c.Id)
            .SingleAsync();

        var detail = await client.GetFromJsonAsync<InboxDetailResponse>(
            $"/api/v1/workspaces/{workspace}/conversations/{conversationId}");

        Assert.NotNull(detail);
        // Two seeded rounds → inbound+outbound each.
        Assert.Equal(4, detail!.Messages.Count);
        Assert.True(detail.Messages[0].OccurredAtUtc <= detail.Messages[^1].OccurredAtUtc);
        Assert.Equal("hello there", detail.Messages[0].Body);
        Assert.Contains(detail.Messages, m => m.Body.StartsWith("reply:", StringComparison.Ordinal));

        // A workspace the caller does not belong to cannot read this thread (policy denial).
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(
            $"/api/v1/workspaces/{FreshWorkspace()}/conversations/{conversationId}")).StatusCode);
        // Unknown ids are 404, not errors.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            $"/api/v1/workspaces/{workspace}/conversations/{Guid.CreateVersion7()}")).StatusCode);
    }

    private sealed record InboxPageResponse(
        [property: JsonPropertyName("page")] int Page,
        [property: JsonPropertyName("pageSize")] int PageSize,
        [property: JsonPropertyName("totalCount")] int TotalCount,
        [property: JsonPropertyName("items")] List<InboxItem> Items);

    private sealed record InboxItem(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("channel")] string Channel,
        [property: JsonPropertyName("participantId")] string ParticipantId,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("unreadCount")] int UnreadCount,
        [property: JsonPropertyName("lastMessagePreview")] string? LastMessagePreview);

    private sealed record InboxDetailResponse(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("messages")] List<InboxMessage> Messages);

    private sealed record InboxMessage(
        [property: JsonPropertyName("body")] string Body,
        [property: JsonPropertyName("occurredAtUtc")] DateTimeOffset OccurredAtUtc);
}
