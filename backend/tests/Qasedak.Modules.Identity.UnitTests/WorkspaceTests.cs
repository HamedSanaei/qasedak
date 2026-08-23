using Qasedak.Modules.Identity.Domain;
using Qasedak.Modules.Identity.Domain.Workspaces;
using Xunit;

namespace Qasedak.Modules.Identity.UnitTests;

public sealed class WorkspaceTests
{
    private readonly Guid _ownerId = Guid.CreateVersion7();

    private readonly Guid _adminId = Guid.CreateVersion7();

    private readonly Guid _memberId = Guid.CreateVersion7();

    private readonly Guid _outsiderId = Guid.CreateVersion7();

    [Fact]
    public void CreateSeedsExactlyOneOwnerMembership()
    {
        var workspace = NewWorkspace();

        var membership = Assert.Single(workspace.Memberships);
        Assert.Equal(_ownerId, membership.UserId);
        Assert.Equal(MembershipRole.Owner, membership.Role);
    }

    [Fact]
    public void OwnerAddsAdminAndMember()
    {
        var workspace = NewWorkspace();
        workspace.AddMember(_ownerId, MembershipRole.Owner, _adminId, MembershipRole.Admin);
        workspace.AddMember(_ownerId, MembershipRole.Owner, _memberId, MembershipRole.Member);

        Assert.Equal(3, workspace.Memberships.Count);
        Assert.Contains(workspace.Memberships, m => m.UserId == _adminId && m.Role == MembershipRole.Admin);
        Assert.Contains(workspace.Memberships, m => m.UserId == _memberId && m.Role == MembershipRole.Member);
    }

    [Fact]
    public void AdminCanAddMember()
    {
        var workspace = NewWorkspace(withAdmin: true);

        var added = workspace.AddMember(_adminId, MembershipRole.Admin, _memberId, MembershipRole.Member);

        Assert.Equal(MembershipRole.Member, added.Role);
    }

    [Fact]
    public void MemberCannotAddMembers()
    {
        var workspace = NewWorkspace(withAdmin: true, withMember: true);

        var violation = Assert.Throws<DomainRuleViolationException>(
            () => workspace.AddMember(_memberId, MembershipRole.Member, Guid.CreateVersion7(), MembershipRole.Member));

        Assert.Equal("workspace.actorNotAuthorized", violation.RuleCode);
    }

    [Fact]
    public void NonMemberActorCannotAddMembers()
    {
        var workspace = NewWorkspace();

        var violation = Assert.Throws<DomainRuleViolationException>(
            () => workspace.AddMember(_outsiderId, MembershipRole.Admin, _memberId, MembershipRole.Member));

        Assert.Equal("workspace.actorNotMember", violation.RuleCode);
    }

    [Fact]
    public void DuplicateMembershipIsRejected()
    {
        var workspace = NewWorkspace(withMember: true);

        var violation = Assert.Throws<DomainRuleViolationException>(
            () => workspace.AddMember(_ownerId, MembershipRole.Owner, _memberId, MembershipRole.Member));

        Assert.Equal("workspace.membershipDuplicate", violation.RuleCode);
    }

    [Fact]
    public void DirectOwnerGrantIsRejected()
    {
        var workspace = NewWorkspace();

        var violation = Assert.Throws<DomainRuleViolationException>(
            () => workspace.AddMember(_ownerId, MembershipRole.Owner, _memberId, MembershipRole.Owner));

        Assert.Equal("workspace.ownerOnlyViaTransfer", violation.RuleCode);
    }

    [Fact]
    public void OwnerPromotesMemberToAdmin()
    {
        var workspace = NewWorkspace(withMember: true);

        workspace.ChangeMemberRole(_ownerId, MembershipRole.Owner, _memberId, MembershipRole.Admin);

        Assert.Equal(MembershipRole.Admin, RoleOf(workspace, _memberId));
    }

