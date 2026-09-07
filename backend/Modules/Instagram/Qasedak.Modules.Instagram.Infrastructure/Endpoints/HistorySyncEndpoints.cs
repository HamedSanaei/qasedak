using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.HistorySync;

namespace Qasedak.Modules.Instagram.Infrastructure.Endpoints;

/// <summary>
/// Workspace-scoped conversation-history sync surface (M13-013 §69/§115). The manual
/// resync endpoint performs ZERO provider traversal inside the HTTP request: it
/// validates workspace membership + exact account ownership first (no token read
/// before authorization), then creates-or-coalesces the durable operation and
/// enqueues the M13-004 job, returning 202 with the operation identity. The status
/// endpoint is token-free and truthful (never "fully synced").
/// </summary>
public static partial class HistorySyncEndpoints
{
    public static IEndpointRouteBuilder MapHistorySyncEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/workspaces/{workspaceId:guid}/instagram")
            .WithTags("Instagram History Sync")
            .RequireAuthorization("workspace-member");

        group.MapPost("/connections/{accountId:guid}/history-sync", async (
            Guid workspaceId,
            Guid accountId,
            IConnectedAccountRepository accounts,
            EnsureConversationSyncUseCase ensure,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("Qasedak.Modules.Instagram.HistorySync");
            var account = await accounts.FindByIdAsync(accountId, cancellationToken);
            if (account is null || account.WorkspaceId != workspaceId)
            {
                // Foreign/absent account: repository convention — never an ownership leak.
                return Results.Json(
                    new { code = AccountFailures.NotFound },
                    statusCode: StatusCodes.Status404NotFound);
            }

            if (account.IsDisconnected)
            {
                return Results.Json(
                    new { code = AccountFailures.AlreadyDisconnected },
                    statusCode: StatusCodes.Status409Conflict);
            }

            // Coalescing: one active operation per account + kind; a duplicate request
            // returns the existing operation instead of starting a second traversal.
            var result = await ensure.ExecuteAsync(accountId, workspaceId, SyncOperationKind.Manual, cancellationToken);
            if (result.AlreadyActive)
            {
                LogCoalesced(logger, accountId, result.OperationId);
            }
            else
            {
                LogEnqueued(logger, accountId, result.OperationId);
            }

            return Results.Json(
                new
                {
                    operationId = result.OperationId,
                    alreadyActive = result.AlreadyActive,
                },
                statusCode: StatusCodes.Status202Accepted);
        });

        group.MapGet("/connections/{accountId:guid}/history-sync", async (
            Guid workspaceId,
            Guid accountId,
            IConnectedAccountRepository accounts,
            IProviderSyncOperationStore operations,
            CancellationToken cancellationToken) =>
        {
            var account = await accounts.FindByIdAsync(accountId, cancellationToken);
            if (account is null || account.WorkspaceId != workspaceId)
            {
                return Results.Json(
                    new { code = AccountFailures.NotFound },
                    statusCode: StatusCodes.Status404NotFound);
            }

            var recent = await operations.ListRecentAsync(accountId, 10, cancellationToken);
            return Results.Ok(new
            {
                operations = recent.Select(o => new
                {
                    operationId = o.OperationId,
                    kind = o.Kind.ToString(),
                    status = o.Status.ToString(),
                    stage = o.Stage,
                    conversationsObserved = o.ConversationsObserved,
                    messageIdsObserved = o.MessageIdsObserved,
                    detailsFetched = o.DetailsFetched,
                    messagesImported = o.MessagesImported,
                    duplicates = o.Duplicates,
                    unsupported = o.Unsupported,
                    historyWindowLimited = o.HistoryWindowLimited,
                    failureCategory = o.FailureCategory,
                    startedAtUtc = o.StartedAtUtc,
                    completedAtUtc = o.CompletedAtUtc,
                    createdAtUtc = o.CreatedAtUtc,
                }),
            });
        });

        return endpoints;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Manual history sync enqueued account={AccountId} operation={OperationId}.")]
    private static partial void LogEnqueued(ILogger logger, Guid accountId, Guid operationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Manual history sync coalesced onto existing active operation account={AccountId} operation={OperationId}.")]
    private static partial void LogCoalesced(ILogger logger, Guid accountId, Guid operationId);
}
