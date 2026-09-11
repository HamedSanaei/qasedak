using Qasedak.BuildingBlocks.Application.Scheduling;
using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Automations.Domain;
using Xunit;

namespace Qasedak.Modules.Automations.UnitTests;

public sealed class AutomationOperationsPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);

    public static TheoryData<AutomationRunStatus, OperatorRunState> RunCases => new()
    {
        { AutomationRunStatus.Running, OperatorRunState.InProgress },
        { AutomationRunStatus.Completed, OperatorRunState.CompletedSuccessfully },
        { AutomationRunStatus.Failed, OperatorRunState.RetriableLocalFailure },
        { AutomationRunStatus.Refused, OperatorRunState.RefusedBeforeExecution },
        { AutomationRunStatus.Finished, OperatorRunState.FinishedWithNonDelivery },
    };

    [Theory]
    [MemberData(nameof(RunCases))]
    public void EveryRunStatusHasStableClassification(AutomationRunStatus source, OperatorRunState expected)
    {
        Assert.Equal(expected, AutomationOperationsPolicy.ClassifyRun(source));
    }

    [Fact]
    public void UnknownRunStatusFailsClosed()
    {
        Assert.Equal(OperatorRunState.UnknownUnsupported, AutomationOperationsPolicy.ClassifyRun((AutomationRunStatus)999));
    }

    public static TheoryData<
        AutomationActionStatus,
        OperatorActionState,
        ExternalAttemptCertainty,
        OperatorRetryability,
        OperatorFailureCategory> ActionCases => new()
    {
        { AutomationActionStatus.Pending, OperatorActionState.NotStarted, ExternalAttemptCertainty.ProvenNoProviderMutationByThisAction, OperatorRetryability.EngineOwned, OperatorFailureCategory.None },
        { AutomationActionStatus.Succeeded, OperatorActionState.Delivered, ExternalAttemptCertainty.ProviderMutationConfirmed, OperatorRetryability.NeverRetry, OperatorFailureCategory.None },
        { AutomationActionStatus.Failed, OperatorActionState.RetriableLocalFailure, ExternalAttemptCertainty.ProvenNoProviderMutationByThisAction, OperatorRetryability.SafeLocalRetry, OperatorFailureCategory.LocalPreProviderFailure },
        { AutomationActionStatus.Suppressed, OperatorActionState.Suppressed, ExternalAttemptCertainty.ProvenNoProviderMutationByThisAction, OperatorRetryability.NeverRetry, OperatorFailureCategory.Suppressed },
        { AutomationActionStatus.Uncertain, OperatorActionState.DeliveryUncertain, ExternalAttemptCertainty.ProviderMutationMayHaveOccurred, OperatorRetryability.NeverRetry, OperatorFailureCategory.AmbiguousExternalOutcome },
        { AutomationActionStatus.TerminalFailed, OperatorActionState.DeterministicTerminalFailure, ExternalAttemptCertainty.NotProvableFromRunSlot, OperatorRetryability.NeverRetry, OperatorFailureCategory.DeterministicTerminalFailure },
        { AutomationActionStatus.Attempting, OperatorActionState.ExternalAttemptInProgress, ExternalAttemptCertainty.ProviderMutationMayHaveOccurred, OperatorRetryability.NeverRetry, OperatorFailureCategory.AmbiguousExternalOutcome },
        { AutomationActionStatus.Scheduled, OperatorActionState.ScheduledForLater, ExternalAttemptCertainty.ScheduledWorkAuthorityRequired, OperatorRetryability.ScheduledWorkAuthorityRequired, OperatorFailureCategory.ScheduledInfrastructure },
        { AutomationActionStatus.ContinuationStarted, OperatorActionState.ContinuationStarted, ExternalAttemptCertainty.ContinuationAuthorityRequired, OperatorRetryability.ContinuationAuthorityRequired, OperatorFailureCategory.ContinuationOwned },
    };

    [Theory]
    [MemberData(nameof(ActionCases))]
    public void EveryActionStatusHasStableSafetyClassification(
        AutomationActionStatus source,
        OperatorActionState state,
        ExternalAttemptCertainty externalAttempt,
        OperatorRetryability retryability,
        OperatorFailureCategory failureCategory)
    {
        var policy = AutomationOperationsPolicy.ClassifyAction(source);
        Assert.Equal(state, policy.State);
        Assert.Equal(externalAttempt, policy.ExternalAttempt);
        Assert.Equal(retryability, policy.Retryability);
        Assert.Equal(failureCategory, policy.FailureCategory);
        Assert.False(policy.BlindResendAllowed);
    }

    [Fact]
    public void ActionMatrixCoversEveryPersistedEnumValue()
    {
        AutomationActionStatus[] covered =
        [
            AutomationActionStatus.Pending,
            AutomationActionStatus.Succeeded,
            AutomationActionStatus.Failed,
            AutomationActionStatus.Suppressed,
            AutomationActionStatus.Uncertain,
            AutomationActionStatus.TerminalFailed,
            AutomationActionStatus.Attempting,
            AutomationActionStatus.Scheduled,
            AutomationActionStatus.ContinuationStarted,
        ];
        Assert.Equal(Enum.GetValues<AutomationActionStatus>().OrderBy(x => (int)x), covered.OrderBy(x => (int)x));
    }

    [Fact]
    public void AttemptingAndUncertainNeverOfferRetryOrResend()
    {
        foreach (var status in new[] { AutomationActionStatus.Attempting, AutomationActionStatus.Uncertain })
        {
            var policy = AutomationOperationsPolicy.ClassifyAction(status);
            Assert.False(policy.AutomaticRetryAllowed);
            Assert.False(policy.ManualRetryAllowed);
            Assert.False(policy.BlindResendAllowed);
            Assert.DoesNotContain(OperatorRecoveryAction.RetrySafeLocalFailure, policy.AllowedActions);
            Assert.DoesNotContain(OperatorRecoveryAction.CancelPendingWork, policy.AllowedActions);
        }
    }

    [Fact]
    public void FailedIsTheOnlyActionStateWithSafeLocalRetry()
    {
        foreach (var status in Enum.GetValues<AutomationActionStatus>())
        {
            var policy = AutomationOperationsPolicy.ClassifyAction(status);
            Assert.Equal(status == AutomationActionStatus.Failed, policy.ManualRetryAllowed);
            Assert.Equal(status == AutomationActionStatus.Failed, policy.AutomaticRetryAllowed);
            Assert.Equal(status == AutomationActionStatus.Failed, policy.AllowedActions.Contains(OperatorRecoveryAction.RetrySafeLocalFailure));
        }
    }

    [Fact]
    public void UnknownFutureActionStatusFailsClosed()
    {
        var policy = AutomationOperationsPolicy.ClassifyAction((AutomationActionStatus)999);
        Assert.Equal(OperatorActionState.UnknownUnsupported, policy.State);
        Assert.Equal(ExternalAttemptCertainty.UnknownFailClosed, policy.ExternalAttempt);
        Assert.Equal(OperatorRetryability.UnknownFailClosed, policy.Retryability);
        Assert.Equal(OperatorFailureCategory.Unknown, policy.FailureCategory);
        Assert.Empty(policy.AllowedActions);
        Assert.False(policy.AutomaticRetryAllowed);
        Assert.False(policy.ManualRetryAllowed);
        Assert.False(policy.BlindResendAllowed);
    }

    [Fact]
    public void FailureCodeIsOpaqueAndNeverControlsRetryability()
    {
        var local = new AutomationActionExecution(0, AutomationActionStatus.Failed, "future.provider-looking.code");
        var terminal = new AutomationActionExecution(0, AutomationActionStatus.TerminalFailed, "future.provider-looking.code");

        var localDescriptor = AutomationOperationsPolicy.DescribeFailure(local);
        var terminalDescriptor = AutomationOperationsPolicy.DescribeFailure(terminal);

        Assert.Equal("future.provider-looking.code", localDescriptor.StableFailureCode);
        Assert.Equal(OperatorRetryability.SafeLocalRetry, localDescriptor.Retryability);
        Assert.Equal(OperatorFailureCategory.LocalPreProviderFailure, localDescriptor.Category);
        Assert.Equal("future.provider-looking.code", terminalDescriptor.StableFailureCode);
        Assert.Equal(OperatorRetryability.NeverRetry, terminalDescriptor.Retryability);
        Assert.Equal(OperatorFailureCategory.DeterministicTerminalFailure, terminalDescriptor.Category);
    }

    [Fact]
    public void ScheduledWorkTaxonomyUsesActualRowAndObservationTime()
    {
        Assert.Equal(OperatorScheduledWorkState.Pending, AutomationOperationsPolicy.ClassifyScheduledWork(Job(ScheduledWorkStatus.Pending), Now));
        Assert.Equal(OperatorScheduledWorkState.Retrying, AutomationOperationsPolicy.ClassifyScheduledWork(Job(ScheduledWorkStatus.Pending, attempts: 1), Now));
        Assert.Equal(OperatorScheduledWorkState.Claimed, AutomationOperationsPolicy.ClassifyScheduledWork(Job(ScheduledWorkStatus.Claimed, leaseExpiresAtUtc: Now.AddMinutes(1)), Now));
        Assert.Equal(OperatorScheduledWorkState.LeaseExpiredReclaimable, AutomationOperationsPolicy.ClassifyScheduledWork(Job(ScheduledWorkStatus.Claimed, leaseExpiresAtUtc: Now), Now));
        Assert.Equal(OperatorScheduledWorkState.Succeeded, AutomationOperationsPolicy.ClassifyScheduledWork(Job(ScheduledWorkStatus.Succeeded), Now));
        Assert.Equal(OperatorScheduledWorkState.PermanentFailed, AutomationOperationsPolicy.ClassifyScheduledWork(Job(ScheduledWorkStatus.Failed), Now));
        Assert.Equal(OperatorScheduledWorkState.DeadLettered, AutomationOperationsPolicy.ClassifyScheduledWork(Job(ScheduledWorkStatus.DeadLettered), Now));
        Assert.Equal(OperatorScheduledWorkState.Cancelled, AutomationOperationsPolicy.ClassifyScheduledWork(Job(ScheduledWorkStatus.Cancelled), Now));
        Assert.Equal(OperatorScheduledWorkState.UnknownUnsupported, AutomationOperationsPolicy.ClassifyScheduledWork(Job((ScheduledWorkStatus)999), Now));
    }

    [Fact]
    public void ScheduledSlotAloneNeverClaimsJobStateOrUnsafeCancellation()
    {
        var slot = AutomationOperationsPolicy.ClassifyAction(AutomationActionStatus.Scheduled);
        Assert.True(slot.RequiresScheduledWorkAuthority);
        Assert.Empty(slot.AllowedActions);

        Assert.True(AutomationOperationsPolicy.CanCancelPendingWork(AutomationActionStatus.Scheduled, OperatorScheduledWorkState.Pending));
        Assert.True(AutomationOperationsPolicy.CanCancelPendingWork(AutomationActionStatus.Scheduled, OperatorScheduledWorkState.Retrying));
        Assert.False(AutomationOperationsPolicy.CanCancelPendingWork(AutomationActionStatus.Scheduled, OperatorScheduledWorkState.Claimed));
        Assert.False(AutomationOperationsPolicy.CanCancelPendingWork(AutomationActionStatus.Scheduled, OperatorScheduledWorkState.LeaseExpiredReclaimable));
        Assert.False(AutomationOperationsPolicy.CanCancelPendingWork(AutomationActionStatus.Succeeded, OperatorScheduledWorkState.Pending));
    }

    [Fact]
    public void AuthorizationReusesWorkspaceMembershipForSafeReadsAndRequiresOperatorForDisposition()
    {
        Assert.True(AutomationOperationsPolicy.Allows(AutomationOperationsPrivilege.WorkspaceMember, AutomationOperationsAccess.ViewSummary));
        Assert.True(AutomationOperationsPolicy.Allows(AutomationOperationsPrivilege.WorkspaceMember, AutomationOperationsAccess.ViewDetail));
        Assert.False(AutomationOperationsPolicy.Allows(AutomationOperationsPrivilege.WorkspaceMember, AutomationOperationsAccess.ResolveDisposition));

        Assert.True(AutomationOperationsPolicy.Allows(AutomationOperationsPrivilege.WorkspaceOperator, AutomationOperationsAccess.ViewSummary));
        Assert.True(AutomationOperationsPolicy.Allows(AutomationOperationsPrivilege.WorkspaceOperator, AutomationOperationsAccess.ViewDetail));
        Assert.True(AutomationOperationsPolicy.Allows(AutomationOperationsPrivilege.WorkspaceOperator, AutomationOperationsAccess.ResolveDisposition));

        foreach (var permission in Enum.GetValues<AutomationOperationsAccess>())
        {
            Assert.False(AutomationOperationsPolicy.Allows(AutomationOperationsPrivilege.None, permission));
        }
        Assert.False(AutomationOperationsPolicy.Allows(AutomationOperationsPrivilege.WorkspaceOperator, (AutomationOperationsAccess)999));
    }

    [Fact]
    public void FieldExposureIsClosedAndRedactsProviderCustomerAndSecretMaterial()
    {
        Assert.Equal(OperatorFieldExposure.SafeList, AutomationOperationsPolicy.ClassifyField(AutomationOperationsField.AutomationRunId));
        Assert.Equal(OperatorFieldExposure.SafeDetailOnly, AutomationOperationsPolicy.ClassifyField(AutomationOperationsField.ChannelAccountId));
        Assert.Equal(OperatorFieldExposure.InternalOnly, AutomationOperationsPolicy.ClassifyField(AutomationOperationsField.TriggerEventId));
        Assert.Equal(OperatorFieldExposure.InternalOnly, AutomationOperationsPolicy.ClassifyField(AutomationOperationsField.ProviderMessageId));

        foreach (var field in new[]
        {
            AutomationOperationsField.AccessToken,
            AutomationOperationsField.RawProviderPayload,
            AutomationOperationsField.CustomerMessageBody,
            AutomationOperationsField.RawScheduledPayload,
            AutomationOperationsField.ProviderErrorBody,
        })
        {
            Assert.Equal(OperatorFieldExposure.NeverExpose, AutomationOperationsPolicy.ClassifyField(field));
        }
        Assert.Equal(OperatorFieldExposure.UnknownFailClosed, AutomationOperationsPolicy.ClassifyField((AutomationOperationsField)999));
    }

    [Fact]
    public void RecoveryTargetUsesExactWorkspaceRunActionAndExecutionRevision()
    {
        var workspaceId = Guid.CreateVersion7();
        var runId = Guid.CreateVersion7();
        var attempted = Now.AddMinutes(-2);
        var completed = Now.AddMinutes(-1);
        var run = AutomationRun.FromState(
            runId,
            Guid.CreateVersion7(),
            7,
            "trigger-opaque",
            workspaceId,
            AutomationRunStatus.Finished,
            Now.AddMinutes(-3),
            completed,
            [new AutomationActionExecution(0, AutomationActionStatus.Uncertain, "action.attemptInterrupted", attempted, completed)]);

        var target = AutomationOperationsPolicy.RecoveryTarget(run, 0);

        Assert.Equal(workspaceId, target.WorkspaceId);
        Assert.Equal(runId, target.AutomationRunId);
        Assert.Equal(0, target.ActionIndex);
        Assert.Equal(7, target.AutomationVersionNumber);
        Assert.Equal(AutomationActionStatus.Uncertain, target.ExpectedActionStatus);
        Assert.Equal(attempted, target.ExpectedAttemptedAtUtc);
        Assert.Equal(completed, target.ExpectedCompletedAtUtc);
    }

    [Fact]
    public void DispositionConcurrencyIsIdempotentConflictSafeAndStaleAware()
    {
        Assert.Equal(DispositionConcurrencyOutcome.Create,
            AutomationOperationsPolicy.EvaluateDispositionConcurrency(null, OperatorRecoveryAction.Acknowledge, true));
        Assert.Equal(DispositionConcurrencyOutcome.IdempotentReplay,
            AutomationOperationsPolicy.EvaluateDispositionConcurrency(OperatorRecoveryAction.Acknowledge, OperatorRecoveryAction.Acknowledge, true));
        Assert.Equal(DispositionConcurrencyOutcome.Conflict,
            AutomationOperationsPolicy.EvaluateDispositionConcurrency(OperatorRecoveryAction.Acknowledge, OperatorRecoveryAction.ResolveWithoutResend, true));
        Assert.Equal(DispositionConcurrencyOutcome.StaleTarget,
            AutomationOperationsPolicy.EvaluateDispositionConcurrency(null, OperatorRecoveryAction.Acknowledge, false));
    }

    [Fact]
    public void OperationalRecoveryActionsCannotMasqueradeAsDispositionReplay()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AutomationOperationsPolicy.EvaluateDispositionConcurrency(null, OperatorRecoveryAction.RetrySafeLocalFailure, true));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AutomationOperationsPolicy.EvaluateDispositionConcurrency(null, OperatorRecoveryAction.CancelPendingWork, true));
    }

    [Fact]
    public void RecoveryVocabularyContainsNoGenericResendCommand()
    {
        var names = Enum.GetNames<OperatorRecoveryAction>();
        Assert.DoesNotContain("Retry", names);
        Assert.DoesNotContain("Resend", names);
        Assert.DoesNotContain("ForceSend", names);
    }

    private static ScheduledWorkItem Job(
        ScheduledWorkStatus status,
        int attempts = 0,
        DateTimeOffset? leaseExpiresAtUtc = null) => new(
            Guid.CreateVersion7(),
            "automation.follow-up",
            "run/action",
            "{}",
            1,
            null,
            Guid.CreateVersion7(),
            Now,
            status,
            attempts,
            8,
            Now,
            null,
            status == ScheduledWorkStatus.Claimed ? "worker-a" : null,
            leaseExpiresAtUtc,
            Now.AddHours(-1),
            Now,
            null,
            null);
}
