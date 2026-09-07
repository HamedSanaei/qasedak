using Qasedak.Modules.Instagram.Application.RevealFlow;
using Qasedak.Modules.Instagram.Application.Webhooks;

namespace Qasedak.Api.CrossModule;

/// <summary>
/// Composition-root continuation bridge (M13-011): normalized inbound events continue
/// pending reveal flows. A user message is ONLY a continuation of an already-started
/// flow — it is never a generic DM automation trigger (M13-012 owns that). A postback is
/// consumed ONLY when its payload validates as an rv1 reveal correlation; all other
/// postbacks remain available to future consumers. Starting a flow is invocation-owned
/// (content is supplied explicitly; M13-012 maps automation configuration into
/// <see cref="StartRevealFlowCommand"/>); the continuation steps need no content.
/// </summary>
public sealed class RevealFlowContinuationBridge(RevealFlowCoordinator coordinator)
    : IIntegrationEventDispatcher
{
    public async Task DispatchAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        switch (integrationEvent)
        {
            case InstagramMessageReceived message:
                await coordinator.OnUserResponseAsync(message, cancellationToken);
                break;
            case InstagramPostbackReceived postback:
                await coordinator.OnPostbackAsync(postback, cancellationToken);
                break;
            default:
                break;
        }
    }
}
