using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;

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

/// <summary>
/// One dispatch for one action slot. Carries the trigger origin (M13-009): the channel
/// dispatcher needs to know the action came from a comment trigger to route it to the
/// comment-ID-addressed Private Reply operation instead of a recipient.id direct message.
/// <see cref="TriggerEntityId"/> is the provider-neutral origin entity (the CommentId for
/// comment triggers), <see cref="ActionIndex"/> is the deterministic effect-owner position.
/// </summary>
public sealed record ActionDispatch(
    Guid WorkspaceId,
    string Channel,
    ChannelAccountId? ChannelAccountId,
    string ParticipantId,
    string MessageText,
    Guid AutomationId,
    int AutomationVersionNumber,
    string TriggerEventId,
    TriggerKind? TriggerKind = null,
    string? TriggerEntityId = null,
    DateTimeOffset? TriggerOccurredAtUtc = null,
    bool IsLiveComment = false,
    int ActionIndex = 0,
    ActionKind ActionKind = ActionKind.SendDirectMessage);

/// <summary>
/// Verdict of one action dispatch. A <c>Terminal</c> rejection means the slot must NEVER
/// be re-attempted (one-shot provider effects): the orchestration records the supplied
/// terminal slot status instead of a retriable failure. Codes are stable and log-safe.
/// </summary>
public sealed record ActionResult(bool Accepted, string? FailureCode, bool Terminal = false, AutomationActionStatus? TerminalStatus = null)
{
    public static ActionResult Delivered() => new(true, null);

    public static ActionResult Rejected(string failureCode) => new(false, failureCode);

    /// <summary>Another operation already delivered the one-shot effect — truthful suppression.</summary>
    public static ActionResult TerminalSuppressed(string failureCode) => new(false, failureCode, Terminal: true, AutomationActionStatus.Suppressed);

    /// <summary>Attempt began but the outcome is unknown — never re-attempted.</summary>
    public static ActionResult TerminalUncertain(string failureCode) => new(false, failureCode, Terminal: true, AutomationActionStatus.Uncertain);

    /// <summary>Provider/effect terminal failure — never re-attempted.</summary>
    public static ActionResult TerminalFailed(string failureCode) => new(false, failureCode, Terminal: true, AutomationActionStatus.TerminalFailed);
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
