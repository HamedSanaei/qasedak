using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;

namespace Qasedak.Modules.Automations.Application;

/// <summary>Outcome of the durable follow-up attempt-marker transition (M13-012 §54).</summary>
public enum AttemptMarkerResult
{
    /// <summary>This caller atomically won the Scheduled → Attempting transition — it may issue the provider mutation.</summary>
    Transitioned,

    /// <summary>Another execution already holds the Attempting marker — this caller MUST NOT send.</summary>
    AlreadyAttempting,

    /// <summary>The slot is already terminally settled — nothing to do.</summary>
    AlreadySettled,

    /// <summary>The run/slot does not exist.</summary>
    NotFound,

    /// <summary>The slot exists but is not in a marker-transitionable state.</summary>
    SlotNotReady,
}

/// <summary>Persistence contract for execution records (idempotency ledger).</summary>
public interface IAutomationRunRepository
{
    /// <summary>Finds the run recorded for a trigger event, if any — the idempotency probe.</summary>
    Task<AutomationRun?> FindByTriggerEventAsync(Guid automationId, string triggerEventId, CancellationToken cancellationToken = default);

    /// <summary>Finds a run by its id (follow-up settlement path).</summary>
    Task<AutomationRun?> FindByIdAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically transitions a Scheduled follow-up slot to Attempting (single UPDATE with a
    /// status guard): concurrent scheduler workers racing for the same slot converge so that
    /// exactly ONE holds the marker and may call the provider; losers are told so without
    /// any external mutation.
    /// </summary>
    Task<AttemptMarkerResult> MarkAttemptingAsync(Guid runId, int actionIndex, DateTimeOffset attemptedAtUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically settles a follow-up slot with a pre-send terminal verdict (automation
    /// paused/disabled, window expired, account or recipient unavailable). Defaults to
    /// <see cref="AutomationActionStatus.Suppressed"/>; window expiry is recorded as
    /// <see cref="AutomationActionStatus.TerminalFailed"/> because the effect can never
    /// succeed. Guarded on Scheduled OR Attempting so a concurrent send winner cannot be
    /// raced into a duplicate; idempotent under at-least-once redelivery.
    /// </summary>
    Task<AttemptMarkerResult> SuppressAsync(Guid runId, int actionIndex, string failureCode, DateTimeOffset completedAtUtc, AutomationActionStatus status = AutomationActionStatus.Suppressed, CancellationToken cancellationToken = default);

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
    string? MediaId = null,
    string? OriginalMediaId = null,
    int ActionIndex = 0,
    ActionKind ActionKind = ActionKind.SendDirectMessage,
    ActionExtras? Extras = null,
    Guid? RunId = null);

/// <summary>
/// Verdict of one action dispatch. A <c>Terminal</c> rejection means the slot must NEVER
/// be re-attempted (one-shot provider effects): the orchestration records the supplied
/// terminal slot status instead of a retriable failure. Codes are stable and log-safe.
/// </summary>
public sealed record ActionResult(
    bool Accepted,
    string? FailureCode,
    bool Terminal = false,
    AutomationActionStatus? TerminalStatus = null,
    bool ExternalAttempt = true,
    AutomationActionStatus? AcceptedStatus = null,
    string? ProviderRecipientId = null,
    string? ProviderMessageId = null)
{
    public static ActionResult Delivered(string? providerRecipientId = null, string? providerMessageId = null) =>
        new(true, null, ProviderRecipientId: providerRecipientId, ProviderMessageId: providerMessageId);

    /// <summary>Durably accepted for later execution (delayed follow-up scheduled).</summary>
    public static ActionResult Scheduled() => new(true, null, AcceptedStatus: Domain.AutomationActionStatus.Scheduled);

    /// <summary>Durably accepted as a started continuation (reveal flow).</summary>
    public static ActionResult ContinuationStarted() => new(true, null, AcceptedStatus: Domain.AutomationActionStatus.ContinuationStarted);

    /// <summary>Retriable rejection AFTER an external attempt — terminal for one-shot safety.</summary>
    public static ActionResult Rejected(string failureCode) => new(false, failureCode);

    /// <summary>Retriable rejection with NO external attempt — safe to re-dispatch on redelivery.</summary>
    public static ActionResult RejectedLocal(string failureCode) => new(false, failureCode, ExternalAttempt: false);

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
