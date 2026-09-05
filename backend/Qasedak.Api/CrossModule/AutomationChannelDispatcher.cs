using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Conversations.Application.Conversations;

namespace Qasedak.Api.CrossModule;

/// <summary>
/// Binds the Automations module's channel-neutral dispatcher port to the outbound channel
/// gateway (filled by Instagram). Policy restrictions — including the 24-hour messaging
/// window — are enforced inside the gateway and surface here as stable failure codes that
/// the execution ledger records per action slot.
/// </summary>
public sealed class AutomationChannelDispatcher(IConversationChannelGateway gateway) : IAutomationActionDispatcher
{
    public async Task<ActionResult> DispatchAsync(ActionDispatch dispatch, CancellationToken cancellationToken = default)
    {
        var result = await gateway.DeliverAsync(
            new ChannelDeliveryRequest(dispatch.WorkspaceId, dispatch.Channel, dispatch.ChannelAccountId, dispatch.ParticipantId, dispatch.MessageText),
            cancellationToken);

        return result.Accepted ? ActionResult.Delivered() : ActionResult.Rejected(result.FailureCode ?? "action.rejected");
    }
}
