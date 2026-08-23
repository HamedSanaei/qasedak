using Qasedak.Modules.Instagram.Application.Webhooks;

namespace Qasedak.Api.CrossModule;

/// <summary>
/// Fans one normalized integration event out to every composition-root consumer
/// (Conversations projection, Automations engine). The Instagram module resolves a single
/// <see cref="IIntegrationEventDispatcher"/>; the composition root owns the fan-out so new
/// consumers join without touching the module.
/// </summary>
public sealed class FanOutIntegrationEventDispatcher(IReadOnlyList<IIntegrationEventDispatcher> dispatchers)
    : IIntegrationEventDispatcher
{
    public async Task DispatchAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        foreach (var dispatcher in dispatchers)
        {
            await dispatcher.DispatchAsync(integrationEvent, cancellationToken);
        }
    }
}
