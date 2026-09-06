using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Conversations.Application.Conversations;

namespace Qasedak.Api.CrossModule;

/// <summary>
/// Binds the Automations module's channel-neutral dispatcher port to the outbound
/// operations. Routing is origin-aware (M13-009): an action fired by a COMMENT trigger
/// is a first-contact response and MUST use the comment-ID-addressed Private Reply
/// operation — never a normal recipient.id DM (a comment does not open the 24h DM
/// window; normal sends fail with 10/2534022 and would consume the one allowed reply
/// incorrectly). Established conversation replies keep the direct-message gateway path
/// (recipient.id, 24h-window policy enforced there).
/// </summary>
public sealed class AutomationChannelDispatcher(
    IConversationChannelGateway gateway,
    AutomationPrivateReplyBridge privateReplyBridge) : IAutomationActionDispatcher
{
    public async Task<ActionResult> DispatchAsync(ActionDispatch dispatch, CancellationToken cancellationToken = default)
    {
        if (dispatch.TriggerKind == Qasedak.Modules.Automations.Domain.Definitions.TriggerKind.CommentCreated
            && dispatch.ActionKind == Qasedak.Modules.Automations.Domain.Definitions.ActionKind.SendDirectMessage)
        {
            return await privateReplyBridge.DispatchAsync(dispatch, cancellationToken);
        }

        var result = await gateway.DeliverAsync(
            new ChannelDeliveryRequest(dispatch.WorkspaceId, dispatch.Channel, dispatch.ChannelAccountId, dispatch.ParticipantId, dispatch.MessageText),
            cancellationToken);

        return result.Accepted ? ActionResult.Delivered() : ActionResult.Rejected(result.FailureCode ?? "action.rejected");
    }
}
