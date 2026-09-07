using Qasedak.BuildingBlocks.Application;
using Qasedak.Modules.Automations.Domain;

namespace Qasedak.Modules.Automations.Application;

/// <summary>
/// Outcome of one follow-up settlement attempt by the scheduled-work handler.
/// </summary>
public enum FollowUpSettlementStatus
{
    /// <summary>The slot was settled (terminal) by this call.</summary>
    Settled,

    /// <summary>The slot was already settled — replay no-op.</summary>
    AlreadySettled,

    /// <summary>The run/slot/version contract could not be honored.</summary>
    NotSettlable,
}

public sealed record FollowUpSettlement(
    FollowUpSettlementStatus Status,
    AutomationActionStatus? FinalStatus,
    string? FailureCode,
    bool MarkerAcquired = false);

/// <summary>
/// Domain-owned settlement of a scheduled follow-up action (M13-012 §42-59):
/// - <see cref="MarkAttemptingAsync"/> persists the durable in-flight marker before the
///   provider mutation, so a crash after the marker can never resend;
/// - <see cref="CompleteAsync"/> records the authoritative terminal/success outcome and
///   closes the run; it is a no-op when the slot is already settled (scheduler
///   at-least-once redelivery), and refuses to settle a slot that is not in
///   Attempting/Scheduled (protects against out-of-order or stale jobs).
/// Timestamps are passed in (no clock inside the Domain); the aggregate is the only
/// source of truth.
/// </summary>
public sealed class FollowUpExecutionUseCase(IAutomationRunRepository runs, IClock clock)
{
    public async Task<FollowUpSettlement> MarkAttemptingAsync(Guid runId, int actionIndex, CancellationToken cancellationToken = default)
    {
        var result = await runs.MarkAttemptingAsync(runId, actionIndex, clock.UtcNow, cancellationToken);
        return result switch
        {
            AttemptMarkerResult.Transitioned => new FollowUpSettlement(
                FollowUpSettlementStatus.Settled, Domain.AutomationActionStatus.Attempting, null, MarkerAcquired: true),
            AttemptMarkerResult.AlreadyAttempting => new FollowUpSettlement(
                FollowUpSettlementStatus.Settled, Domain.AutomationActionStatus.Attempting, null),
            AttemptMarkerResult.AlreadySettled => new FollowUpSettlement(
                FollowUpSettlementStatus.AlreadySettled, null, null),
            _ => new FollowUpSettlement(FollowUpSettlementStatus.NotSettlable, null, "followUp.runMissing"),
        };
    }

    /// <summary>
    /// Pre-send terminal verdict (automation paused/disabled, window expired, account
    /// or recipient unavailable). Window expiry is recorded as TerminalFailed because the
    /// effect can never succeed; other lifecycle verdicts default to Suppressed. The guarded
    /// transition makes concurrent at-least-once redeliveries idempotent and can never race
    /// a marker-holding sender into a duplicate.
    /// </summary>
    public async Task<FollowUpSettlement> SuppressAsync(
        Guid runId,
        int actionIndex,
        string failureCode,
        AutomationActionStatus status = AutomationActionStatus.Suppressed,
        CancellationToken cancellationToken = default)
    {
        var result = await runs.SuppressAsync(runId, actionIndex, failureCode, clock.UtcNow, status, cancellationToken);
        return result switch
        {
            AttemptMarkerResult.Transitioned => new FollowUpSettlement(
                FollowUpSettlementStatus.Settled, status, failureCode),
            AttemptMarkerResult.AlreadySettled => new FollowUpSettlement(
                FollowUpSettlementStatus.AlreadySettled, null, null),
            _ => new FollowUpSettlement(FollowUpSettlementStatus.NotSettlable, null, "followUp.runMissing"),
        };
    }

    public async Task<FollowUpSettlement> CompleteAsync(
        Guid runId,
        int actionIndex,
        AutomationActionStatus status,
        string? failureCode,
        string? providerRecipientId = null,
        string? providerMessageId = null,
        CancellationToken cancellationToken = default)
    {
        if (status is not (Domain.AutomationActionStatus.Succeeded
            or Domain.AutomationActionStatus.Suppressed
            or Domain.AutomationActionStatus.Uncertain
            or Domain.AutomationActionStatus.TerminalFailed))
        {
            throw new ArgumentOutOfRangeException(nameof(status), "Follow-up settlement requires a success or terminal status.");
        }

        var run = await runs.FindByIdAsync(runId, cancellationToken);
        if (run is null || actionIndex < 0 || actionIndex >= run.Actions.Count)
        {
            return new FollowUpSettlement(FollowUpSettlementStatus.NotSettlable, null, "followUp.runMissing");
        }

        var slot = run.Actions[actionIndex];
        if (slot.Status is Domain.AutomationActionStatus.Succeeded
            or Domain.AutomationActionStatus.Suppressed
            or Domain.AutomationActionStatus.Uncertain
            or Domain.AutomationActionStatus.TerminalFailed)
        {
            // At-least-once scheduler redelivery: the authoritative outcome is already stored.
            return new FollowUpSettlement(FollowUpSettlementStatus.AlreadySettled, slot.Status, slot.FailureCode);
        }

        if (slot.Status != Domain.AutomationActionStatus.Attempting)
        {
            // Only the execution holding the durable Attempting marker may settle the
            // slot — a stale/out-of-order job (or a crash before the marker) must not.
            return new FollowUpSettlement(FollowUpSettlementStatus.NotSettlable, slot.Status, "followUp.slotNotReady");
        }

        var now = clock.UtcNow;
        if (status == Domain.AutomationActionStatus.Succeeded)
        {
            run.RecordSuccess(actionIndex, now, providerRecipientId, providerMessageId);
        }
        else
        {
            run.RecordTerminal(actionIndex, status, failureCode ?? "followUp.terminal", now, providerRecipientId, providerMessageId);
        }

        await runs.SaveChangesAsync(run, cancellationToken);
        return new FollowUpSettlement(FollowUpSettlementStatus.Settled, status, failureCode);
    }
}
