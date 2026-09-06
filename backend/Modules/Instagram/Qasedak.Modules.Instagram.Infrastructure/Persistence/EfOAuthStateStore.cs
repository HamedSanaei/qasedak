using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Instagram.Application.Accounts;

namespace Qasedak.Modules.Instagram.Infrastructure.Persistence;

/// <summary>
/// Durable OAuth-state store: hash-persisted, workspace/redirect-bound, short-lived,
/// atomically consumable. Safe for multi-instance deployment (no process memory).
/// </summary>
public sealed class EfOAuthStateStore(InstagramDbContext context) : IOAuthStateStore
{
    public async Task<OAuthStateIssuance> IssueAsync(Guid workspaceId, string redirectUri, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var normalizedRedirect = redirectUri.Trim();
        // Opportunistic purge keeps the table bounded: states are single-use and
        // short-lived, so expired rows are garbage. Best-effort, same scope.
        await context.OAuthStates
            .Where(r => r.ExpiresAtUtc <= now)
            .ExecuteDeleteAsync(cancellationToken);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var state = ToUrlSafe(Convert.ToBase64String(RandomNumberGenerator.GetBytes(OAuthStatePolicy.StateByteLength)));
            var row = new OAuthStateRow
            {
                StateHash = Hash(state),
                WorkspaceId = workspaceId,
                RedirectUri = normalizedRedirect,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.Add(OAuthStatePolicy.Lifetime),
            };
            context.OAuthStates.Add(row);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return new OAuthStateIssuance(state, row.ExpiresAtUtc);
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            {
                // Astronomically unlikely hash collision: mint fresh state material.
                context.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException("OAuth state issuance collided repeatedly.");
    }

    public async Task<OAuthStateConsumption> ConsumeAsync(string state, Guid workspaceId, string redirectUri, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var hash = Hash(state ?? string.Empty);
        var row = await context.OAuthStates.AsNoTracking()
            .SingleOrDefaultAsync(r => r.StateHash == hash, cancellationToken);
        if (row is null)
        {
            return OAuthStateConsumption.NotFound;
        }

        if (row.WorkspaceId != workspaceId)
        {
            return OAuthStateConsumption.WorkspaceMismatch;
        }

        if (!string.Equals(row.RedirectUri, redirectUri.Trim(), StringComparison.Ordinal))
        {
            return OAuthStateConsumption.RedirectMismatch;
        }

        if (row.ExpiresAtUtc <= now)
        {
            return OAuthStateConsumption.Expired;
        }

        if (row.ConsumedAtUtc is not null)
        {
            return OAuthStateConsumption.AlreadyConsumed;
        }

        // Atomic one-time consume: exactly one concurrent caller wins the row.
        var consumed = await context.OAuthStates
            .Where(r => r.StateHash == hash && r.ConsumedAtUtc == null && r.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(updates => updates.SetProperty(r => r.ConsumedAtUtc, now), cancellationToken);
        return consumed == 1 ? OAuthStateConsumption.Consumed : OAuthStateConsumption.AlreadyConsumed;
    }

    internal static string Hash(string state)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(state));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string ToUrlSafe(string base64) =>
        base64.TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        for (var current = (Exception?)exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("23505", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
