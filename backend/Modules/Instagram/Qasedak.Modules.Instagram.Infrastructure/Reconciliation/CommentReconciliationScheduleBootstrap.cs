using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Application.Scheduling;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.Reconciliation;

namespace Qasedak.Modules.Instagram.Infrastructure.Reconciliation;

/// <summary>
/// M13-013 Phase A startup bootstrap: on every host start, ensures ACTIVE accounts
/// (including accounts that predate M13-013) acquire the recurring comment-
/// reconciliation chain. Occurrence-specific idempotency keys make the ensure
/// idempotent across instances/restarts; bounded per run
/// (<see cref="CommentReconciliationPolicy"/> constants). DB-only: zero provider
/// calls, zero token reads, no lock held during network I/O, and it never schedules
/// disconnected accounts. Newly connected accounts are chained at connect time;
/// each settled sweep chains its own next occurrence.
/// </summary>
public sealed partial class CommentReconciliationScheduleBootstrap(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<CommentReconciliationScheduleBootstrap> logger) : IHostedService
{
    /// <summary>Bounded number of accounts ensured per bootstrap invocation (anti-thundering-herd).</summary>
    public const int MaxAccountsPerRun = 25;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var accounts = scope.ServiceProvider.GetRequiredService<IConnectedAccountRepository>();
            var scheduledWork = scope.ServiceProvider.GetRequiredService<IScheduledWorkStore>();

            var active = accounts.ListActiveAsync(MaxAccountsPerRun, cancellationToken).GetAwaiter().GetResult();
            var dueAtUtc = clock.UtcNow;
            var enqueued = 0;
            foreach (var account in active)
            {
                cancellationToken.ThrowIfCancellationRequested();
                scheduledWork.EnqueueAsync(
                    new ScheduledWorkEnqueue(
                        CommentReconciliationPolicy.JobType,
                        CommentReconciliationPolicy.IdempotencyKey(account.Id, dueAtUtc),
                        CommentReconciliationPolicy.Payload(account.Id),
                        PayloadVersion: CommentReconciliationPolicy.PayloadVersion,
                        ConnectedAccountId: account.Id,
                        WorkspaceId: account.WorkspaceId,
                        DueAtUtc: dueAtUtc,
                        MaxAttempts: CommentReconciliationPolicy.DefaultMaxAttempts),
                    clock.UtcNow,
                    cancellationToken).GetAwaiter().GetResult();
                enqueued++;
            }

            LogBootstrapResult(active.Count, enqueued);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Startup must never fail because reconciliation scheduling could not be
            // ensured: existing chains keep running; the next host start retries.
            LogBootstrapFailed(exception.GetType().Name);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Comment reconciliation bootstrap: active={Active} enqueued={Enqueued}.")]
    private partial void LogBootstrapResult(int active, int enqueued);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Comment reconciliation bootstrap failed ({Failure}); next host start retries.")]
    private partial void LogBootstrapFailed(string failure);
}
