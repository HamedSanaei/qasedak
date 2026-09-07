using Microsoft.Extensions.Logging;
using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Application.Scheduling;
using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;

namespace Qasedak.Api.CrossModule;

/// <summary>
/// Durable delayed-follow-up executor (M13-012 §42-59). At due time, in order:
/// 1. parse the identifier-only payload (run id + action index);
/// 2. load the run — the slot must be Scheduled (a slot recovered as Uncertain after a
///    crash is settled terminally with ZERO sends; an already-settled slot replays);
/// 3. revalidate the automation lifecycle — not Active ⇒ suppress, never send;
/// 4. resolve the exact pinned frozen version (never CurrentDefinition) for content;
/// 5. revalidate the exact connected account + participant and the 24h window through
///    the channel-neutral eligibility port (Meta stays the final send authority);
/// 6. persist the durable Attempting marker, dispatch the Direct Message, settle the
///    slot terminally. At-least-once scheduler redelivery can never produce a second
///    send because the run ledger — not the job — is the external-attempt authority.
/// </summary>
public sealed partial class AutomationFollowUpScheduledHandler(
    IAutomationRunRepository runs,
    IAutomationRepository automations,
    FollowUpExecutionUseCase followUps,
    IDirectEligibilityPort eligibility,
    AutomationDirectSendBridge directSend,
    IClock clock,
    ILogger<AutomationFollowUpScheduledHandler> logger) : IScheduledWorkHandler
{
    public string WorkType => FollowUpJobPolicy.WorkType;

    public async Task<WorkOutcome> HandleAsync(ScheduledWorkItem item, CancellationToken cancellationToken)
    {
        var payload = FollowUpJobPolicy.Parse(item.PayloadJson);
        if (payload is null)
        {
            LogMalformed(item.Id);
            return new WorkOutcome.Permanent("followUp.malformedPayload");
        }

        var run = await runs.FindByIdAsync(payload.RunId, cancellationToken);
        if (run is null)
        {
            return new WorkOutcome.Permanent("followUp.runMissing");
        }

        if (payload.ActionIndex < 0 || payload.ActionIndex >= run.Actions.Count)
        {
            return new WorkOutcome.Permanent("followUp.slotMissing");
        }

        var slot = run.Actions[payload.ActionIndex];
        if (slot.Status is AutomationActionStatus.Succeeded
            or AutomationActionStatus.Suppressed
            or AutomationActionStatus.Uncertain
            or AutomationActionStatus.TerminalFailed)
        {
            // At-least-once scheduler redelivery after settlement — nothing to do.
            return WorkOutcome.Succeeded.Instance;
        }

        if (slot.Status == AutomationActionStatus.Attempting)
        {
            // A previous execution died between the durable marker and the outcome — the
            // provider mutation may have happened. Settle Uncertain, zero traffic.
            await followUps.CompleteAsync(
                run.Id, payload.ActionIndex, AutomationActionStatus.Uncertain, "followUp.attemptInterrupted", cancellationToken: cancellationToken);
            LogInterrupted(item.Id, run.Id);
            return WorkOutcome.Succeeded.Instance;
        }

        if (slot.Status != AutomationActionStatus.Scheduled)
        {
            return new WorkOutcome.Permanent("followUp.slotNotScheduled");
        }

        // Lifecycle revalidation: a paused/disabled automation never sends.
        var automation = await automations.FindByIdAsync(run.AutomationId, cancellationToken);
        if (automation is null || automation.Status != AutomationStatus.Active)
        {
            await followUps.SuppressAsync(run.Id, payload.ActionIndex, "automation.notActive", cancellationToken: cancellationToken);
            LogSuppressed(item.Id, run.Id, "automation.notActive");
            return WorkOutcome.Succeeded.Instance;
        }

        // Exact pinned version — content must be reproducible even after unpublish/revision.
        var version = automation.Versions.FirstOrDefault(v => v.Number == run.AutomationVersionNumber);
        if (version is null || payload.ActionIndex >= version.Definition.Actions.Count)
        {
            await followUps.SuppressAsync(run.Id, payload.ActionIndex, "followUp.versionMissing", cancellationToken: cancellationToken);
            return WorkOutcome.Succeeded.Instance;
        }

        var action = version.Definition.Actions[payload.ActionIndex];
        if (action.Kind != ActionKind.ScheduleFollowUp)
        {
            await followUps.SuppressAsync(run.Id, payload.ActionIndex, "followUp.definitionChanged", cancellationToken: cancellationToken);
            return WorkOutcome.Succeeded.Instance;
        }

        if (automation.ChannelAccountId is not { IsResolved: true })
        {
            await followUps.SuppressAsync(run.Id, payload.ActionIndex, "followUp.accountUnavailable", cancellationToken: cancellationToken);
            return WorkOutcome.Succeeded.Instance;
        }

        // Exact-account + participant + window revalidation (no provider call yet).
        var participantId = slot.ProviderRecipientId ?? string.Empty;
        var verdict = await eligibility.EvaluateAsync(run.WorkspaceId, automation.ChannelAccountId.Value, participantId, clock.UtcNow, cancellationToken);
        switch (verdict)
        {
            case DirectEligibility.Eligible:
                break;
            case DirectEligibility.WindowExpired:
                // The 24h window has closed — the effect can never succeed. Terminal safe
                // outcome (Meta remains final authority at send time, §51); never rescheduled.
                await followUps.SuppressAsync(run.Id, payload.ActionIndex, "direct.windowExpired",
                    AutomationActionStatus.TerminalFailed, cancellationToken);
                LogSuppressed(item.Id, run.Id, "direct.windowExpired");
                return WorkOutcome.Succeeded.Instance;
            case DirectEligibility.AccountUnavailable:
                await followUps.SuppressAsync(run.Id, payload.ActionIndex, "followUp.accountUnavailable", cancellationToken: cancellationToken);
                return WorkOutcome.Succeeded.Instance;
            case DirectEligibility.RecipientUnavailable:
                await followUps.SuppressAsync(run.Id, payload.ActionIndex, "followUp.recipientUnavailable", cancellationToken: cancellationToken);
                return WorkOutcome.Succeeded.Instance;
            default:
                // Projection transiently unavailable — retry the job later; no marker yet,
                // no external attempt, so retrying is safe.
                return new WorkOutcome.Retryable("followUp.eligibilityUnknown");
        }

        // Durable marker, then the provider mutation. The guarded transition guarantees
        // exactly one concurrent worker may send; everyone else backs off without traffic.
        var marked = await followUps.MarkAttemptingAsync(run.Id, payload.ActionIndex, cancellationToken);
        if (marked.Status == FollowUpSettlementStatus.AlreadySettled)
        {
            return WorkOutcome.Succeeded.Instance;
        }

        if (!marked.MarkerAcquired)
        {
            return marked.FinalStatus == AutomationActionStatus.Attempting
                ? new WorkOutcome.Retryable("followUp.attemptInProgress")
                : WorkOutcome.Succeeded.Instance;
        }

        var dispatch = new ActionDispatch(
            run.WorkspaceId,
            InstagramReplyGateway.Channel,
            automation.ChannelAccountId,
            participantId,
            action.MessageText,
            run.AutomationId,
            run.AutomationVersionNumber,
            run.TriggerEventId,
            TriggerKind: null,
            TriggerEntityId: null,
            TriggerOccurredAtUtc: null,
            IsLiveComment: false,
            MediaId: null,
            OriginalMediaId: null,
            payload.ActionIndex,
            ActionKind.DirectMessage,
            Extras: null,
            RunId: run.Id);

        var result = await directSend.DispatchAsync(dispatch, cancellationToken);

        if (result.Accepted)
        {
            await followUps.CompleteAsync(
                run.Id, payload.ActionIndex, AutomationActionStatus.Succeeded, null,
                result.ProviderRecipientId, result.ProviderMessageId, cancellationToken);
            LogDelivered(item.Id, run.Id);
            return WorkOutcome.Succeeded.Instance;
        }

        var status = result.TerminalStatus ?? AutomationActionStatus.TerminalFailed;
        await followUps.CompleteAsync(
            run.Id, payload.ActionIndex, status, result.FailureCode ?? "followUp.terminal",
            result.ProviderRecipientId, result.ProviderMessageId, cancellationToken);
        LogSettled(item.Id, run.Id, status);
        return WorkOutcome.Succeeded.Instance;
    }

    [LoggerMessage(EventId = 7300, Level = LogLevel.Warning, Message = "Follow-up job={JobId} malformed payload.")]
    private partial void LogMalformed(Guid jobId);

    [LoggerMessage(EventId = 7301, Level = LogLevel.Warning, Message = "Follow-up job={JobId} recovered interrupted attempt run={RunId} as Uncertain; no send.")]
    private partial void LogInterrupted(Guid jobId, Guid runId);

    [LoggerMessage(EventId = 7302, Level = LogLevel.Information, Message = "Follow-up job={JobId} suppressed run={RunId} reason={Reason}.")]
    private partial void LogSuppressed(Guid jobId, Guid runId, string reason);

    [LoggerMessage(EventId = 7303, Level = LogLevel.Information, Message = "Follow-up job={JobId} delivered run={RunId}.")]
    private partial void LogDelivered(Guid jobId, Guid runId);

    [LoggerMessage(EventId = 7304, Level = LogLevel.Information, Message = "Follow-up job={JobId} settled run={RunId} status={Status}.")]
    private partial void LogSettled(Guid jobId, Guid runId, AutomationActionStatus status);
}
