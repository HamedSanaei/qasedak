using Microsoft.Extensions.Logging;
using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Application.Scheduling;
using Qasedak.Modules.Instagram.Application.FollowerSnapshots;
using Qasedak.Modules.Instagram.Infrastructure.Insights;

namespace Qasedak.Modules.Instagram.Infrastructure.Snapshots;

/// <summary>
/// M13-007 daily follower snapshot handler on the M13-004 scheduled-work mechanism.
/// Payload carries identifiers only (ConnectedAccountId + snapshot UTC day); the
/// protected token is resolved at execution time.
///
/// Outcome mapping:
/// <list type="bullet">
/// <item>Observed → snapshot upserted (account/day uniqueness + provenance precedence
/// are PostgreSQL-enforced) → exactly one next-day occurrence enqueued (occurrence-
/// specific idempotency key) → Succeeded.</item>
/// <item>NoData (provider answered without a value) → occurrence settled WITHOUT a
/// fabricated row; next-day schedule continues at bounded 1/day frequency → Succeeded.</item>
/// <item>Retryable (rate limit/5xx/transport) → nothing persisted; next-day cadence is
/// still chained (idempotent); THIS occurrence reports Retryable for M13-004 backoff.</item>
/// <item>AccountTerminal (unknown/disconnected/missing token/authentication/basic
/// permission loss) → zero provider retry for the occurrence, no snapshot, no chain:
/// account-health machinery (M13-005) owns the account.</item>
/// </list>
/// Delivery is at-least-once; external effects are idempotent by key + conditional
/// upsert — exactly-once execution is never claimed.
/// </summary>
public sealed partial class FollowerSnapshotScheduledHandler(
    FollowerSnapshotUseCase snapshots,
    IScheduledWorkStore scheduledWork,
    IClock clock,
    InsightsMetrics metrics,
    ILogger<FollowerSnapshotScheduledHandler> logger) : IScheduledWorkHandler
{
    public string WorkType => FollowerSnapshotPolicy.JobType;

    public async Task<WorkOutcome> HandleAsync(ScheduledWorkItem item, CancellationToken cancellationToken)
    {
        var parsed = FollowerSnapshotPolicy.ParsePayload(item.PayloadJson);
        if (parsed is null)
        {
            LogMalformedPayload(item.Id);
            metrics.SnapshotOutcomes.Add(1, new KeyValuePair<string, object?>("outcome", "malformedPayload"));
            return new WorkOutcome.Permanent("followers.malformedPayload");
        }

        var (accountId, snapshotDateUtc) = parsed.Value;
        var outcome = await snapshots.ExecuteAsync(accountId, snapshotDateUtc, cancellationToken);
        switch (outcome)
        {
            case FollowerSnapshotOutcome.Observed observed:
                await ChainNextOccurrenceAsync(accountId, snapshotDateUtc, item, cancellationToken);
                LogObserved(item.Id, observed.Persisted);
                metrics.SnapshotOutcomes.Add(1, new KeyValuePair<string, object?>("outcome", observed.Persisted ? "observed" : "observedExisting"));
                return WorkOutcome.Succeeded.Instance;

            case FollowerSnapshotOutcome.NoData noData:
                // Settled for the day: no fabricated row; bounded next-day schedule
                // continues so a restored value is detected at 1/day frequency.
                await ChainNextOccurrenceAsync(accountId, snapshotDateUtc, item, cancellationToken);
                LogNoData(item.Id, noData.FailureCode);
                metrics.SnapshotOutcomes.Add(1, new KeyValuePair<string, object?>("outcome", "noData"));
                return WorkOutcome.Succeeded.Instance;

            case FollowerSnapshotOutcome.Retryable retryable:
                // Keep the daily cadence alive while this occurrence backs off: the
                // next-day key is idempotent, so redelivery can never duplicate it.
                await ChainNextOccurrenceAsync(accountId, snapshotDateUtc, item, cancellationToken);
                LogRetry(item.Id, retryable.FailureCode);
                metrics.SnapshotOutcomes.Add(1, new KeyValuePair<string, object?>("outcome", "retry"));
                return new WorkOutcome.Retryable(retryable.FailureCode);

            case FollowerSnapshotOutcome.AccountTerminal terminal:
                LogTerminal(item.Id, terminal.FailureCode);
                metrics.SnapshotOutcomes.Add(1, new KeyValuePair<string, object?>("outcome", "accountTerminal"));
                return new WorkOutcome.Permanent(terminal.FailureCode);

            default:
                metrics.SnapshotOutcomes.Add(1, new KeyValuePair<string, object?>("outcome", "unknownOutcome"));
                return new WorkOutcome.Permanent("followers.unknownOutcome");
        }
    }

    private async Task ChainNextOccurrenceAsync(
        Guid accountId, DateOnly snapshotDateUtc, ScheduledWorkItem item, CancellationToken cancellationToken)
    {
        var nextDay = snapshotDateUtc.AddDays(1);
        await scheduledWork.EnqueueAsync(
            new ScheduledWorkEnqueue(
                FollowerSnapshotPolicy.JobType,
                FollowerSnapshotPolicy.IdempotencyKey(accountId, nextDay),
                FollowerSnapshotPolicy.Payload(accountId, nextDay),
                PayloadVersion: 1,
                ConnectedAccountId: accountId,
                WorkspaceId: item.WorkspaceId,
                DueAtUtc: FollowerSnapshotPolicy.NextOccurrenceDueAt(nextDay),
                MaxAttempts: FollowerSnapshotPolicy.DefaultMaxAttempts),
            clock.UtcNow,
            cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Follower snapshot observed job={JobId} persisted={Persisted}.")]
    private partial void LogObserved(Guid jobId, bool persisted);

    [LoggerMessage(Level = LogLevel.Information, Message = "Follower snapshot no-data job={JobId} reason={Reason}.")]
    private partial void LogNoData(Guid jobId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Follower snapshot retryable job={JobId} reason={Reason}.")]
    private partial void LogRetry(Guid jobId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Follower snapshot account-terminal job={JobId} reason={Reason}.")]
    private partial void LogTerminal(Guid jobId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Follower snapshot payload malformed job={JobId}.")]
    private partial void LogMalformedPayload(Guid jobId);
}
