using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Automations.Domain.Definitions;

namespace Qasedak.Api.CrossModule;

/// <summary>
/// Binds the Automations module's channel-neutral dispatcher port to the outbound
/// operations (M13-012 §29-31, §57). Routing is origin-aware: the legacy
/// <see cref="ActionKind.SendDirectMessage"/> fired by a COMMENT trigger is a
/// first-contact response and MUST use the comment-ID-addressed Private Reply operation —
/// never a normal recipient.id DM (a comment does not open the 24h DM window; normal
/// sends fail with 10/2534022 and would consume the one allowed reply incorrectly).
/// Explicit kinds route to their dedicated operation; the run ledger's durable Attempting
/// marker (persisted by the use case before this call) makes every provider-attempted
/// outcome terminal.
/// </summary>
public sealed class AutomationChannelDispatcher(
    AutomationPrivateReplyBridge privateReplyBridge,
    AutomationDirectSendBridge directSendBridge,
    AutomationRevealBridge revealBridge,
    AutomationPublicReplyBridge publicReplyBridge,
    AutomationFollowUpBridge followUpBridge) : IAutomationActionDispatcher
{
    public async Task<ActionResult> DispatchAsync(ActionDispatch dispatch, CancellationToken cancellationToken = default)
    {
        switch (dispatch.ActionKind)
        {
            case ActionKind.SendDirectMessage:
                // Legacy origin-aware semantics (M13-012 §9): comment origin ⇒ Private Reply.
                return dispatch.TriggerKind == TriggerKind.CommentCreated
                    ? await privateReplyBridge.DispatchAsync(dispatch, cancellationToken)
                    : await directSendBridge.DispatchAsync(dispatch, cancellationToken);

            case ActionKind.SendPrivateReply:
                return await privateReplyBridge.DispatchAsync(dispatch, cancellationToken);

            case ActionKind.DirectMessage:
                return await directSendBridge.DispatchAsync(dispatch, cancellationToken);

            case ActionKind.StartRevealFlow:
                return await revealBridge.DispatchAsync(dispatch, cancellationToken);

            case ActionKind.SendPublicReply:
                return await publicReplyBridge.DispatchAsync(dispatch, cancellationToken);

            case ActionKind.ScheduleFollowUp:
                return await followUpBridge.DispatchAsync(dispatch, cancellationToken);

            default:
                return ActionResult.TerminalFailed("action.unsupportedKind");
        }
    }
}
