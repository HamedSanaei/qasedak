using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Domain.Accounts;

namespace Qasedak.Modules.Instagram.Infrastructure.Persistence;

/// <summary>EF Core repository over connected-account aggregates.</summary>
public sealed class EfConnectedAccountRepository(InstagramDbContext context) : IConnectedAccountRepository
{
    /// <summary>Tracked load: callers may mutate the aggregate (disconnect/rotation) and save.</summary>
    public Task<ConnectedAccount?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Accounts.SingleOrDefaultAsync(a => a.Id == id, cancellationToken)!;

    /// <summary>Read-only duplicate check; no tracking needed.</summary>
    public Task<ConnectedAccount?> FindByProviderIdentityAsync(
        Guid workspaceId, string providerUserId, CancellationToken cancellationToken = default) =>
        context.Accounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.WorkspaceId == workspaceId && a.ProviderUserId == providerUserId, cancellationToken)!;

    /// <summary>Workspace resolution for cross-module event routing; read-only.</summary>
    public Task<Guid?> FindWorkspaceIdByProviderIdentityAsync(string providerUserId, CancellationToken cancellationToken = default) =>
        context.Accounts.AsNoTracking()
            .Where(a => a.ProviderUserId == providerUserId)
            .Select(a => (Guid?)a.WorkspaceId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ConnectedAccount>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var rows = await context.Accounts
            .AsNoTracking()
            .Where(a => a.WorkspaceId == workspaceId)
            .ToArrayAsync(cancellationToken);

        return rows;
    }

    public Task AddAsync(ConnectedAccount account, CancellationToken cancellationToken = default) =>
        context.Accounts.AddAsync(account, cancellationToken).AsTask();

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
