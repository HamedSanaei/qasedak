using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Identity.Application.Workspaces;
using Qasedak.Modules.Identity.Infrastructure.Persistence;

namespace Qasedak.Modules.Identity.Infrastructure.Security;

/// <summary>Membership lookups backed by the identity persistence model.</summary>
public sealed class EfWorkspaceAccessChecker(IdentityDbContext context) : IWorkspaceAccessChecker
{
    public Task<bool> IsMemberAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default) =>
        context.Memberships.AsNoTracking()
            .AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, cancellationToken);
}
