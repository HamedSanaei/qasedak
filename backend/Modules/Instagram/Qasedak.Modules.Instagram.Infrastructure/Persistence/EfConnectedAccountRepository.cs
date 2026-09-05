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

    /// <summary>
    /// Single-query deterministic routing resolution over active rows only.
    /// Disconnected history can never shadow the active account, and duplicate
    /// active owners surface as Ambiguous instead of an order-dependent pick.
    /// </summary>
    public async Task<AccountResolution> ResolveActiveAccountAsync(string providerAccountId, CancellationToken cancellationToken = default)
    {
        var active = await context.Accounts.AsNoTracking()
            .Where(a => a.ProviderUserId == providerAccountId && a.DisconnectedAtUtc == null)
            .OrderBy(a => a.Id)
            .ToListAsync(cancellationToken);

        return active.Count switch
        {
            0 => AccountResolution.NotFound(),
            1 => AccountResolution.Resolved(active[0]),
            _ => AccountResolution.Ambiguous(),
        };
    }

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
