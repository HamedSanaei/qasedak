using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;

namespace Qasedak.Modules.Automations.Application;

public sealed record ExecutionRequest(
    Guid AutomationId,
    TriggerContext Trigger,
    string Channel,
    ChannelAccountId? ChannelAccountId,
    Guid? WorkspaceIdHint = null);

public sealed record ExecutionOutcome(
    ExecutionStatus Status,
    IReadOnlyList<AutomationActionExecution> Actions)
{
    public static ExecutionOutcome From(AutomationRunStatus status, IReadOnlyList<AutomationActionExecution> actions) =>
        new(Map(status), actions);

    private static ExecutionStatus Map(AutomationRunStatus status) => status switch
    {
        AutomationRunStatus.Completed => ExecutionStatus.Executed,
        AutomationRunStatus.Failed => ExecutionStatus.Failed,
        AutomationRunStatus.Refused => ExecutionStatus.RefusedNotActive,
        AutomationRunStatus.Finished => ExecutionStatus.Finished,
        _ => ExecutionStatus.Executed,
    };
}

public enum ExecutionStatus
{
    Executed,

    AlreadyProcessed,

    NotMatched,

    RefusedNotActive,

    RefusedStaleVersion,

    Failed,

    /// <summary>All slots terminal; at least one one-shot effect was not delivered.</summary>
    Finished,
}

/// <summary>
/// Orchestrates one idempotent automation execution:
/// 1. load the automation — must be Active (disabled/paused automations refuse);
/// 2. evaluate the frozen version's definition deterministically; non-matches end cheaply;
/// 3. probe the run ledger by producer event id: an existing run short-circuits to
///    AlreadyProcessed (webhook redelivery never re-dispatches succeeded slots);
/// 4. start a run pinned to the frozen version number and execute action slots strictly
///    in order, persisting after each so partially executed runs survive crashes and are
///    resumed at the first pending slot on retry;
/// 5. a recorded run whose pinned version no longer equals the automation's frozen
///    version is refused as stale — executions stay reproducible against their version.
/// Concurrent deliveries of the same event race on the ledger's unique index; losers map
/// to AlreadyProcessed.
/// </summary>
public sealed class ExecuteAutomationUseCase(
    IAutomationRepository automations,
    IAutomationRunRepository runs,
    IAutomationActionDispatcher dispatcher)
{
    public async Task<ExecutionOutcome> ExecuteAsync(ExecutionRequest request, CancellationToken cancellationToken = default)
    {
        var automation = await automations.FindByIdAsync(request.AutomationId, cancellationToken);
        if (automation is null || (request.WorkspaceIdHint is { } hint && automation.WorkspaceId != hint))
        {
            return new ExecutionOutcome(ExecutionStatus.RefusedNotActive, []);
        }

        if (automation.Status != AutomationStatus.Active)
        {
            return new ExecutionOutcome(ExecutionStatus.RefusedNotActive, []);
        }

        if (request.ChannelAccountId is not { IsResolved: true }
            || automation.ChannelAccountId != request.ChannelAccountId)
        {
            // Exact-account enforcement: execution requires a resolved request
            // account; a bound automation runs only for its own account's events,
            // and a legacy unbound automation never matches an exact-account
            // request. Refusal happens before evaluation and the ledger, so
            // nothing is recorded or dispatched.
            return new ExecutionOutcome(ExecutionStatus.RefusedNotActive, []);
        }

        var frozenVersion = automation.FrozenActiveVersion();
        var evaluation = AutomationEvaluator.Evaluate(frozenVersion.Definition, request.Trigger);
        if (!evaluation.Matched)
        {
            return new ExecutionOutcome(ExecutionStatus.NotMatched, []);
        }

        var existing = await runs.FindByTriggerEventAsync(automation.Id, request.Trigger.EventId, cancellationToken);
        if (existing is not null)
        {
            if (existing.AutomationVersionNumber != frozenVersion.Number)
            {
                return new ExecutionOutcome(ExecutionStatus.RefusedStaleVersion, existing.Actions);
            }

            if (existing.Status is AutomationRunStatus.Completed or AutomationRunStatus.Finished)
            {
                // Fully executed (or terminally closed) runs are immutable ledger entries;
                // a Finished run's one-shot effects must never be re-dispatched.
                return new ExecutionOutcome(
                    existing.Status == AutomationRunStatus.Completed ? ExecutionStatus.AlreadyProcessed : ExecutionStatus.Finished,
                    existing.Actions);
            }

            // Running or partially failed runs resume at their non-terminal slots.
            return await ExecuteSlotsAsync(existing, frozenVersion.Definition, request, cancellationToken);
        }

        var run = AutomationRun.Start(Guid.CreateVersion7(), automation, request.Trigger.EventId, request.Trigger.OccurredAtUtc);
        try
        {
            await runs.SaveChangesAsync(run, cancellationToken);
        }
        catch (Exception exception) when (IsDuplicateKeyViolation(exception))
        {
            // A concurrent delivery of the same event won the ledger slot.
            return new ExecutionOutcome(ExecutionStatus.AlreadyProcessed, []);
        }

        return await ExecuteSlotsAsync(run, frozenVersion.Definition, request, cancellationToken);
    }

    private async Task<ExecutionOutcome> ExecuteSlotsAsync(
        AutomationRun run,
        AutomationDefinition definition,
        ExecutionRequest request,
        CancellationToken cancellationToken)
    {
        foreach (var slot in run.Actions
            .Where(a => a.Status is Domain.AutomationActionStatus.Pending or Domain.AutomationActionStatus.Failed)
            .OrderBy(a => a.Index))
        {
            var action = definition.Actions[slot.Index];
            var result = await dispatcher.DispatchAsync(new ActionDispatch(
                run.WorkspaceId,
                request.Channel,
                request.ChannelAccountId,
                request.Trigger.SenderId ?? string.Empty,
                action.MessageText,
                run.AutomationId,
                run.AutomationVersionNumber,
                request.Trigger.EventId,
                request.Trigger.Kind,
                request.Trigger.CommentId,
                request.Trigger.OccurredAtUtc,
                request.Trigger.IsLiveComment,
                slot.Index,
                action.Kind), cancellationToken);

            if (result.Accepted)
            {
                run.RecordSuccess(slot.Index, request.Trigger.OccurredAtUtc);
            }
            else if (result.Terminal && result.TerminalStatus is { } terminalStatus)
            {
                // One-shot effect outcomes are terminal: never re-attempted, never marked
                // delivered. A Finished run is immutable like a Completed run.
                run.RecordTerminal(slot.Index, terminalStatus, result.FailureCode ?? "action.terminal", request.Trigger.OccurredAtUtc);
            }
            else
            {
                run.RecordFailure(slot.Index, result.FailureCode ?? "action.rejected", request.Trigger.OccurredAtUtc);
            }

            await runs.SaveChangesAsync(run, cancellationToken);
        }

        return ExecutionOutcome.From(run.Status, run.Actions);
    }

    /// <summary>Npgsql reports unique-index races as SQLSTATE 23505 ("duplicate key").</summary>
    private static bool IsDuplicateKeyViolation(Exception exception)
    {
        for (var current = (Exception?)exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("23505", StringComparison.Ordinal)
                || current.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
