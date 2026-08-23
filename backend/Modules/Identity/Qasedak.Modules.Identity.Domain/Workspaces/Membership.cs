using Qasedak.BuildingBlocks.Domain;

namespace Qasedak.Modules.Identity.Domain.Workspaces;

/// <summary>
/// A user's role-bearing tie to exactly one workspace. Memberships are owned by the
/// Workspace aggregate: only the aggregate mutates them, guaranteeing workspace-wide
/// invariants (at least one owner, no duplicates).
/// </summary>
public sealed class Membership : Entity<Guid>
{
    internal Membership(Guid id, Guid workspaceId, Guid userId, MembershipRole role)
        : base(id)
    {
        WorkspaceId = workspaceId;
        UserId = userId;
        Role = role;
    }

    public Guid WorkspaceId { get; }

    public Guid UserId { get; }

    public MembershipRole Role { get; internal set; }
}
