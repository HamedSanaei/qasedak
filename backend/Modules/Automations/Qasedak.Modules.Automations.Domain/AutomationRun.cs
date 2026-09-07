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

    /// <summary>
    /// All action slots are terminal but at least one was not delivered (suppressed /
    /// uncertain / terminal-failed one-shot effect). Immutable like <see cref="Completed"/>;
    /// resuming a Finished run never re-dispatches anything.
    /// </summary>
    Finished = 5,
}

/// <summary>Outcome of one action within a run.</summary>
public enum AutomationActionStatus
{
    Pending = 0,
    Succeeded = 1,

    /// <summary>Retriable failure (channel/temporary).</summary>
    Failed = 2,

    /// <summary>Terminal: the one-shot effect was already claimed by another operation.</summary>
    Suppressed = 3,

    /// <summary>Terminal: an attempt began but the provider outcome is unknown; never re-attempted.</summary>
    Uncertain = 4,

    /// <summary>Terminal: the provider/effect failed deterministically; never re-attempted.</summary>
    TerminalFailed = 5,

    /// <summary>
    /// Durable in-flight marker persisted BEFORE any non-repeatable provider mutation
    /// (M13-012 §54-56): after it exists, a crash/restart must never issue another
    /// mutation — the slot is recovered as <see cref="Uncertain"/>.
    /// </summary>
    Attempting = 6,

    /// <summary>
    /// The action was durably accepted for later execution (delayed follow-up scheduled
    /// via platform scheduled work). Settled by the follow-up handler at due time.
    /// </summary>
    Scheduled = 7,

    /// <summary>
    /// The action durably started a continuation that outlives the originating dispatch
    /// (reveal flow awaiting user interaction). The continuation store is authoritative;
    /// this slot is never re-dispatched.
    /// </summary>
    ContinuationStarted = 8,
}

/// <summary>One action slot inside a run (additive metadata; no token, no raw payload).</summary>
public sealed record AutomationActionExecution(
    int Index,
    AutomationActionStatus Status,
    string? FailureCode,
    DateTimeOffset? AttemptedAtUtc = null,
    DateTimeOffset? CompletedAtUtc = null,
    string? ProviderRecipientId = null,
    string? ProviderMessageId = null);

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

    /// <summary>Marks the indexed action as durably in-flight (before the provider call).</summary>
    public void RecordAttempt(int actionIndex, DateTimeOffset attemptedAtUtc)
    {
        EnsureMutable(actionIndex);
        _actions[actionIndex] = _actions[actionIndex] with
        {
            Status = AutomationActionStatus.Attempting,
            AttemptedAtUtc = attemptedAtUtc,
        };
    }

    /// <summary>Settles an in-flight action as durably scheduled for later execution.</summary>
    public void RecordScheduled(int actionIndex, DateTimeOffset occurredAtUtc, string? providerRecipientId)
    {
        EnsureMutable(actionIndex);
        _actions[actionIndex] = _actions[actionIndex] with
        {
            Status = AutomationActionStatus.Scheduled,
            CompletedAtUtc = occurredAtUtc,
            ProviderRecipientId = providerRecipientId,
        };
        CloseIfTerminal(occurredAtUtc);
    }

    /// <summary>Settles an in-flight action as a durably started continuation.</summary>
    public void RecordContinuationStarted(int actionIndex, DateTimeOffset occurredAtUtc)
    {
        EnsureMutable(actionIndex);
        _actions[actionIndex] = _actions[actionIndex] with
        {
            Status = AutomationActionStatus.ContinuationStarted,
            CompletedAtUtc = occurredAtUtc,
        };
        CloseIfTerminal(occurredAtUtc);
    }

    /// <summary>Marks the indexed action succeeded; only the next pending slot may follow.</summary>
    public void RecordSuccess(
        int actionIndex,
        DateTimeOffset occurredAtUtc,
        string? providerRecipientId = null,
        string? providerMessageId = null)
    {
        EnsureMutable(actionIndex);
        _actions[actionIndex] = _actions[actionIndex] with
        {
            Status = AutomationActionStatus.Succeeded,
            CompletedAtUtc = occurredAtUtc,
            ProviderRecipientId = providerRecipientId,
            ProviderMessageId = providerMessageId,
        };
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

    /// <summary>
    /// Records a terminal (never re-attempted) outcome for a one-shot effect slot:
    /// <see cref="AutomationActionStatus.Suppressed"/>, <see cref="AutomationActionStatus.Uncertain"/>
    /// or <see cref="AutomationActionStatus.TerminalFailed"/>. A run whose slots are all
    /// terminal closes as <see cref="AutomationRunStatus.Finished"/> — immutable, never
    /// re-dispatched, never marked delivered.
    /// </summary>
    public void RecordTerminal(
        int actionIndex,
        AutomationActionStatus status,
        string failureCode,
        DateTimeOffset occurredAtUtc,
        string? providerRecipientId = null,
        string? providerMessageId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        if (status is not (AutomationActionStatus.Suppressed or AutomationActionStatus.Uncertain or AutomationActionStatus.TerminalFailed))
        {
            throw new AutomationsDomainException("run.invalidTerminalStatus", $"Status {status} is not a terminal slot outcome.");
        }

        EnsureMutable(actionIndex);
        _actions[actionIndex] = _actions[actionIndex] with
        {
            Status = status,
            FailureCode = failureCode,
            CompletedAtUtc = occurredAtUtc,
            ProviderRecipientId = providerRecipientId,
            ProviderMessageId = providerMessageId,
        };
        CloseIfTerminal(occurredAtUtc);
    }

    /// <summary>Reopens a locally-failed run for a resumed execution pass.</summary>
    public void ReopenForRetry()
    {
        if (Status == AutomationRunStatus.Failed)
        {
            Status = AutomationRunStatus.Running;
            FinishedAtUtc = null;
        }
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
        if (current.Status is not (AutomationActionStatus.Pending
            or AutomationActionStatus.Failed
            or AutomationActionStatus.Attempting
            or AutomationActionStatus.Scheduled))
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
            return;
        }

        // Scheduled (delayed follow-up) and ContinuationStarted (reveal flow) slots are
        // settled later by their continuation — they keep the run Running; only fully
        // settled terminal sets close as Finished.
        if (_actions.All(a => a.Status is AutomationActionStatus.Succeeded
            or AutomationActionStatus.Suppressed
            or AutomationActionStatus.Uncertain
            or AutomationActionStatus.TerminalFailed))
        {
            Status = AutomationRunStatus.Finished;
            FinishedAtUtc = occurredAtUtc;
        }
    }
}
