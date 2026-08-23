namespace Qasedak.Modules.Automations.Domain;

/// <summary>Lifecycle of a single triggered execution.</summary>
public enum AutomationRunStatus
{
    /// <summary>At least one action is not yet terminal.</summary>
    Running = 1,

    /// <summary>Every action succeeded.</summary>
    Completed = 2,

    /// <summary>At least one action failed permanently; retriable via new attempts.</summary>
    Failed = 3,

    /// <summary>Evaluation matched but the source automation was stale/disabled mid-run.</summary>
    Refused = 4,
}

/// <summary>Outcome of one action within a run.</summary>
public enum AutomationActionStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
}

/// <summary>One action slot inside a run.</summary>
public sealed record AutomationActionExecution(int Index, AutomationActionStatus Status, string? FailureCode);

/// <summary>
/// Execution record making automation effects idempotent: one run exists per
/// (automation, trigger event). Actions are slots executed strictly in index order;
/// succeeded slots are never re-dispatched, so webhook redelivery and retries continue
/// partially executed runs exactly where they stopped. A completed run is immutable.
/// </summary>
public sealed class AutomationRun
{
    private readonly List<AutomationActionExecution> _actions = [];

    private AutomationRun(
        Guid id,
        Guid automationId,
        int automationVersionNumber,
        string triggerEventId,
        Guid workspaceId,
        int expectedActions,
        DateTimeOffset startedAtUtc)
    {
        Id = id;
        AutomationId = automationId;
        AutomationVersionNumber = automationVersionNumber;
        TriggerEventId = triggerEventId;
        WorkspaceId = workspaceId;
        Status = AutomationRunStatus.Running;
        StartedAtUtc = startedAtUtc;
        for (var index = 0; index < expectedActions; index++)
        {
            _actions.Add(new AutomationActionExecution(index, AutomationActionStatus.Pending, null));
        }
    }

    public Guid Id { get; }

    public Guid AutomationId { get; }

    /// <summary>The frozen definition version this run executes — reproducibility anchor.</summary>
    public int AutomationVersionNumber { get; }

    /// <summary>Inbox event identity; the natural idempotency key.</summary>
    public string TriggerEventId { get; }

    public Guid WorkspaceId { get; }

    public AutomationRunStatus Status { get; private set; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset? FinishedAtUtc { get; private set; }

    public IReadOnlyList<AutomationActionExecution> Actions => _actions.AsReadOnly();

    public static AutomationRun Start(
        Guid id,
        Automation automation,
        string triggerEventId,
        DateTimeOffset startedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(automation);
        if (automation.Status != AutomationStatus.Active)
        {
            throw new AutomationsDomainException("automation.notActive", "Runs can only start for active automations.");
        }

        var version = automation.FrozenActiveVersion();
        return new AutomationRun(
            id, automation.Id, version.Number, triggerEventId, automation.WorkspaceId,
            version.Definition.Actions.Count, startedAtUtc);
    }

    /// <summary>Rehydration for persistence; state was valid when saved.</summary>
    public static AutomationRun FromState(
        Guid id,
        Guid automationId,
        int automationVersionNumber,
        string triggerEventId,
        Guid workspaceId,
        AutomationRunStatus status,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? finishedAtUtc,
        IReadOnlyList<AutomationActionExecution> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        var run = new AutomationRun(id, automationId, automationVersionNumber, triggerEventId, workspaceId, actions.Count, startedAtUtc);
        run._actions.Clear();
        run._actions.AddRange(actions);
        run.Status = status;
        run.FinishedAtUtc = finishedAtUtc;
        return run;
    }

    /// <summary>Marks the indexed action succeeded; only the next pending slot may follow.</summary>
    public void RecordSuccess(int actionIndex, DateTimeOffset occurredAtUtc)
    {
        EnsureMutable(actionIndex);
        _actions[actionIndex] = _actions[actionIndex] with { Status = AutomationActionStatus.Succeeded };
        CloseIfTerminal(occurredAtUtc);
    }

    public void RecordFailure(int actionIndex, string failureCode, DateTimeOffset occurredAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        EnsureMutable(actionIndex);
        _actions[actionIndex] = _actions[actionIndex] with { Status = AutomationActionStatus.Failed, FailureCode = failureCode };
        Status = AutomationRunStatus.Failed;
        FinishedAtUtc = occurredAtUtc;
    }

    private void EnsureMutable(int actionIndex)
    {
        if (Status is AutomationRunStatus.Completed or AutomationRunStatus.Refused)
        {
            throw new AutomationsDomainException("run.immutable", "A closed run can no longer change.");
        }

        if (Status == AutomationRunStatus.Failed && _actions[actionIndex].Status == AutomationActionStatus.Pending)
        {
            // Retries may resume a failed run at its next pending slot; recording into an
            // earlier succeeded/failed slot stays forbidden below.
        }

        if (actionIndex < 0 || actionIndex >= _actions.Count)
        {
            throw new AutomationsDomainException("run.actionIndexInvalid", "Action index outside the run.");
        }

        var current = _actions[actionIndex];
        if (current.Status != AutomationActionStatus.Pending && current.Status != AutomationActionStatus.Failed)
        {
            throw new AutomationsDomainException("run.alreadyRecorded", $"Action {actionIndex} was already recorded.");
        }
    }

    private void CloseIfTerminal(DateTimeOffset occurredAtUtc)
    {
        if (_actions.All(a => a.Status == AutomationActionStatus.Succeeded))
        {
            Status = AutomationRunStatus.Completed;
            FinishedAtUtc = occurredAtUtc;
        }
    }
}
