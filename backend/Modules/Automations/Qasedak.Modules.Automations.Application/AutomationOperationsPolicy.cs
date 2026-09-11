using Qasedak.BuildingBlocks.Application.Scheduling;
using Qasedak.Modules.Automations.Domain;

namespace Qasedak.Modules.Automations.Application;

/// <summary>Stable operator projection of the persisted run lifecycle.</summary>
public enum OperatorRunState
{
    InProgress = 1,
    CompletedSuccessfully = 2,
    RetriableLocalFailure = 3,
    RefusedBeforeExecution = 4,
    FinishedWithNonDelivery = 5,
    UnknownUnsupported = 6,
}

/// <summary>Stable operator projection of one automation action slot.</summary>
public enum OperatorActionState
{
    NotStarted = 1,
    Delivered = 2,
    RetriableLocalFailure = 3,
    Suppressed = 4,
    DeliveryUncertain = 5,
    DeterministicTerminalFailure = 6,
    ExternalAttemptInProgress = 7,
    ScheduledForLater = 8,
    ContinuationStarted = 9,
    UnknownUnsupported = 10,
}

/// <summary>Whether the originating action may already have mutated the provider.</summary>
public enum ExternalAttemptCertainty
{
    ProvenNoProviderMutationByThisAction = 1,
    ProviderMutationConfirmed = 2,
    ProviderMutationMayHaveOccurred = 3,
    NotProvableFromRunSlot = 4,
    ScheduledWorkAuthorityRequired = 5,
    ContinuationAuthorityRequired = 6,
    UnknownFailClosed = 7,
}

/// <summary>Recovery retryability derived from durable state, never failure-code text.</summary>
public enum OperatorRetryability
{
    EngineOwned = 1,
    SafeLocalRetry = 2,
    NeverRetry = 3,
    ScheduledWorkAuthorityRequired = 4,
    ContinuationAuthorityRequired = 5,
    UnknownFailClosed = 6,
}

/// <summary>Low-cardinality failure class for operator diagnostics.</summary>
public enum OperatorFailureCategory
{
    None = 1,
    LocalPreProviderFailure = 2,
    Suppressed = 3,
    AmbiguousExternalOutcome = 4,
    DeterministicTerminalFailure = 5,
    ScheduledInfrastructure = 6,
    ContinuationOwned = 7,
    Unknown = 8,
}

/// <summary>Closed operator vocabulary; there is deliberately no generic Retry/Resend/ForceSend.</summary>
public enum OperatorRecoveryAction
{
    Acknowledge = 1,
    ResolveWithoutResend = 2,
    ConfirmExternallyDelivered = 3,
    ConfirmExternallyNotDelivered = 4,
    CancelPendingWork = 5,
    RetrySafeLocalFailure = 6,
}

/// <summary>Stable diagnostic projection of the actual platform scheduled-work row.</summary>
public enum OperatorScheduledWorkState
{
    Pending = 1,
    Claimed = 2,
    Retrying = 3,
    Succeeded = 4,
    PermanentFailed = 5,
    DeadLettered = 6,
    Cancelled = 7,
    LeaseExpiredReclaimable = 8,
    UnknownUnsupported = 9,
}

public enum AutomationOperationsPrivilege
{
    None = 0,
    WorkspaceMember = 1,
    WorkspaceOperator = 2,
}

public enum AutomationOperationsAccess
{
    ViewSummary = 1,
    ViewDetail = 2,
    ResolveDisposition = 3,
}

public enum OperatorFieldExposure
{
    SafeList = 1,
    SafeDetailOnly = 2,
    InternalOnly = 3,
    NeverExpose = 4,
    UnknownFailClosed = 5,
}

/// <summary>Fields considered by the M14 operations contract.</summary>
public enum AutomationOperationsField
{
    AutomationId = 1,
    AutomationRunId = 2,
    AutomationVersionNumber = 3,
    ActionIndex = 4,
    WorkspaceId = 5,
    ChannelAccountId = 6,
    TriggerEventId = 7,
    ScheduledWorkId = 8,
    IdempotencyKey = 9,
    ProviderRecipientId = 10,
    ProviderMessageId = 11,
    FailureCode = 12,
    AccessToken = 13,
    RawProviderPayload = 14,
    CustomerMessageBody = 15,
    RawScheduledPayload = 16,
    ProviderErrorBody = 17,
}

