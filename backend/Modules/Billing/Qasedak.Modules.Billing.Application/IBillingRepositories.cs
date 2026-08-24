using Qasedak.Modules.Billing.Domain;

namespace Qasedak.Modules.Billing.Application;

/// <summary>Plan catalog read/write port.</summary>
public interface IPlanRepository
{
    Task<Plan?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Plan?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Plan>> ListAsync(CancellationToken cancellationToken = default);

    Task SaveChangesAsync(Plan plan, CancellationToken cancellationToken = default);
}

/// <summary>Subscription persistence port (one live subscription per workspace).</summary>
public interface ISubscriptionRepository
{
    Task<Subscription?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The workspace's current subscription, whatever its state; null when none.</summary>
    Task<Subscription?> FindByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(Subscription subscription, CancellationToken cancellationToken = default);
}
