using Microsoft.EntityFrameworkCore;
using Qasedak.BuildingBlocks.Application.Scheduling;

namespace Qasedak.BuildingBlocks.Infrastructure.Scheduling;

/// <summary>
/// Durable scheduled-work store over real PostgreSQL. Concurrency safety comes from
/// the database, never from application checks: the idempotency unique index makes
/// enqueue race-safe, and claiming is one UPDATE..RETURNING statement so two workers
/// can never hold the same record. Callers pass time explicitly (no clock inside the
/// store) so every transition stays deterministic and testable.
/// </summary>
public sealed class EfScheduledWorkStore(ScheduledWorkDbContext context) : IScheduledWorkStore
{
    public async Task<(ScheduledWorkItem Item, bool Duplicate)> EnqueueAsync(
        ScheduledWorkEnqueue request, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkType);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);
        ScheduledWorkPayloadGuard.ThrowIfSuspicious(request.PayloadJson);

        var row = new ScheduledWorkRow
        {
            Id = Guid.CreateVersion7(),
            WorkType = request.WorkType.Trim(),
            IdempotencyKey = request.IdempotencyKey.Trim(),
            PayloadJson = request.PayloadJson,
            PayloadVersion = request.PayloadVersion,
            ConnectedAccountId = request.ConnectedAccountId,
            WorkspaceId = request.WorkspaceId,
            DueAtUtc = request.DueAtUtc,
            Status = ScheduledWorkStatus.Pending,
            Attempts = 0,
            MaxAttempts = Math.Max(1, request.MaxAttempts),
            NextAttemptAtUtc = request.DueAtUtc,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        context.Jobs.Add(row);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // A concurrent worker won the idempotency slot: one logical job survives.
            context.ChangeTracker.Clear();
            var existing = await context.Jobs.AsNoTracking()
                .SingleAsync(r => r.IdempotencyKey == row.IdempotencyKey, cancellationToken);
            return (ToItem(existing), true);
        }

        return (ToItem(row), false);
    }

    public async Task<IReadOnlyList<ScheduledWorkItem>> ClaimDueAsync(
        string leaseOwner, int batchSize, TimeSpan leaseDuration, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        // Single statement: pending past-due rows plus expired leases, oldest first.
        // Only one worker can win each row; losers simply see fewer rows. Status codes
        // are passed as parameters (never literals) so enum order stays free to evolve.
        var leaseExpires = now.Add(leaseDuration);
        var pending = (int)ScheduledWorkStatus.Pending;
        var claimed = (int)ScheduledWorkStatus.Claimed;
        var rows = await context.Jobs.FromSqlRaw(
            """
            UPDATE platform.scheduled_jobs AS job SET
                "Status" = {6},
                "Attempts" = job."Attempts" + 1,
                "LeaseOwner" = {0},
                "LeaseExpiresAtUtc" = {1},
                "UpdatedAtUtc" = {2},
                "StartedAtUtc" = COALESCE(job."StartedAtUtc", {2})
            WHERE job."Id" IN (
                SELECT "Id" FROM platform.scheduled_jobs
                WHERE (("Status" = {5} AND "NextAttemptAtUtc" <= {3})
                    OR ("Status" = {6} AND "LeaseExpiresAtUtc" IS NOT NULL AND "LeaseExpiresAtUtc" <= {3}))
                ORDER BY "NextAttemptAtUtc" ASC
                LIMIT {4}
                FOR UPDATE SKIP LOCKED
            )
            RETURNING job.*
            """,
            leaseOwner, leaseExpires, now, now, Math.Max(1, batchSize), pending, claimed)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return rows.Select(ToItem).ToList();
    }

    public async Task<bool> RenewLeaseAsync(Guid id, string leaseOwner, TimeSpan leaseDuration, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var affected = await context.Jobs
            .Where(r => r.Id == id && r.Status == ScheduledWorkStatus.Claimed && r.LeaseOwner == leaseOwner)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(r => r.LeaseExpiresAtUtc, now.Add(leaseDuration))
                .SetProperty(r => r.UpdatedAtUtc, now), cancellationToken);
        return affected == 1;
    }

    public async Task CompleteAsync(Guid id, string leaseOwner, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var affected = await context.Jobs
            .Where(r => r.Id == id && r.Status == ScheduledWorkStatus.Claimed && r.LeaseOwner == leaseOwner)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(r => r.Status, ScheduledWorkStatus.Succeeded)
                .SetProperty(r => r.FinishedAtUtc, now)
                .SetProperty(r => r.UpdatedAtUtc, now), cancellationToken);
        if (affected != 1)
        {
            throw new ScheduledWorkException(ScheduledWorkFailures.LeaseLost, "Claim is no longer held by this worker.");
        }
    }

    public async Task FailAsync(Guid id, string leaseOwner, WorkOutcome outcome, DateTimeOffset nextAttemptAtUtc, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        // Fresh untracked read: the tracker may hold a stale copy written before the
        // raw-SQL claim updated the row.
        var row = await context.Jobs.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new ScheduledWorkException(ScheduledWorkFailures.LeaseLost, "Unknown scheduled-work record.");
        if (row.Status != ScheduledWorkStatus.Claimed || row.LeaseOwner != leaseOwner)
        {
            throw new ScheduledWorkException(ScheduledWorkFailures.LeaseLost, "Claim is no longer held by this worker.");
        }

        var (status, failureCode, finishedAtUtc, releaseLease) = outcome switch
        {
            WorkOutcome.Succeeded => (ScheduledWorkStatus.Succeeded, (string?)null, (DateTimeOffset?)now, false),
            WorkOutcome.Retryable retryable when row.Attempts < row.MaxAttempts =>
                (ScheduledWorkStatus.Pending, retryable.FailureCode, null, true),
            WorkOutcome.Retryable retryable =>
                (ScheduledWorkStatus.DeadLettered, retryable.FailureCode, (DateTimeOffset?)now, false),
            WorkOutcome.Permanent permanent =>
                (ScheduledWorkStatus.Failed, (string?)permanent.FailureCode, (DateTimeOffset?)now, false),
            WorkOutcome.DeadLetter deadLetter =>
                (ScheduledWorkStatus.DeadLettered, (string?)deadLetter.FailureCode, (DateTimeOffset?)now, false),
            _ => (ScheduledWorkStatus.DeadLettered, ScheduledWorkFailures.UnknownWorkType, (DateTimeOffset?)now, false),
        };

        var candidates = context.Jobs
            .Where(r => r.Id == id && r.Status == ScheduledWorkStatus.Claimed && r.LeaseOwner == leaseOwner);

        int affected;
        if (releaseLease)
        {
            affected = await candidates.ExecuteUpdateAsync(updates => updates
                .SetProperty(r => r.Status, status)
                .SetProperty(r => r.LastFailureCode, failureCode)
                .SetProperty(r => r.NextAttemptAtUtc, nextAttemptAtUtc)
                .SetProperty(r => r.LeaseOwner, (string?)null)
                .SetProperty(r => r.LeaseExpiresAtUtc, (DateTimeOffset?)null)
                .SetProperty(r => r.FinishedAtUtc, finishedAtUtc)
                .SetProperty(r => r.UpdatedAtUtc, now), cancellationToken);
        }
        else
        {
            affected = await candidates.ExecuteUpdateAsync(updates => updates
                .SetProperty(r => r.Status, status)
                .SetProperty(r => r.LastFailureCode, failureCode)
                .SetProperty(r => r.FinishedAtUtc, finishedAtUtc)
                .SetProperty(r => r.UpdatedAtUtc, now), cancellationToken);
        }

        if (affected != 1)
        {
            throw new ScheduledWorkException(ScheduledWorkFailures.LeaseLost, "Claim is no longer held by this worker.");
        }
    }

    public async Task CancelAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await context.Jobs
            .Where(r => r.Id == id && (r.Status == ScheduledWorkStatus.Pending || r.Status == ScheduledWorkStatus.Claimed))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(r => r.Status, ScheduledWorkStatus.Cancelled)
                .SetProperty(r => r.FinishedAtUtc, now)
                .SetProperty(r => r.UpdatedAtUtc, now), cancellationToken);
    }

    public async Task<ScheduledWorkItem?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var row = await context.Jobs.AsNoTracking().SingleOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);
        return row is null ? null : ToItem(row);
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        for (var current = (Exception?)exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("23505", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static ScheduledWorkItem ToItem(ScheduledWorkRow row) => new(
        row.Id, row.WorkType, row.IdempotencyKey, row.PayloadJson, row.PayloadVersion,
        row.ConnectedAccountId, row.WorkspaceId, row.DueAtUtc, row.Status, row.Attempts,
        row.MaxAttempts, row.NextAttemptAtUtc, row.LastFailureCode, row.LeaseOwner,
        row.LeaseExpiresAtUtc, row.CreatedAtUtc, row.UpdatedAtUtc, row.StartedAtUtc, row.FinishedAtUtc);
}