public enum DispositionConcurrencyOutcome
{
    Create = 1,
    IdempotentReplay = 2,
    Conflict = 3,
    StaleTarget = 4,
}

/// <summary>Pure operator view of an action state and its recovery safety.</summary>
public sealed record OperatorActionPolicy(
    OperatorActionState State,
    ExternalAttemptCertainty ExternalAttempt,
    OperatorRetryability Retryability,
    OperatorFailureCategory FailureCategory,
    IReadOnlyList<OperatorRecoveryAction> AllowedActions,
    bool AutomaticRetryAllowed,
    bool ManualRetryAllowed,
    bool BlindResendAllowed,
    bool RequiresScheduledWorkAuthority,
    bool RequiresContinuationAuthority);

/// <summary>
/// Optimistic recovery target. The action status/timestamps form the target revision;
/// a later state transition changes this tuple and makes a stale disposition fail closed.
/// </summary>
public sealed record AutomationRecoveryTarget(
    Guid WorkspaceId,
    Guid AutomationRunId,
    int ActionIndex,
    int AutomationVersionNumber,
    AutomationActionStatus ExpectedActionStatus,
    DateTimeOffset? ExpectedAttemptedAtUtc,
    DateTimeOffset? ExpectedCompletedAtUtc);

/// <summary>Failure projection keeps the stable code but never accepts provider prose.</summary>
public sealed record OperatorFailureDescriptor(
    string? StableFailureCode,
    OperatorFailureCategory Category,
    OperatorRetryability Retryability,
    ExternalAttemptCertainty ExternalAttempt);

/// <summary>M14 operator/recovery contract. Pure, deterministic and I/O-free.</summary>
public static class AutomationOperationsPolicy
{
    private static readonly OperatorRecoveryAction[] NoActions = [];

    public static OperatorRunState ClassifyRun(AutomationRunStatus status) => status switch
    {
        AutomationRunStatus.Running => OperatorRunState.InProgress,
        AutomationRunStatus.Completed => OperatorRunState.CompletedSuccessfully,
        AutomationRunStatus.Failed => OperatorRunState.RetriableLocalFailure,
        AutomationRunStatus.Refused => OperatorRunState.RefusedBeforeExecution,
        AutomationRunStatus.Finished => OperatorRunState.FinishedWithNonDelivery,
        _ => OperatorRunState.UnknownUnsupported,
    };

    public static OperatorActionPolicy ClassifyAction(AutomationActionStatus status) => status switch
    {
        AutomationActionStatus.Pending => Policy(
            OperatorActionState.NotStarted,
            ExternalAttemptCertainty.ProvenNoProviderMutationByThisAction,
            OperatorRetryability.EngineOwned,
            OperatorFailureCategory.None),

        AutomationActionStatus.Succeeded => Policy(
            OperatorActionState.Delivered,
            ExternalAttemptCertainty.ProviderMutationConfirmed,
            OperatorRetryability.NeverRetry,
            OperatorFailureCategory.None),

        AutomationActionStatus.Failed => Policy(
            OperatorActionState.RetriableLocalFailure,
            ExternalAttemptCertainty.ProvenNoProviderMutationByThisAction,
            OperatorRetryability.SafeLocalRetry,
            OperatorFailureCategory.LocalPreProviderFailure,
            [OperatorRecoveryAction.RetrySafeLocalFailure, OperatorRecoveryAction.Acknowledge],
            automaticRetryAllowed: true,
            manualRetryAllowed: true),

        AutomationActionStatus.Suppressed => Policy(
            OperatorActionState.Suppressed,
            ExternalAttemptCertainty.ProvenNoProviderMutationByThisAction,
            OperatorRetryability.NeverRetry,
            OperatorFailureCategory.Suppressed,
            [OperatorRecoveryAction.Acknowledge]),

        AutomationActionStatus.Uncertain => Policy(
            OperatorActionState.DeliveryUncertain,
            ExternalAttemptCertainty.ProviderMutationMayHaveOccurred,
            OperatorRetryability.NeverRetry,
            OperatorFailureCategory.AmbiguousExternalOutcome,
            [
                OperatorRecoveryAction.Acknowledge,
                OperatorRecoveryAction.ResolveWithoutResend,
                OperatorRecoveryAction.ConfirmExternallyDelivered,
                OperatorRecoveryAction.ConfirmExternallyNotDelivered,
            ]),

        AutomationActionStatus.TerminalFailed => Policy(
            OperatorActionState.DeterministicTerminalFailure,
            ExternalAttemptCertainty.NotProvableFromRunSlot,
            OperatorRetryability.NeverRetry,
            OperatorFailureCategory.DeterministicTerminalFailure,
            [OperatorRecoveryAction.Acknowledge, OperatorRecoveryAction.ResolveWithoutResend]),

        AutomationActionStatus.Attempting => Policy(
            OperatorActionState.ExternalAttemptInProgress,
            ExternalAttemptCertainty.ProviderMutationMayHaveOccurred,
            OperatorRetryability.NeverRetry,
            OperatorFailureCategory.AmbiguousExternalOutcome,
            [OperatorRecoveryAction.Acknowledge]),

        AutomationActionStatus.Scheduled => Policy(
            OperatorActionState.ScheduledForLater,
            ExternalAttemptCertainty.ScheduledWorkAuthorityRequired,
            OperatorRetryability.ScheduledWorkAuthorityRequired,
            OperatorFailureCategory.ScheduledInfrastructure,
            requiresScheduledWorkAuthority: true),

        AutomationActionStatus.ContinuationStarted => Policy(
            OperatorActionState.ContinuationStarted,
            ExternalAttemptCertainty.ContinuationAuthorityRequired,
            OperatorRetryability.ContinuationAuthorityRequired,
            OperatorFailureCategory.ContinuationOwned,
            requiresContinuationAuthority: true),

        _ => Policy(
            OperatorActionState.UnknownUnsupported,
            ExternalAttemptCertainty.UnknownFailClosed,
            OperatorRetryability.UnknownFailClosed,
            OperatorFailureCategory.Unknown),
    };

