using Qasedak.Modules.Instagram.Domain.Accounts;

namespace Qasedak.Modules.Instagram.Application.Accounts;

/// <summary>Persistence contract for connected-account aggregates.</summary>
public interface IConnectedAccountRepository
{
    Task<ConnectedAccount?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Finds an account in a workspace by its app-scoped provider identity.</summary>
    Task<ConnectedAccount?> FindByProviderIdentityAsync(
        Guid workspaceId, string providerUserId, CancellationToken cancellationToken = default);

    /// <summary>Lists all accounts of a workspace (any health/disconnect state).</summary>
    Task<IReadOnlyList<ConnectedAccount>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    Task AddAsync(ConnectedAccount account, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Protected storage for raw token material keyed by account id. Implementations encrypt at
/// rest before production use; token values never appear in logs, API responses or the domain.
/// </summary>
public interface IProtectedTokenStore
{
    /// <summary>Stores (or atomically replaces) the current access token for an account.</summary>
    Task StoreAsync(Guid accountId, string accessToken, CancellationToken cancellationToken = default);

    /// <summary>Returns the stored access token for the account, or null when absent.</summary>
    Task<string?> GetAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>Irrevocably deletes stored material (disconnect/revoke path).</summary>
    Task DeleteAsync(Guid accountId, CancellationToken cancellationToken = default);
}

/// <summary>Stable failure codes surfaced by account lifecycle use cases.</summary>
public static class AccountFailures
{
    public const string NotFound = "account.notFound";

    public const string AlreadyConnected = "account.alreadyConnected";

    public const string AlreadyDisconnected = "account.alreadyDisconnected";

    public const string OAuthRejected = "account.oauthRejected";

    public const string OAuthUnavailable = "account.oauthUnavailable";
}

/// <summary>A single connection-state projection per the API contract sketch; no token values.</summary>
public readonly record struct ConnectionStateRecord(
    Guid AccountId,
    Guid WorkspaceId,
    string ProviderIdentity,
    string Path,
    IReadOnlyList<string> Scopes,
    string Health,
    string? HealthDetail,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset ConnectedAtUtc,
    DateTimeOffset? DisconnectedAtUtc);
