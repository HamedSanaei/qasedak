using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Application.Scheduling;

namespace Qasedak.Modules.Instagram.Application.HistorySync;

/// <summary>
/// Ensures one durable conversation-history sync operation for an exact connected
/// account (M13-013 §67-71): creates-or-coalesces the operation row (one active per
/// account/kind, DB-enforced) and enqueues the M13-004 job. Never performs provider
/// I/O — the scheduled worker does that. Used by the connection lifecycle (initial
/// sync), the startup bootstrap (existing accounts) and the manual resync endpoint.
/// </summary>
public sealed class EnsureConversationSyncUseCase(
    IProviderSyncOperationStore operations,
    IScheduledWorkStore scheduledWork,
    IClock clock)
{
    public sealed record EnsureSyncResult(Guid OperationId, bool AlreadyActive);

    public async Task<EnsureSyncResult> ExecuteAsync(
        Guid connectedAccountId, Guid workspaceId, SyncOperationKind kind, CancellationToken cancellationToken = default)
    {
        var operation = await operations.CreateOrGetActiveAsync(
            Guid.CreateVersion7(), connectedAccountId, workspaceId, kind, clock.UtcNow, cancellationToken);
        if (operation is null)
        {
            // Coalescing race: reload once; a fresh create retries on the next call.
            throw new InvalidOperationException("convsync.operationUnavailable");
        }

        var alreadyActive = operation.Status is SyncOperationStatus.Queued or SyncOperationStatus.Running;
        await scheduledWork.EnqueueAsync(
            new ScheduledWorkEnqueue(
                ConversationHistoryPolicy.JobType,
                ConversationHistoryPolicy.OperationIdempotencyKey(operation.OperationId),
                ConversationHistoryPolicy.Payload(operation.OperationId, connectedAccountId),
                PayloadVersion: ConversationHistoryPolicy.PayloadVersion,
                ConnectedAccountId: connectedAccountId,
                WorkspaceId: workspaceId,
                DueAtUtc: clock.UtcNow,
                MaxAttempts: ConversationHistoryPolicy.DefaultMaxAttempts),
            clock.UtcNow,
            cancellationToken);

        return new EnsureSyncResult(operation.OperationId, alreadyActive);
    }
}
