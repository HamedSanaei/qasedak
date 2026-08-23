using Qasedak.Modules.Identity.Domain.Workspaces;

namespace Qasedak.Modules.Identity.Application.Workspaces;

/// <summary>Persistence contract for workspace aggregates (read with memberships, add graph).</summary>
public interface IWorkspaceRepository
{
    /// <summary>Loads a workspace together with its membership entities.</summary>
    Task<Workspace?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Registers a new workspace aggregate including all its memberships.</summary>
    Task AddAsync(Workspace workspace, CancellationToken cancellationToken = default);

    /// <summary>Persists tracked changes as one atomic unit.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
