using Microsoft.Extensions.Logging;
using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Application.Scheduling;
using Qasedak.Modules.Instagram.Application.Accounts;

namespace Qasedak.Modules.Instagram.Infrastructure.Refresh;

/// <summary>
/// First production scheduled-work handler (M13-005): Instagram token refresh.
/// Payload carries only the connected account id (never token material). Flow per
/// execution: parse payload → exact-account refresh use case → on rotation enqueue
/// exactly one next occurrence (idempotency-keyed, so redelivery cannot duplicate)
/// → settle. Stale concurrent losers retry boundedly; redelivery after a crash is
/// safe because rotation is compare-and-swapped and next-occurrence enqueue dedupes.
/// No account id, workspace id, token or username ever enters logs.
/// </summary>
public sealed partial class TokenRefreshScheduledHandler(
    RefreshInstagramTokenUseCase refresh,
    IScheduledWorkStore scheduledWork,
    IClock clock,
    ILogger<TokenRefreshScheduledHandler> logger) : IScheduledWorkHandler
{
    public string WorkType => TokenRefreshPolicy.JobType;

    public async Task<WorkOutcome> HandleAsync(ScheduledWorkItem item, CancellationToken cancellationToken)
    {
        var accountId = TokenRefreshPolicy.ParseRefreshPayload(item.PayloadJson);
        if (accountId is null)
        {
            LogMalformedPayload(item.Id);
            return new WorkOutcome.Permanent("refresh.malformedPayload");
        }

        var outcome = await refresh.ExecuteAsync(accountId.Value, cancellationToken);
        switch (outcome)
        {
            case TokenRefreshOutcome.Rotated rotated:
                await scheduledWork.EnqueueAsync(
                    new ScheduledWorkEnqueue(
                        TokenRefreshPolicy.JobType,
                        TokenRefreshPolicy.IdempotencyKey(accountId.Value, rotated.NewExpiryUtc),
                        TokenRefreshPolicy.RefreshPayload(accountId.Value),
                        PayloadVersion: 1,
                        ConnectedAccountId: accountId.Value,
                        WorkspaceId: item.WorkspaceId,
                        DueAtUtc: TokenRefreshPolicy.NextDueAt(rotated.NewExpiryUtc, clock.UtcNow),
                        MaxAttempts: TokenRefreshPolicy.DefaultMaxAttempts),
                    clock.UtcNow,
                    cancellationToken);
                LogRotated(item.Id);
                return WorkOutcome.Succeeded.Instance;

            case TokenRefreshOutcome.Skipped skipped:
                LogSkipped(item.Id, skipped.FailureCode);
                return new WorkOutcome.Permanent(skipped.FailureCode);

            case TokenRefreshOutcome.Stale:
                LogStale(item.Id);
                return new WorkOutcome.Retryable("refresh.concurrentRotation");

            case TokenRefreshOutcome.Retryable retryable:
                return new WorkOutcome.Retryable(retryable.FailureCode);

            case TokenRefreshOutcome.Permanent permanent:
                LogPermanent(item.Id, permanent.FailureCode);
                return new WorkOutcome.Permanent(permanent.FailureCode);

            default:
                return new WorkOutcome.Permanent("refresh.unknownOutcome");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Token refresh rotated job={JobId}.")]
    private partial void LogRotated(Guid jobId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Token refresh skipped job={JobId} reason={Reason}.")]
    private partial void LogSkipped(Guid jobId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Token refresh lost a concurrent rotation job={JobId}; retrying boundedly.")]
    private partial void LogStale(Guid jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Token refresh permanent failure job={JobId} reason={Reason}.")]
    private partial void LogPermanent(Guid jobId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Token refresh payload malformed job={JobId}.")]
    private partial void LogMalformedPayload(Guid jobId);
}
