using Qasedak.Modules.Instagram.Domain.Accounts;

namespace Qasedak.Modules.Instagram.Application.Accounts;

/// <summary>Persistence contract for connected-account aggregates.</summary>
public interface IConnectedAccountRepository
{
    Task<ConnectedAccount?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds an account in a workspace by its canonical provider account identity
    /// (the OAuth user_id, which for Instagram Login is the professional IG_ID and
    /// the webhook entry.id routing identity). Returns the first match regardless
    /// of disconnect state: connect-flow duplicate detection only, never routing.
    /// </summary>
    Task<ConnectedAccount?> FindByProviderIdentityAsync(
        Guid workspaceId, string providerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deterministic inbound-routing resolution: the one ACTIVE connected account
    /// carrying this webhook routing identity, across all workspaces, in one query.
    /// Row order never influences the outcome.
    /// </summary>
    Task<AccountResolution> ResolveActiveAccountAsync(string providerAccountId, CancellationToken cancellationToken = default);

    /// <summary>Lists all accounts of a workspace (any health/disconnect state).</summary>
    Task<IReadOnlyList<ConnectedAccount>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    Task AddAsync(ConnectedAccount account, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Deterministic outcome of routing-identity resolution (never first-match).</summary>
public enum AccountResolutionStatus
{
    /// <summary>Exactly one active account carries the identity.</summary>
    Resolved,

    /// <summary>No active account carries the identity (unknown or all disconnected).</summary>
    NotFound,

    /// <summary>Several active accounts carry the identity: fail closed, never choose.</summary>
    Ambiguous,
}

/// <summary>Structured inbound-routing verdict for one webhook account identity.</summary>
public sealed record AccountResolution(AccountResolutionStatus Status, ConnectedAccount? Account)
{
    public static AccountResolution Resolved(ConnectedAccount account) => new(AccountResolutionStatus.Resolved, account);

    public static AccountResolution NotFound() => new(AccountResolutionStatus.NotFound, null);

    public static AccountResolution Ambiguous() => new(AccountResolutionStatus.Ambiguous, null);
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

    /// <summary>The professional account is actively connected in another workspace.</summary>
    public const string AlreadyConnectedElsewhere = "account.alreadyConnectedElsewhere";

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
