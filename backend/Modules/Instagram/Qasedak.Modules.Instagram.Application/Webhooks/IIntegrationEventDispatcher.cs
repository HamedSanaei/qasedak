namespace Qasedak.Modules.Instagram.Application.Webhooks;

/// <summary>Boundary toward future consumers of normalized integration events.</summary>
public interface IIntegrationEventDispatcher
{
    Task DispatchAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}
