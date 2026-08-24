namespace Qasedak.Modules.Identity.Application.Workspaces;

/// <summary>
/// Answers workspace-membership questions for composition-root authorization without
/// exposing domain internals across module boundaries.
/// </summary>
public interface IWorkspaceAccessChecker
{
    Task<bool> IsMemberAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default);
}
