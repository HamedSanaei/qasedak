using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Application.Scheduling;
using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Automations.Domain.Definitions;

namespace Qasedak.Api.CrossModule;

/// <summary>
/// Composition-root adapter turning a ScheduleFollowUp automation action into a durable
/// platform scheduled job (M13-004) whose payload carries identifiers only — the run id
/// and action index (M13-012 §42-44). The message text lives in the frozen automation
/// version pinned by the run and is resolved at due time; the enqueue is idempotency-keyed
/// so redelivery of the same dispatch can never schedule a second job. The slot is marked
/// Scheduled by the run ledger (the authoritative settlement happens at due time).
/// </summary>
public sealed class AutomationFollowUpBridge(
    IScheduledWorkStore scheduledWork,
    IClock clock)
{
    public async Task<ActionResult> DispatchAsync(ActionDispatch dispatch, CancellationToken cancellationToken = default)
    {
        var delay = dispatch.Extras?.Delay;
        if (delay is null || delay.Value < AutomationAction.FollowUpDelayMin || delay.Value > AutomationAction.FollowUpDelayMax)
        {
            // Defensive re-check: authoring already enforced bounds; a corrupted/legacy
            // row must never produce an out-of-bounds schedule.
            return ActionResult.TerminalFailed("followUp.invalidDelay");
        }

        if (dispatch.RunId is null)
        {
            return ActionResult.TerminalFailed("followUp.missingRunId");
        }

        if (string.IsNullOrWhiteSpace(dispatch.ParticipantId))
        {
            // Without an authoritative participant the due-time revalidation can never
            // succeed — fail closed now rather than scheduling a doomed job.
            return ActionResult.TerminalFailed("followUp.recipientUnavailable");
        }

        var runId = dispatch.RunId.Value;
        await scheduledWork.EnqueueAsync(new ScheduledWorkEnqueue(
            WorkType: FollowUpJobPolicy.WorkType,
            IdempotencyKey: FollowUpJobPolicy.IdempotencyKey(runId, dispatch.ActionIndex),
            PayloadJson: FollowUpJobPolicy.Payload(runId, dispatch.ActionIndex),
            PayloadVersion: FollowUpJobPolicy.PayloadVersion,
            ConnectedAccountId: dispatch.ChannelAccountId is { IsResolved: true } ? dispatch.ChannelAccountId.Value.Value : null,
            WorkspaceId: dispatch.WorkspaceId,
            DueAtUtc: clock.UtcNow + delay.Value,
            MaxAttempts: FollowUpJobPolicy.DefaultMaxAttempts), clock.UtcNow, cancellationToken);

        return ActionResult.Scheduled();
    }
}
