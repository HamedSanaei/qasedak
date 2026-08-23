using Qasedak.Modules.Automations.Domain;

namespace Qasedak.Modules.Automations.Application;

/// <summary>Persistence contract for execution records (idempotency ledger).</summary>
public interface IAutomationRunRepository
{
    /// <summary>Finds the run recorded for a trigger event, if any — the idempotency probe.</summary>
    Task<AutomationRun?> FindByTriggerEventAsync(Guid automationId, string triggerEventId, CancellationToken cancellationToken = default);

    /// <summary>Persists the current run state (insert or full-row upsert of slots).</summary>
    Task SaveChangesAsync(AutomationRun run, CancellationToken cancellationToken = default);
}

/// <summary>
/// Outbound boundary that performs one automation action. The composition root binds it
/// to the channel-specific sender; the module never references one.
/// </summary>
public interface IAutomationActionDispatcher
{
    Task<ActionResult> DispatchAsync(ActionDispatch dispatch, CancellationToken cancellationToken = default);
}

public sealed record ActionDispatch(
    Guid WorkspaceId,
    string Channel,
    string ParticipantId,
    string MessageText,
    Guid AutomationId,
    int AutomationVersionNumber,
    string TriggerEventId);

public sealed record ActionResult(bool Accepted, string? FailureCode)
{
    public static ActionResult Delivered() => new(true, null);

    public static ActionResult Rejected(string failureCode) => new(false, failureCode);
}

/// <summary>Stable failure codes for the orchestration flow.</summary>
public static class ExecutionFailures
{
    public const string AutomationNotFound = "automation.notFound";

    public const string NotActive = "automation.notActive";

    public const string VersionStale = "automation.versionStale";

    public const string AlreadyProcessed = "run.alreadyProcessed";

    public const string DuplicateStart = "run.duplicateStart";
}
