using Microsoft.Extensions.Logging;
using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Application.Scheduling;
using Qasedak.Modules.Instagram.Application.Reconciliation;

namespace Qasedak.Modules.Instagram.Infrastructure.Reconciliation;

/// <summary>
/// M13-013 Phase A recurring sweep handler on the M13-004 scheduled-work mechanism.
/// Payload carries identifiers only (ConnectedAccountId); the protected token is
/// resolved at execution time by the use case. One logical recurring chain per
/// eligible active account; the occurrence-specific idempotency key collapses
/// restart/duplicate enqueues onto one job and the next occurrence is chained after
/// settlement — at-least-once delivery never produces overlapping sweeps for the
/// same account.
///
/// Chaining policy: success, idle (no active comment automation) and rate-limited
/// sweeps keep the chain alive (the idle occurrence proves the zero-traffic
/// invariant each cycle). PERMANENT outcomes (disconnected account, missing token,
/// permission/auth loss) do NOT chain — the DB-only bootstrap re-establishes the
/// chain on the next host start; no tight loop, no endless pointless occurrence.
/// Cancellation is never classified as a provider failure.
/// </summary>
public sealed partial class CommentReconciliationScheduledHandler(
    ICommentSweep sweep,
    IScheduledWorkStore scheduledWork,
    IClock clock,
    CommentReconciliationMetrics metrics,
    ILogger<CommentReconciliationScheduledHandler> logger) : IScheduledWorkHandler
{
    public string WorkType => CommentReconciliationPolicy.JobType;

    public async Task<WorkOutcome> HandleAsync(ScheduledWorkItem item, CancellationToken cancellationToken)
    {
        var accountId = CommentReconciliationPolicy.ParseAccountId(item.PayloadJson);
        if (accountId is null)
        {
            LogMalformed(item.Id);
            return new WorkOutcome.Permanent("commentRecon.malformedPayload");
        }

        var outcome = await sweep.ExecuteAsync(accountId.Value, cancellationToken);

        if (outcome.FailureCode is not null)
        {
            LogFailure(item.Id, outcome.FailureCode);
            metrics.SweepCompleted(outcome.FailureCode, outcome.MediaScanned, outcome.CommentPagesScanned);
            metrics.Comments(outcome.CommentsDispatched, outcome.SelfSkipped, outcome.UnknownAuthorSkipped);
            if (outcome.RateLimited)
            {
                // Transient: keep the chain alive while this occurrence backs off.
                await ChainNextOccurrenceAsync(accountId.Value, item, cancellationToken);
                return new WorkOutcome.Retryable(outcome.FailureCode);
            }

            // Permanent: no chain; the next host-start bootstrap re-establishes it.
            return new WorkOutcome.Permanent(outcome.FailureCode);
        }

        await ChainNextOccurrenceAsync(accountId.Value, item, cancellationToken);
        metrics.SweepCompleted(outcome.ProviderTrafficAttempted ? "completed" : "idle", outcome.MediaScanned, outcome.CommentPagesScanned);
        metrics.Comments(outcome.CommentsDispatched, outcome.SelfSkipped, outcome.UnknownAuthorSkipped);
        LogSweep(item.Id, outcome.CommentsDispatched, outcome.MediaScanned, outcome.CommentPagesScanned);
        return WorkOutcome.Succeeded.Instance;
    }

    private async Task ChainNextOccurrenceAsync(Guid accountId, ScheduledWorkItem item, CancellationToken cancellationToken)
    {
        var nextDueAt = CommentReconciliationPolicy.NextOccurrenceDueAt(clock.UtcNow);
        await scheduledWork.EnqueueAsync(
            new ScheduledWorkEnqueue(
                CommentReconciliationPolicy.JobType,
                CommentReconciliationPolicy.IdempotencyKey(accountId, nextDueAt),
                CommentReconciliationPolicy.Payload(accountId),
                PayloadVersion: CommentReconciliationPolicy.PayloadVersion,
                ConnectedAccountId: accountId,
                WorkspaceId: item.WorkspaceId,
                DueAtUtc: nextDueAt,
                MaxAttempts: CommentReconciliationPolicy.DefaultMaxAttempts),
            clock.UtcNow,
            cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Comment reconciliation sweep job={JobId} dispatched={Dispatched} media={Media} pages={Pages}.")]
    private partial void LogSweep(Guid jobId, int dispatched, int media, int pages);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Comment reconciliation sweep job={JobId} failure={Failure}.")]
    private partial void LogFailure(Guid jobId, string failure);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Comment reconciliation payload malformed job={JobId}.")]
    private partial void LogMalformed(Guid jobId);
}
