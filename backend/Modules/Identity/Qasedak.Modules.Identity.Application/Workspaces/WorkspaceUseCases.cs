using Qasedak.Modules.Identity.Application.Authentication;
using Qasedak.Modules.Identity.Domain.Workspaces;

namespace Qasedak.Modules.Identity.Application.Workspaces;

/// <summary>Stable failure codes surfaced by workspace-boundary use cases.</summary>
public static class WorkspaceFailures
{
    public const string InvalidName = "workspace.invalidName";

    public const string UnknownActor = "workspace.unknownActor";

    public const string NotFound = "workspace.notFound";

    /// <summary>Authenticated but not a member of the requested workspace.</summary>
    public const string Forbidden = "workspace.forbidden";
}

/// <summary>Creates a workspace whose creator becomes its first Owner.</summary>
public sealed class CreateWorkspaceUseCase(IUserRepository users, IWorkspaceRepository workspaces)
{
    public async Task<CreateWorkspaceResult> ExecuteAsync(Guid actingUserId, string name, CancellationToken cancellationToken = default)
    {
        if (!WorkspaceName.TryCreate(name, out var workspaceName))
        {
            return CreateWorkspaceResult.Fail(WorkspaceFailures.InvalidName);
        }

        var actor = await users.FindByIdAsync(actingUserId, cancellationToken);
        if (actor is null)
        {
            return CreateWorkspaceResult.Fail(WorkspaceFailures.UnknownActor);
        }

        var workspace = Workspace.Create(workspaceName, actingUserId);
        await workspaces.AddAsync(workspace, cancellationToken);
        await workspaces.SaveChangesAsync(cancellationToken);

        return CreateWorkspaceResult.Ok(workspace.Id, workspace.Name.Value);
    }
}

/// <summary>Outcome of creating a workspace.</summary>
public readonly record struct CreateWorkspaceResult(bool Success, Guid WorkspaceId, string Name, string? FailureCode)
{
    public static CreateWorkspaceResult Ok(Guid workspaceId, string name) =>
        new(true, workspaceId, name, null);

    public static CreateWorkspaceResult Fail(string failureCode) =>
        new(false, Guid.Empty, string.Empty, failureCode);
}

/// <summary>Lists members of a workspace, gated on caller membership at the boundary.</summary>
public sealed class ListWorkspaceMembersUseCase(IWorkspaceRepository workspaces)
{
    public async Task<WorkspaceMembersResult> ExecuteAsync(Guid actingUserId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var workspace = await workspaces.FindByIdAsync(workspaceId, cancellationToken);
        if (workspace is null)
        {
            return WorkspaceMembersResult.Fail(WorkspaceFailures.NotFound);
        }

        if (!workspace.Memberships.Any(m => m.UserId == actingUserId))
        {
            return WorkspaceMembersResult.Fail(WorkspaceFailures.Forbidden);
        }

        var members = workspace.Memberships
            .Select(m => new WorkspaceMember(m.UserId, m.Role))
            .ToArray();

        return WorkspaceMembersResult.Ok(workspace.Name.Value, members);
    }
}

/// <summary>A single workspace member projection.</summary>
public readonly record struct WorkspaceMember(Guid UserId, MembershipRole Role);

/// <summary>Outcome of listing workspace members.</summary>
public readonly record struct WorkspaceMembersResult(
    bool Success,
    string WorkspaceName,
    IReadOnlyList<WorkspaceMember> Members,
    string? FailureCode)
{
    public static WorkspaceMembersResult Ok(string workspaceName, IReadOnlyList<WorkspaceMember> members) =>
        new(true, workspaceName, members, null);

    public static WorkspaceMembersResult Fail(string failureCode) =>
        new(false, string.Empty, [], failureCode);
}
