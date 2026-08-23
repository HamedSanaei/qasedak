namespace Qasedak.Modules.Identity.Domain.Workspaces;

/// <summary>
/// Workspace roles ordered from most to least privileged. Ordering is part of the contract:
/// comparisons such as role &lt;= MembershipRole.Member are used for authorization decisions.
/// </summary>
public enum MembershipRole
{
    Owner = 1,

    Admin = 2,

    Member = 3,
}