    public static OperatorFailureDescriptor DescribeFailure(AutomationActionExecution action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var policy = ClassifyAction(action.Status);
        return new OperatorFailureDescriptor(
            action.FailureCode,
            policy.FailureCategory,
            policy.Retryability,
            policy.ExternalAttempt);
    }

    public static OperatorScheduledWorkState ClassifyScheduledWork(
        ScheduledWorkItem item,
        DateTimeOffset observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Status switch
        {
            ScheduledWorkStatus.Pending when item.Attempts > 0 => OperatorScheduledWorkState.Retrying,
            ScheduledWorkStatus.Pending => OperatorScheduledWorkState.Pending,
            ScheduledWorkStatus.Claimed when item.LeaseExpiresAtUtc is { } lease && lease <= observedAtUtc
                => OperatorScheduledWorkState.LeaseExpiredReclaimable,
            ScheduledWorkStatus.Claimed => OperatorScheduledWorkState.Claimed,
            ScheduledWorkStatus.Succeeded => OperatorScheduledWorkState.Succeeded,
            ScheduledWorkStatus.Failed => OperatorScheduledWorkState.PermanentFailed,
            ScheduledWorkStatus.DeadLettered => OperatorScheduledWorkState.DeadLettered,
            ScheduledWorkStatus.Cancelled => OperatorScheduledWorkState.Cancelled,
            _ => OperatorScheduledWorkState.UnknownUnsupported,
        };
    }

    /// <summary>
    /// Slot-only Scheduled never authorizes cancellation. The caller must join the exact
    /// correlated scheduled-work row; only a not-currently-claimed Pending/Retrying row
    /// is eligible for the future CancelPendingWork operation.
    /// </summary>
    public static bool CanCancelPendingWork(
        AutomationActionStatus actionStatus,
        OperatorScheduledWorkState scheduledWorkState) =>
        actionStatus == AutomationActionStatus.Scheduled
        && scheduledWorkState is OperatorScheduledWorkState.Pending or OperatorScheduledWorkState.Retrying;

    public static bool Allows(
        AutomationOperationsPrivilege privilege,
        AutomationOperationsAccess permission) => permission switch
        {
            AutomationOperationsAccess.ViewSummary =>
                privilege is AutomationOperationsPrivilege.WorkspaceMember or AutomationOperationsPrivilege.WorkspaceOperator,
            AutomationOperationsAccess.ViewDetail =>
                privilege is AutomationOperationsPrivilege.WorkspaceMember or AutomationOperationsPrivilege.WorkspaceOperator,
            AutomationOperationsAccess.ResolveDisposition =>
                privilege == AutomationOperationsPrivilege.WorkspaceOperator,
            _ => false,
        };

