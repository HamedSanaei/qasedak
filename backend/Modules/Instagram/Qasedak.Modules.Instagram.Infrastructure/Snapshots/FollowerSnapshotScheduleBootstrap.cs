using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Application.Scheduling;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.FollowerSnapshots;
using Qasedak.Modules.Instagram.Application.Insights;

namespace Qasedak.Modules.Instagram.Infrastructure.Snapshots;

/// <summary>
/// M13-007 production-hardening bootstrap: on every host start, ensures ACTIVE
/// accounts that predate M13-007 (or whose daily chain has a gap) acquire today's
/// follower-snapshot occurrence. Idempotent per (account, UTC day) idempotency key, so
/// multiple instances/restarts collapse onto one logical job; bounded per run
/// (<see cref="InsightsPolicy.MaxBootstrapAccountsPerRun"/>). DB-only: no provider
/// call, no token read, no lock held during network I/O — and it never fabricates
/// jobs for disconnected accounts. Newly connected accounts are scheduled at connect
/// time; the handler chains each settled day to the next.
/// </summary>
public sealed partial class FollowerSnapshotScheduleBootstrap(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<FollowerSnapshotScheduleBootstrap> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var accounts = scope.ServiceProvider.GetRequiredService<IConnectedAccountRepository>();
            var scheduledWork = scope.ServiceProvider.GetRequiredService<IScheduledWorkStore>();
            var snapshots = scope.ServiceProvider.GetRequiredService<IFollowerSnapshotStore>();

            var today = FollowerSnapshotPolicy.UtcDay(clock.UtcNow);
            var active = accounts.ListActiveAsync(InsightsPolicy.MaxBootstrapAccountsPerRun, cancellationToken).GetAwaiter().GetResult();
            var enqueued = 0;
            foreach (var account in active)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (snapshots.ExistsForDayAsync(account.Id, today, cancellationToken).GetAwaiter().GetResult())
                {
                    continue;
                }

                scheduledWork.EnqueueAsync(
                    new ScheduledWorkEnqueue(
                        FollowerSnapshotPolicy.JobType,
                        FollowerSnapshotPolicy.IdempotencyKey(account.Id, today),
                        FollowerSnapshotPolicy.Payload(account.Id, today),
                        PayloadVersion: 1,
                        ConnectedAccountId: account.Id,
                        WorkspaceId: account.WorkspaceId,
                        DueAtUtc: clock.UtcNow,
                        MaxAttempts: FollowerSnapshotPolicy.DefaultMaxAttempts),
                    clock.UtcNow,
                    cancellationToken).GetAwaiter().GetResult();
                enqueued++;
            }

            LogBootstrapResult(active.Count, enqueued);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Startup must never fail because snapshot scheduling could not be ensured:
            // accounts already scheduled keep their chains; the next host start retries.
            LogBootstrapFailed(exception.GetType().Name);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Follower snapshot bootstrap: active={Active} enqueuedToday={Enqueued}.")]
    private partial void LogBootstrapResult(int active, int enqueued);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Follower snapshot bootstrap failed ({Failure}); next host start retries.")]
    private partial void LogBootstrapFailed(string failure);
}
