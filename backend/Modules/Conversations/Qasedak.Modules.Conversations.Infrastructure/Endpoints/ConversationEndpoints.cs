using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Qasedak.Modules.Conversations.Application.Conversations;

namespace Qasedak.Modules.Conversations.Infrastructure.Endpoints;

/// <summary>
/// Workspace inbox HTTP surface: paginated, filterable conversation list and per-thread
/// detail. Every route is workspace-scoped; threads outside the workspace are 404.
/// </summary>
public static class ConversationEndpoints
{
    public static IEndpointRouteBuilder MapConversationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var conversations = endpoints.MapGroup("/api/v1/workspaces/{workspaceId:guid}/conversations")
            .WithTags("Conversations")
            .RequireAuthorization();

        conversations.MapGet(string.Empty, async (
            Guid workspaceId,
            string? status,
            int? page,
            int? pageSize,
            IConversationQueries queries,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                workspaceId,
                InboxFilter.From(status, page ?? 1, pageSize ?? InboxFilter.DefaultPageSize),
                cancellationToken);
            return Results.Ok(new
            {
                page = result.Page,
                pageSize = result.PageSize,
                totalCount = result.TotalCount,
                items = result.Items.Select(row => new
                {
                    id = row.Id,
                    channel = row.Channel,
                    participantId = row.ParticipantId,
                    status = row.Status.ToLowerInvariant(),
                    lastMessageAtUtc = row.LastMessageAtUtc,
                    unreadCount = row.UnreadCount,
                    lastMessagePreview = row.LastMessagePreview,
                }),
            });
        });

        conversations.MapGet("/{conversationId:guid}", async (
            Guid workspaceId,
            Guid conversationId,
            IConversationQueries queries,
            CancellationToken cancellationToken) =>
        {
            var detail = await queries.GetDetailAsync(workspaceId, conversationId, cancellationToken);
            return detail is null
                ? Results.NotFound(new { code = "conversation.notFound" })
                : Results.Ok(new
                {
                    id = detail.Value.Row.Id,
                    channel = detail.Value.Row.Channel,
                    participantId = detail.Value.Row.ParticipantId,
                    status = detail.Value.Row.Status.ToLowerInvariant(),
                    lastMessageAtUtc = detail.Value.Row.LastMessageAtUtc,
                    unreadCount = detail.Value.Row.UnreadCount,
                    messages = detail.Value.Messages.Select(m => new
                    {
                        id = m.Id,
                        direction = m.Direction.ToString(),
                        providerMessageId = m.ProviderMessageId,
                        senderId = m.SenderId,
                        body = m.Body,
                        occurredAtUtc = m.OccurredAtUtc,
                    }),
                });
        });

        conversations.MapPost("/{conversationId:guid}/replies", async (
            Guid workspaceId,
            Guid conversationId,
            ReplyRequest request,
            IConversationRepository repository,
            SendReplyUseCase useCase,
            CancellationToken cancellationToken) =>
        {
            var result = await useCase.ExecuteAsync(
                new SendReplyCommand(workspaceId, conversationId, request.Text ?? string.Empty, DateTimeOffset.UtcNow),
                cancellationToken);
            return result.Succeeded
                ? Results.Created(
                    $"/api/v1/workspaces/{workspaceId}/conversations/{conversationId}",
                    new { messageId = result.MessageId })
                : MapReplyFailure(result.FailureCode!);
        }).RequireAuthorization();

        return endpoints;
    }

    private static IResult MapReplyFailure(string failureCode) => failureCode switch
    {
        ReplyFailures.NotFound => Results.Json(new { code = failureCode }, statusCode: StatusCodes.Status404NotFound),
        ReplyFailures.EmptyText or ReplyFailures.TooLongText =>
            Results.Json(new { code = failureCode }, statusCode: StatusCodes.Status400BadRequest),
        ReplyFailures.ArchivedThread or ReplyFailures.MessagingWindowClosed or
            "channel.unsupported" or "instagram.noConnectedAccount" or "instagram.tokenMissing" =>
            Results.Json(new { code = failureCode }, statusCode: StatusCodes.Status409Conflict),
        _ => Results.Json(new { code = failureCode }, statusCode: StatusCodes.Status502BadGateway),
    };

    public sealed record ReplyRequest(string? Text);
}
