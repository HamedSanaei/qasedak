using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Application.Scheduling;

namespace Qasedak.BuildingBlocks.Infrastructure.Scheduling;

/// <summary>Scheduled-work operational counters.</summary>
public sealed class ScheduledWorkMetrics
{
    public const string MeterName = "Qasedak.Platform.Scheduling";

    private readonly Counter<long> _claimed;
    private readonly Counter<long> _finished;
    private readonly Counter<long> _unknownType;

    public ScheduledWorkMetrics(IMeterFactory? meters = null)
    {
        var meter = meters?.Create(MeterName) ?? new Meter(MeterName);
        _claimed = meter.CreateCounter<long>("scheduled_work.claimed", description: "Records claimed by workers.");
        _finished = meter.CreateCounter<long>("scheduled_work.finished", description: "Records finished by terminal state.");
        _unknownType = meter.CreateCounter<long>("scheduled_work.unknown_type", description: "Claimed records with no registered handler.");
    }

    public void Claimed(int count) => _claimed.Add(count);

    public void Finished(string state) =>
        _finished.Add(1, new KeyValuePair<string, object?>("state", state));

    public void UnknownType(string workType) =>
        _unknownType.Add(1, new KeyValuePair<string, object?>("work_type", workType));
}

/// <summary>
/// Poll/claim/dispatch loop (M13-004 runtime): claims due records under one lease
/// owner per host, dispatches each to its module-owned handler resolved in a fresh
/// scope, and records the verdict. Unknown work types dead-letter without spinning.
/// A crashed host simply stops renewing: its records become claimable again after
/// lease expiry.
/// </summary>
public sealed partial class ScheduledWorkDispatcher(
    IServiceScopeFactory scopes,
    IOptions<ScheduledWorkOptions> options,
    IClock clock,
    ScheduledWorkMetrics metrics,
    ILogger<ScheduledWorkDispatcher> logger) : BackgroundService
{
    private readonly string _leaseOwner = "worker-" + Guid.CreateVersion7().ToString("N");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var poll = TimeSpan.FromSeconds(Math.Max(1, options.Value.PollIntervalSeconds));
        using var timer = new PeriodicTimer(poll);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogPollFailed(exception);
            }
        }
    }

    /// <summary>One poll cycle, exposed for deterministic tests without timers.</summary>
    public async Task<int> PollOnceAsync(CancellationToken cancellationToken = default)
    {
        // The store is scoped per cycle: a singleton worker must never hold a scoped
        // DbContext across cycles (captive dependency). Handler dispatch opens its own
        // nested scopes below.
        using var scope = scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IScheduledWorkStore>();
        var now = clock.UtcNow;
        var claimed = await store.ClaimDueAsync(
            _leaseOwner, options.Value.BatchSize, LeaseDuration(), now, cancellationToken);
        if (claimed.Count == 0)
        {
            return 0;
        }

        metrics.Claimed(claimed.Count);
        foreach (var item in claimed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DispatchOneAsync(store, item, cancellationToken);
        }

        return claimed.Count;
    }

    private async Task DispatchOneAsync(IScheduledWorkStore store, ScheduledWorkItem item, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var handler = scope.ServiceProvider
            .GetServices<IScheduledWorkHandler>()
            .FirstOrDefault(h => string.Equals(h.WorkType, item.WorkType, StringComparison.Ordinal));
        if (handler is null)
        {
            metrics.UnknownType(item.WorkType);
            await store.FailAsync(item.Id, _leaseOwner,
                new WorkOutcome.DeadLetter(ScheduledWorkFailures.UnknownWorkType),
                clock.UtcNow, clock.UtcNow, cancellationToken);
            metrics.Finished("deadlettered");
            LogUnknownType(item.WorkType, item.Id);
            return;
        }

        WorkOutcome outcome;
        try
        {
            outcome = await handler.HandleAsync(item, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Handler faults are transient by default: the record retries with backoff
            // instead of wedging the worker. Permanent failure is the handler's verdict.
            LogHandlerFaulted(exception, item.WorkType, item.Id);
            outcome = new WorkOutcome.Retryable("handler.faulted");
        }

        var now = clock.UtcNow;
        if (outcome is WorkOutcome.Succeeded)
        {
            await store.CompleteAsync(item.Id, _leaseOwner, now, cancellationToken);
            metrics.Finished("succeeded");
            return;
        }

        var next = ScheduledWorkBackoff.NextAttemptAt(
            now, item.Attempts, options.Value.BackoffBaseSeconds, options.Value.BackoffMaxSeconds);
        await store.FailAsync(item.Id, _leaseOwner, outcome, next, now, cancellationToken);
        metrics.Finished(outcome is WorkOutcome.Retryable ? "scheduled-retry" : "failed");
    }

    private TimeSpan LeaseDuration() => TimeSpan.FromSeconds(Math.Max(5, options.Value.LeaseSeconds));

    [LoggerMessage(Level = LogLevel.Error, Message = "Scheduled-work poll cycle failed.")]
    private partial void LogPollFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No handler registered for work type {WorkType} job={JobId}.")]
    private partial void LogUnknownType(string workType, Guid jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Scheduled-work handler faulted workType={WorkType} job={JobId}.")]
    private partial void LogHandlerFaulted(Exception exception, string workType, Guid jobId);
}
