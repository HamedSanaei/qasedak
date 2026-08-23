using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Instagram.Application.Accounts;

namespace Qasedak.Modules.Instagram.Infrastructure.Persistence;

/// <summary>
/// Protected token store over the instagram.account_tokens table. Only ciphertext ever
/// reaches persistence; plaintext lives in memory for the duration of a use only.
/// </summary>
public sealed class ProtectedTokenStore(InstagramDbContext context, ITokenProtector protector) : IProtectedTokenStore
{
    public Task StoreAsync(Guid accountId, string accessToken, CancellationToken cancellationToken = default)
    {
        var ciphertext = protector.Protect(accessToken);
        return UpsertAsync(accountId, ciphertext, cancellationToken);
    }

    public async Task<string?> GetAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var ciphertext = await context.AccountTokens
            .AsNoTracking()
            .Where(t => t.AccountId == accountId)
            .Select(t => t.Ciphertext)
            .SingleOrDefaultAsync(cancellationToken);

        return ciphertext is null ? null : protector.Unprotect(ciphertext);
    }

    public async Task DeleteAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await context.AccountTokens
            .Where(t => t.AccountId == accountId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task UpsertAsync(Guid accountId, string ciphertext, CancellationToken cancellationToken)
    {
        var existing = await context.AccountTokens.SingleOrDefaultAsync(t => t.AccountId == accountId, cancellationToken);
        if (existing is null)
        {
            await context.AccountTokens.AddAsync(new StoredAccountToken(accountId, ciphertext), cancellationToken);
        }
        else
        {
            // Atomic rotation: replace the ciphertext in place.
            existing.ReplaceCiphertext(ciphertext);
        }
    }
}
