using Qasedak.BuildingBlocks.Domain;

namespace Qasedak.Modules.Identity.Domain.Workspaces;

/// <summary>
/// Aggregate root for a tenant boundary. All membership mutations flow through this type so
/// the invariants hold atomically:
/// - every workspace always has at least one Owner;
/// - a user holds at most one membership per workspace;
/// - only sufficiently privileged actors may mutate membership (Owner &gt; Admin &gt; Member);
/// - Owner role can be granted or revoked only by an Owner.
/// </summary>
public sealed class Workspace : Entity<Guid>
{
    private readonly List<Membership> _memberships = [];

    private Workspace(Guid id, WorkspaceName name) : base(id)
    {
        Name = name;
    }

    public WorkspaceName Name { get; }

    public IReadOnlyCollection<Membership> Memberships => _memberships.AsReadOnly();

    /// <summary>Creates a workspace whose creator becomes its first and only Owner.</summary>
    public static Workspace Create(WorkspaceName name, Guid ownerUserId)
    {
        var workspace = new Workspace(Guid.CreateVersion7(), name);
        workspace._memberships.Add(
            new Membership(Guid.CreateVersion7(), workspace.Id, ownerUserId, MembershipRole.Owner));
        return workspace;
    }

    /// <summary>Rehydrates an existing workspace from persistence without re-running creation rules.</summary>
    public static Workspace FromState(
        Guid id,
        WorkspaceName name,
        IEnumerable<(Guid MembershipId, Guid UserId, MembershipRole Role)> memberships)
    {
        var workspace = new Workspace(id, name);
        foreach (var (membershipId, userId, role) in memberships)
        {
            workspace._memberships.Add(new Membership(membershipId, id, userId, role));
        }

        if (!workspace.HasOwner)
        {
            throw new DomainRuleViolationException(
                "workspace.lastOwnerProtected",
                "A persisted workspace must contain at least one owner.");
        }

        return workspace;
    }

    /// <summary>Adds a user as a Member or Admin. Only an Owner can grant Owner via TransferOwnership.</summary>
    public Membership AddMember(Guid actingUserId, MembershipRole actingRole, Guid userId, MembershipRole role)
    {
        EnsureActorCanManageMemberships(actingUserId, actingRole);

        if (role == MembershipRole.Owner)
        {
            throw new DomainRuleViolationException(
                "workspace.ownerOnlyViaTransfer",
                "The Owner role is granted exclusively through ownership transfer.");
        }

        if (_memberships.Exists(m => m.UserId == userId))
        {
            throw new DomainRuleViolationException(
                "workspace.membershipDuplicate",
                "The user already holds a membership in this workspace.");
        }

        var membership = new Membership(Guid.CreateVersion7(), Id, userId, role);
        _memberships.Add(membership);
        return membership;
    }

    /// <summary>
    /// Changes a member's role. Admins may toggle between Member and Admin; only an Owner may
    /// grant or revoke Owner, and demotion of the last remaining owner is refused.
    /// </summary>
    public void ChangeMemberRole(Guid actingUserId, MembershipRole actingRole, Guid userId, MembershipRole newRole)
    {
        EnsureActorCanManageMemberships(actingUserId, actingRole);

        var target = RequireMembership(userId);

        if ((actingRole == MembershipRole.Admin && actingUserId != userId)
            && (newRole == MembershipRole.Owner || target.Role == MembershipRole.Owner))
        {
            throw new DomainRuleViolationException(
                "workspace.ownerChangeRequiresOwner",
                "Only an owner can grant or change the Owner role.");
        }

        if (target.Role == MembershipRole.Owner
            && newRole != MembershipRole.Owner
            && CountOwners == 1)
        {
            throw new DomainRuleViolationException(
                "workspace.lastOwnerProtected",
                "The last owner cannot be demoted; transfer ownership first.");
        }

        target.Role = newRole;
    }

    /// <summary>Transfers ownership: the target becomes Owner and the current owner becomes Admin.</summary>
    public void TransferOwnership(Guid actingUserId, MembershipRole actingRole, Guid fromUserId, Guid toUserId)
    {
        if (actingRole != MembershipRole.Owner)
        {
            throw new DomainRuleViolationException(
                "workspace.actorNotAuthorized",
                "Ownership transfer requires the acting user to be an owner.");
        }

        if (fromUserId != actingUserId)
        {
            throw new DomainRuleViolationException(
                "workspace.transferFromMustBeSelf",
                "An owner can transfer only their own ownership.");
        }

        var source = RequireMembership(fromUserId);
        var target = RequireMembership(toUserId);

        if (source.Role != MembershipRole.Owner)
        {
            throw new DomainRuleViolationException(
                "workspace.transferSourceNotOwner",
                "Ownership transfer requires the source to currently be an owner.");
        }

        if (toUserId == fromUserId)
        {
            throw new DomainRuleViolationException(
                "workspace.transferToSelf",
                "Ownership transfer to self has no effect.");
        }

        source.Role = MembershipRole.Admin;
        target.Role = MembershipRole.Owner;
    }

    /// <summary>
    /// Removes a member. Admins may remove Members only; Owners may remove any non-owner or
    /// themselves/co-owners as long as at least one Owner remains afterwards.
    /// </summary>
    public void RemoveMember(Guid actingUserId, MembershipRole actingRole, Guid userId)
    {
        EnsureActorCanManageMemberships(actingUserId, actingRole);

        var target = RequireMembership(userId);

        var actorIsPrivilegedEnough =
            actingRole == MembershipRole.Owner
            || (actingRole == MembershipRole.Admin && target.Role == MembershipRole.Member);

        if (!actorIsPrivilegedEnough)
        {
            throw new DomainRuleViolationException(
                "workspace.actorNotAuthorized",
                "Admins may remove members only.");
        }

        if (target.Role == MembershipRole.Owner && CountOwners == 1)
        {
            throw new DomainRuleViolationException(
                "workspace.lastOwnerProtected",
                "The last owner cannot leave or be removed.");
        }

        _memberships.RemoveAll(m => m.UserId == userId);
    }

    private bool HasOwner => _memberships.Exists(m => m.Role == MembershipRole.Owner);

    private int CountOwners => _memberships.Count(m => m.Role == MembershipRole.Owner);

    private void EnsureActorCanManageMemberships(Guid actingUserId, MembershipRole actingRole)
    {
        if (actingRole is not (MembershipRole.Owner or MembershipRole.Admin))
        {
            throw new DomainRuleViolationException(
                "workspace.actorNotAuthorized",
                "Only owners and admins manage workspace memberships.");
        }

        if (!_memberships.Exists(m => m.UserId == actingUserId))
        {
            throw new DomainRuleViolationException(
                "workspace.actorNotMember",
                "The acting user does not belong to this workspace.");
        }
    }

    private Membership RequireMembership(Guid userId) =>
        _memberships.FirstOrDefault(m => m.UserId == userId)
        ?? throw new DomainRuleViolationException(
            "workspace.membershipMissing",
            "No such membership exists in this workspace.");
}