    [Fact]
    public void AdminCannotChangeAnOwnersRole()
    {
        var workspace = NewWorkspace(withAdmin: true);

        var violation = Assert.Throws<DomainRuleViolationException>(
            () => workspace.ChangeMemberRole(_adminId, MembershipRole.Admin, _ownerId, MembershipRole.Admin));

        Assert.Equal("workspace.ownerChangeRequiresOwner", violation.RuleCode);
    }

    [Fact]
    public void AdminCannotGrantOwnership()
    {
        var workspace = NewWorkspace(withAdmin: true, withMember: true);

        var violation = Assert.Throws<DomainRuleViolationException>(
            () => workspace.ChangeMemberRole(_adminId, MembershipRole.Admin, _memberId, MembershipRole.Owner));

        Assert.Equal("workspace.ownerChangeRequiresOwner", violation.RuleCode);
    }

    [Fact]
    public void LastOwnerDemotionIsRejected()
    {
        var workspace = NewWorkspace();

        var violation = Assert.Throws<DomainRuleViolationException>(
            () => workspace.ChangeMemberRole(_ownerId, MembershipRole.Owner, _ownerId, MembershipRole.Admin));

        Assert.Equal("workspace.lastOwnerProtected", violation.RuleCode);
    }

    [Fact]
    public void DemotionIsAllowedOnceSecondOwnerExists()
    {
        var workspace = NewWorkspace();
        workspace.TransferOwnership(_ownerId, MembershipRole.Owner, fromUserId: _ownerId, toUserId: AddMemberAs(workspace, _ownerId, MembershipRole.Owner));
        // After transfer the original owner is an admin and the new owner is the transferred-to user.

        var newOwnerId = workspace.Memberships.Single(m => m.Role == MembershipRole.Owner).UserId;
        workspace.ChangeMemberRole(newOwnerId, MembershipRole.Owner, _ownerId, MembershipRole.Member);

        Assert.Equal(MembershipRole.Member, RoleOf(workspace, _ownerId));
    }

    [Fact]
    public void OwnershipTransferSwapsRoles()
    {
        var workspace = NewWorkspace();
        var targetId = AddMemberAs(workspace, _ownerId, MembershipRole.Owner);

        workspace.TransferOwnership(_ownerId, MembershipRole.Owner, fromUserId: _ownerId, toUserId: targetId);

        Assert.Equal(MembershipRole.Owner, RoleOf(workspace, targetId));
        Assert.Equal(MembershipRole.Admin, RoleOf(workspace, _ownerId));
    }

    [Fact]
    public void NonOwnerCannotTransferOwnership()
    {
        var workspace = NewWorkspace(withAdmin: true, withMember: true);

        var violation = Assert.Throws<DomainRuleViolationException>(
            () => workspace.TransferOwnership(_adminId, MembershipRole.Admin, fromUserId: _ownerId, toUserId: _adminId));

        Assert.Equal("workspace.actorNotAuthorized", violation.RuleCode);
    }

    [Fact]
    public void TransferRequiresSourceToBeTheActingOwner()
    {
        var workspace = NewWorkspace(withMember: true);

        var violation = Assert.Throws<DomainRuleViolationException>(
            () => workspace.TransferOwnership(_ownerId, MembershipRole.Owner, fromUserId: Guid.CreateVersion7(), toUserId: _memberId));

        Assert.Equal("workspace.transferFromMustBeSelf", violation.RuleCode);
    }

    [Fact]
    public void TransferToNonMemberIsRejected()
    {
        var workspace = NewWorkspace();

        var violation = Assert.Throws<DomainRuleViolationException>(
            () => workspace.TransferOwnership(_ownerId, MembershipRole.Owner, fromUserId: _ownerId, toUserId: _outsiderId));

        Assert.Equal("workspace.membershipMissing", violation.RuleCode);
    }

