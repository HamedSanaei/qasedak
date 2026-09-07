using Microsoft.Extensions.Logging;
using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Application.Scheduling;
using Qasedak.Modules.Instagram.Application.HistorySync;

namespace Qasedak.Modules.Instagram.Infrastructure.HistorySync;

/// <summary>
/// M13-013 Phase B durable sync handler on the M13-004 mechanism. Payload carries
/// local identifiers only (OperationId + ConnectedAccountId); the protected token is
/// resolved at execution time. One logical job per operation (idempotency keyed);
/// continuation jobs resume from the operation checkpoint with a deterministic
/// stage key — at-least-once delivery can never run two identical full traversals.
/// Rate-limited/transient stages report Retryable (M13-004 backoff) and the
/// operation is marked RateLimitedRetrying; permission/auth/account-terminal stages
/// are permanent (no tight loop); the DB-only bootstrap or a fresh manual resync
/// re-establishes later. Cancellation is not a provider failure.
/// </summary>
public sealed partial class ConversationHistorySyncScheduledHandler(
    ConversationHistorySyncUseCase sync,
    IScheduledWorkStore scheduledWork,
    IClock clock,
    ConversationSyncMetrics metrics,
    ILogger<ConversationHistorySyncScheduledHandler> logger) : IScheduledWorkHandler
{
    public string WorkType => ConversationHistoryPolicy.JobType;

    public async Task<WorkOutcome> HandleAsync(ScheduledWorkItem item, CancellationToken cancellationToken)
    {
        var parsed = ConversationHistoryPolicy.ParsePayload(item.PayloadJson);
        if (parsed is null)
        {
            LogMalformed(item.Id);
            return new WorkOutcome.Permanent("convsync.malformedPayload");
        }

        var (operationId, accountId) = parsed.Value;
        var stage = await sync.ExecuteAsync(operationId, cancellationToken);
        metrics.Messages(stage.Counters.MessagesImported, stage.Counters.Duplicates, stage.Counters.Unsupported, stage.Counters.HistoryWindowLimited);

        switch (stage.Status)
        {
            case SyncOperationStatus.CompletedWithinProviderLimits:
                metrics.OperationCompleted("sync", "completedWithinProviderLimits", stage.Counters.ConversationsObserved, stage.Counters.DetailsFetched);
                LogCompleted(item.Id, operationId);
                return WorkOutcome.Succeeded.Instance;

            case SyncOperationStatus.Failed:
                metrics.OperationCompleted("sync", "failed", stage.Counters.ConversationsObserved, stage.Counters.DetailsFetched);
                LogFailed(item.Id, operationId, stage.FailureCode ?? "unknown");
                return new WorkOutcome.Permanent(stage.FailureCode ?? "convsync.failed");

            case SyncOperationStatus.RateLimitedRetrying:
                metrics.OperationCompleted("sync", "rateLimitedRetrying", stage.Counters.ConversationsObserved, stage.Counters.DetailsFetched);
                LogRetrying(item.Id, operationId, stage.FailureCode ?? "unknown");
                return new WorkOutcome.Retryable(stage.FailureCode ?? ConversationHistoryFailures.RateLimited);

            case SyncOperationStatus.Running when stage.HasMoreWork:
                // Budget exhausted: enqueue the deterministic continuation occurrence.
                await scheduledWork.EnqueueAsync(
                    new ScheduledWorkEnqueue(
                        ConversationHistoryPolicy.JobType,
                        ConversationHistoryPolicy.ContinuationIdempotencyKey(operationId, stage.NextStage),
                        ConversationHistoryPolicy.Payload(operationId, accountId),
                        PayloadVersion: ConversationHistoryPolicy.PayloadVersion,
                        ConnectedAccountId: accountId,
                        WorkspaceId: item.WorkspaceId,
                        DueAtUtc: clock.UtcNow,
                        MaxAttempts: ConversationHistoryPolicy.DefaultMaxAttempts),
                    clock.UtcNow,
                    cancellationToken);
                LogContinued(item.Id, operationId);
                return WorkOutcome.Succeeded.Instance;

            default:
                // Idle replay of a settled operation or a stage held by another worker.
                return WorkOutcome.Succeeded.Instance;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Conversation history sync completed job={JobId} operation={OperationId}.")]
    private partial void LogCompleted(Guid jobId, Guid operationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Conversation history sync continued job={JobId} operation={OperationId}.")]
    private partial void LogContinued(Guid jobId, Guid operationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Conversation history sync failed job={JobId} operation={OperationId} reason={Reason}.")]
    private partial void LogFailed(Guid jobId, Guid operationId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Conversation history sync retrying job={JobId} operation={OperationId} reason={Reason}.")]
    private partial void LogRetrying(Guid jobId, Guid operationId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Conversation history sync payload malformed job={JobId}.")]
    private partial void LogMalformed(Guid jobId);
}
