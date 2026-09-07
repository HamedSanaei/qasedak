using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Instagram.Application.HistorySync;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;

namespace Qasedak.Modules.Instagram.Infrastructure.HistorySync;

/// <summary>
/// Instagram-owned sync-operation persistence. One active (Queued/Running) operation
/// per (ConnectedAccountId, Kind) is enforced by a partial unique index in
/// PostgreSQL — concurrent manual/initial requests coalesce at the database instead
/// of racing. All transitions are guarded single-row updates so two workers can never
/// double-complete or double-start a stage.
/// </summary>
public sealed class EfProviderSyncOperationStore(InstagramDbContext context) : IProviderSyncOperationStore
{
    public async Task<ProviderSyncOperation?> CreateOrGetActiveAsync(
        Guid operationId, Guid connectedAccountId, Guid workspaceId, SyncOperationKind kind,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var existing = await context.ProviderSyncOperations
            .FirstOrDefaultAsync(
                o => o.ConnectedAccountId == connectedAccountId && o.Kind == kind &&
                     (o.Status == SyncOperationStatus.Queued || o.Status == SyncOperationStatus.Running),
                cancellationToken);
        if (existing is not null)
        {
            return existing.ToContract();
        }

        context.ProviderSyncOperations.Add(new ProviderSyncOperationRow
        {
            OperationId = operationId,
            ConnectedAccountId = connectedAccountId,
            WorkspaceId = workspaceId,
            Kind = kind,
            Status = SyncOperationStatus.Queued,
            Stage = 0,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return new ProviderSyncOperation(
                operationId, connectedAccountId, workspaceId, kind, SyncOperationStatus.Queued, 0,
                0, 0, 0, 0, 0, 0, 0, 0, null, null, now, null, null, now);
        }
        catch (DbUpdateException)
        {
            // Coalescing race lost: the other request's operation wins; the caller
            // treats this as "already active".
            var winner = await context.ProviderSyncOperations
                .FirstOrDefaultAsync(
                    o => o.ConnectedAccountId == connectedAccountId && o.Kind == kind &&
                         (o.Status == SyncOperationStatus.Queued || o.Status == SyncOperationStatus.Running),
                    cancellationToken);
            return winner?.ToContract();
        }
    }

    public async Task<ProviderSyncOperation?> FindByIdAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        var row = await context.ProviderSyncOperations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.OperationId == operationId, cancellationToken);
        return row?.ToContract();
    }

    /// <summary>
    /// Atomically claims the stage: Queued → Running, or a stale Running row whose
    /// previous worker died/cancelled (M13-004 re-delivers only after the job lease
    /// expires, so a Running row seen here means the old worker is gone). Terminal
    /// operations return false and the caller replays them idempotently.
    /// </summary>
    public async Task<bool> TryStartAsync(Guid operationId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var updated = await context.ProviderSyncOperations
            .Where(o => o.OperationId == operationId &&
                        (o.Status == SyncOperationStatus.Queued || o.Status == SyncOperationStatus.Running))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(o => o.Status, SyncOperationStatus.Running)
                    .SetProperty(o => o.StartedAtUtc, now)
                    .SetProperty(o => o.UpdatedAtUtc, now),
                cancellationToken);
        return updated == 1;
    }

    public async Task CheckpointAsync(
        Guid operationId, int stage, string? nextCursor, ProviderSyncCounters counters,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await context.ProviderSyncOperations
            .Where(o => o.OperationId == operationId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(o => o.Stage, stage)
                    .SetProperty(o => o.NextProviderCursor, nextCursor)
                    .SetProperty(o => o.ConversationsObserved, counters.ConversationsObserved)
                    .SetProperty(o => o.MessageIdsObserved, counters.MessageIdsObserved)
                    .SetProperty(o => o.DetailsFetched, counters.DetailsFetched)
                    .SetProperty(o => o.MessagesImported, counters.MessagesImported)
                    .SetProperty(o => o.Duplicates, counters.Duplicates)
                    .SetProperty(o => o.Unsupported, counters.Unsupported)
                    .SetProperty(o => o.HistoryWindowLimited, counters.HistoryWindowLimited)
                    .SetProperty(o => o.RateLimited, counters.RateLimited)
                    .SetProperty(o => o.UpdatedAtUtc, now),
                cancellationToken);
    }

    public async Task CompleteAsync(Guid operationId, ProviderSyncCounters counters, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await context.ProviderSyncOperations
            .Where(o => o.OperationId == operationId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(o => o.Status, SyncOperationStatus.CompletedWithinProviderLimits)
                    .SetProperty(o => o.CompletedAtUtc, now)
                    .SetProperty(o => o.NextProviderCursor, (string?)null)
                    .SetProperty(o => o.ConversationsObserved, counters.ConversationsObserved)
                    .SetProperty(o => o.MessageIdsObserved, counters.MessageIdsObserved)
                    .SetProperty(o => o.DetailsFetched, counters.DetailsFetched)
                    .SetProperty(o => o.MessagesImported, counters.MessagesImported)
                    .SetProperty(o => o.Duplicates, counters.Duplicates)
                    .SetProperty(o => o.Unsupported, counters.Unsupported)
                    .SetProperty(o => o.HistoryWindowLimited, counters.HistoryWindowLimited)
                    .SetProperty(o => o.RateLimited, counters.RateLimited)
                    .SetProperty(o => o.UpdatedAtUtc, now),
                cancellationToken);
    }

    public async Task FailAsync(
        Guid operationId, string failureCategory, bool transient, ProviderSyncCounters counters,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await context.ProviderSyncOperations
            .Where(o => o.OperationId == operationId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(o => o.Status, transient ? SyncOperationStatus.RateLimitedRetrying : SyncOperationStatus.Failed)
                    .SetProperty(o => o.FailureCategory, failureCategory)
                    .SetProperty(o => o.ConversationsObserved, counters.ConversationsObserved)
                    .SetProperty(o => o.MessageIdsObserved, counters.MessageIdsObserved)
                    .SetProperty(o => o.DetailsFetched, counters.DetailsFetched)
                    .SetProperty(o => o.MessagesImported, counters.MessagesImported)
                    .SetProperty(o => o.Duplicates, counters.Duplicates)
                    .SetProperty(o => o.Unsupported, counters.Unsupported)
                    .SetProperty(o => o.HistoryWindowLimited, counters.HistoryWindowLimited)
                    .SetProperty(o => o.RateLimited, counters.RateLimited)
                    .SetProperty(o => o.UpdatedAtUtc, now),
                cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderSyncOperation>> ListRecentAsync(Guid connectedAccountId, int limit, CancellationToken cancellationToken = default)
    {
        var rows = await context.ProviderSyncOperations
            .AsNoTracking()
            .Where(o => o.ConnectedAccountId == connectedAccountId)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return rows.Select(r => r.ToContract()).ToList();
    }

    private Task<bool> ActiveExistsAsync(Guid connectedAccountId, SyncOperationKind kind, CancellationToken cancellationToken) =>
        context.ProviderSyncOperations
            .AnyAsync(
                o => o.ConnectedAccountId == connectedAccountId && o.Kind == kind &&
                     (o.Status == SyncOperationStatus.Queued || o.Status == SyncOperationStatus.Running),
                cancellationToken);
}