    public static OperatorFieldExposure ClassifyField(AutomationOperationsField field) => field switch
    {
        AutomationOperationsField.AutomationId => OperatorFieldExposure.SafeList,
        AutomationOperationsField.AutomationRunId => OperatorFieldExposure.SafeList,
        AutomationOperationsField.AutomationVersionNumber => OperatorFieldExposure.SafeList,
        AutomationOperationsField.ActionIndex => OperatorFieldExposure.SafeList,
        AutomationOperationsField.FailureCode => OperatorFieldExposure.SafeList,
        AutomationOperationsField.WorkspaceId => OperatorFieldExposure.SafeDetailOnly,
        AutomationOperationsField.ChannelAccountId => OperatorFieldExposure.SafeDetailOnly,
        AutomationOperationsField.ScheduledWorkId => OperatorFieldExposure.SafeDetailOnly,
        AutomationOperationsField.TriggerEventId => OperatorFieldExposure.InternalOnly,
        AutomationOperationsField.IdempotencyKey => OperatorFieldExposure.InternalOnly,
        AutomationOperationsField.ProviderRecipientId => OperatorFieldExposure.InternalOnly,
        AutomationOperationsField.ProviderMessageId => OperatorFieldExposure.InternalOnly,
        AutomationOperationsField.AccessToken => OperatorFieldExposure.NeverExpose,
        AutomationOperationsField.RawProviderPayload => OperatorFieldExposure.NeverExpose,
        AutomationOperationsField.CustomerMessageBody => OperatorFieldExposure.NeverExpose,
        AutomationOperationsField.RawScheduledPayload => OperatorFieldExposure.NeverExpose,
        AutomationOperationsField.ProviderErrorBody => OperatorFieldExposure.NeverExpose,
        _ => OperatorFieldExposure.UnknownFailClosed,
    };

    public static AutomationRecoveryTarget RecoveryTarget(AutomationRun run, int actionIndex)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (actionIndex < 0 || actionIndex >= run.Actions.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(actionIndex));
        }

        var action = run.Actions[actionIndex];
        return new AutomationRecoveryTarget(
            run.WorkspaceId,
            run.Id,
            actionIndex,
            run.AutomationVersionNumber,
            action.Status,
            action.AttemptedAtUtc,
            action.CompletedAtUtc);
    }

    public static bool IsDispositionOnly(OperatorRecoveryAction action) => action is
        OperatorRecoveryAction.Acknowledge
        or OperatorRecoveryAction.ResolveWithoutResend
        or OperatorRecoveryAction.ConfirmExternallyDelivered
        or OperatorRecoveryAction.ConfirmExternallyNotDelivered;

    public static DispositionConcurrencyOutcome EvaluateDispositionConcurrency(
        OperatorRecoveryAction? existingDisposition,
        OperatorRecoveryAction requestedDisposition,
        bool targetVersionStillCurrent)
    {
        if (!IsDispositionOnly(requestedDisposition))
        {
            throw new ArgumentOutOfRangeException(nameof(requestedDisposition), "Only disposition-only actions use this concurrency contract.");
        }

        if (!targetVersionStillCurrent)
        {
            return DispositionConcurrencyOutcome.StaleTarget;
        }

        return existingDisposition switch
        {
            null => DispositionConcurrencyOutcome.Create,
            { } existing when existing == requestedDisposition => DispositionConcurrencyOutcome.IdempotentReplay,
            _ => DispositionConcurrencyOutcome.Conflict,
        };
    }

    private static OperatorActionPolicy Policy(
        OperatorActionState state,
        ExternalAttemptCertainty externalAttempt,
        OperatorRetryability retryability,
        OperatorFailureCategory failureCategory,
        IReadOnlyList<OperatorRecoveryAction>? allowedActions = null,
        bool automaticRetryAllowed = false,
        bool manualRetryAllowed = false,
        bool requiresScheduledWorkAuthority = false,
        bool requiresContinuationAuthority = false) =>
        new(
            state,
            externalAttempt,
            retryability,
            failureCategory,
            allowedActions ?? NoActions,
            automaticRetryAllowed,
            manualRetryAllowed,
            BlindResendAllowed: false,
            requiresScheduledWorkAuthority,
            requiresContinuationAuthority);
}