    [Fact]
    public void AdminRemovesMemberOnly()
    {
        var workspace = NewWorkspace(withAdmin: true, withMember: true);
        var secondMember = AddMemberAs(workspace, _adminId, MembershipRole.Admin);
        workspace.RemoveMember(_adminId, MembershipRole.Admin, secondMember);
        Assert.Null(RoleOrNull(workspace, secondMember));

        var violation = Assert.Throws<DomainRuleViolationException>(
            () => workspace.RemoveMember(_adminId, MembershipRole.Admin, _ownerId));

        Assert.Equal("workspace.actorNotAuthorized", violation.RuleCode);
    }

    [Fact]
    public void LastOwnerRemovalIsRejected()
    {
        var workspace = NewWorkspace();

        var violation = Assert.Throws<DomainRuleViolationException>(
            () => workspace.RemoveMember(_ownerId, MembershipRole.Owner, _ownerId));

        Assert.Equal("workspace.lastOwnerProtected", violation.RuleCode);
    }

    [Fact]
    public void OwnerMayRemoveCoOwnerWhileAnotherOwnerRemains()
    {
        var workspace = NewWorkspace();
        var coOwner = AddMemberAs(workspace, _ownerId, MembershipRole.Owner);
        workspace.ChangeMemberRole(_ownerId, MembershipRole.Owner, coOwner, MembershipRole.Owner);

        workspace.RemoveMember(coOwner, MembershipRole.Owner, coOwner);

        Assert.Null(RoleOrNull(workspace, coOwner));
        Assert.Equal(MembershipRole.Owner, RoleOf(workspace, _ownerId));
    }

    [Fact]
    public void FromStateRejectsWorkspacesWithoutOwner()
    {
        var id = Guid.CreateVersion7();
        var memberships = new[] { (Guid.CreateVersion7(), Guid.CreateVersion7(), MembershipRole.Member) };

        var violation = Assert.Throws<DomainRuleViolationException>(
            () => Workspace.FromState(id, WorkspaceName.Create("Acme Corp"), memberships));

        Assert.Equal("workspace.lastOwnerProtected", violation.RuleCode);
    }

    [Fact]
    public void FromStateRestoresPersistedMemberships()
    {
        var id = Guid.CreateVersion7();
        var ownerUser = Guid.CreateVersion7();
        var memberUser = Guid.CreateVersion7();
        var memberships = new[]
        {
            (Guid.CreateVersion7(), ownerUser, MembershipRole.Owner),
            (Guid.CreateVersion7(), memberUser, MembershipRole.Member),
        };

        var workspace = Workspace.FromState(id, WorkspaceName.Create("Acme Corp"), memberships);

        Assert.Equal(2, workspace.Memberships.Count);
        Assert.All(workspace.Memberships, m => Assert.Equal(id, m.WorkspaceId));
    }

    private Workspace NewWorkspace(bool withAdmin = false, bool withMember = false)
    {
        var workspace = Workspace.Create(WorkspaceName.Create("Acme Corp"), _ownerId);
        if (withAdmin)
        {
            workspace.AddMember(_ownerId, MembershipRole.Owner, _adminId, MembershipRole.Admin);
        }

        if (withMember)
        {
            workspace.AddMember(_ownerId, MembershipRole.Owner, _memberId, MembershipRole.Member);
        }

        return workspace;
    }

    private static Guid AddMemberAs(Workspace workspace, Guid actingUserId, MembershipRole actingRole)
    {
        var userId = Guid.CreateVersion7();
        workspace.AddMember(actingUserId, actingRole, userId, MembershipRole.Member);
        return userId;
    }

    private static MembershipRole RoleOf(Workspace workspace, Guid userId) =>
        RoleOrNull(workspace, userId)
        ?? throw new InvalidOperationException($"No membership for {userId}.");

    private static MembershipRole? RoleOrNull(Workspace workspace, Guid userId) =>
        workspace.Memberships.FirstOrDefault(m => m.UserId == userId)?.Role;
}
