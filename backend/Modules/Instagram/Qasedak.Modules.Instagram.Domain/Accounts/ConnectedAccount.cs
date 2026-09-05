namespace Qasedak.Modules.Instagram.Domain.Accounts;

/// <summary>
/// A workspace's connected Instagram professional account. The aggregate owns connection
/// metadata and health state only: raw token material never lives here — it is held by the
/// module's protected token store keyed by account id (lifecycle contract §4).
/// Workspace reference is a stable identifier; no cross-module project reference exists.
/// </summary>
public sealed class ConnectedAccount
{
    private readonly List<string> _scopes = [];

    private ConnectedAccount(
        Guid id,
        Guid workspaceId,
        string providerUserId,
        ConnectionPath path,
        DateTimeOffset connectedAtUtc)
    {
        Id = id;
        WorkspaceId = workspaceId;
        ProviderUserId = providerUserId;
        Path = path;
        ConnectedAtUtc = connectedAtUtc;
    }

    public Guid Id { get; private init; }

    /// <summary>Stable identifier of the owning workspace (no cross-module reference).</summary>
    public Guid WorkspaceId { get; private init; }

    /// <summary>
    /// Canonical provider account routing identity. For Instagram Login this is the
    /// Instagram professional account ID (IG_ID): Meta guarantees the OAuth
    /// code-exchange user_id equals the IG_ID carried by webhook entry.id, so the
    /// value stored at connect time routes webhooks without further mapping
    /// (meta-instagram-platform-contract.md §2/Outcome A). Never an IGSID, mid or
    /// comment id.
    /// </summary>
    public string ProviderUserId { get; private init; }

    public ConnectionPath Path { get; private init; }

    public IReadOnlyList<string> Scopes => _scopes;

    public AccountHealth Health { get; private set; } = AccountHealth.Connected;

    /// <summary>Actionable detail for non-Connected health states; null when healthy.</summary>
    public string? HealthDetail { get; private set; }

    /// <summary>Expiry of the current long-lived token; null for never-expiring FB Page tokens.</summary>
    public DateTimeOffset? TokenExpiresAtUtc { get; private set; }

    public DateTimeOffset ConnectedAtUtc { get; private init; }

    public DateTimeOffset? DisconnectedAtUtc { get; private set; }

    public bool IsDisconnected => DisconnectedAtUtc is not null;

    /// <summary>Creates a connected account in Connected state from a completed OAuth flow.</summary>
    public static ConnectedAccount Create(
        Guid id,
        Guid workspaceId,
        string providerUserId,
        ConnectionPath path,
        IReadOnlyList<string> scopes,
        DateTimeOffset? tokenExpiresAtUtc,
        DateTimeOffset connectedAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new InstagramDomainException("account.invalidId", "Account id must not be empty.");
        }

        if (workspaceId == Guid.Empty)
        {
            throw new InstagramDomainException("account.workspaceRequired", "A connected account must belong to a workspace.");
        }

        if (string.IsNullOrWhiteSpace(providerUserId))
        {
            throw new InstagramDomainException("account.providerIdentityRequired", "Provider identity is required.");
        }

        if (scopes.Count == 0)
        {
            throw new InstagramDomainException("account.scopesRequired", "At least one granted scope must be recorded.");
        }

        if (path == ConnectionPath.InstagramLogin && tokenExpiresAtUtc is null)
        {
            throw new InstagramDomainException(
                "account.expiryRequired",
                "Instagram Login connections carry expiring long-lived tokens; an expiry is required.");
        }

        if (tokenExpiresAtUtc is { } expiry && expiry <= connectedAtUtc)
        {
            throw new InstagramDomainException("account.expiryInPast", "Token expiry must be in the future at connect time.");
        }

        var account = new ConnectedAccount(id, workspaceId, providerUserId.Trim(), path, connectedAtUtc);
        account._scopes.AddRange(scopes.Select(s => s.Trim()).Where(s => s.Length > 0));
        account.TokenExpiresAtUtc = tokenExpiresAtUtc;
        return account;
    }

    /// <summary>Rehydrates stored state without re-running creation rules.</summary>
    public static ConnectedAccount FromState(
        Guid id,
        Guid workspaceId,
        string providerUserId,
        ConnectionPath path,
        IReadOnlyList<string> scopes,
        AccountHealth health,
        string? healthDetail,
        DateTimeOffset? tokenExpiresAtUtc,
        DateTimeOffset connectedAtUtc,
        DateTimeOffset? disconnectedAtUtc)
    {
        var account = new ConnectedAccount(id, workspaceId, providerUserId, path, connectedAtUtc);
        account._scopes.AddRange(scopes);
        account.Health = health;
        account.HealthDetail = healthDetail;
        account.TokenExpiresAtUtc = tokenExpiresAtUtc;
        account.DisconnectedAtUtc = disconnectedAtUtc;
        return account;
    }

    /// <summary>Records an accepted refresh/exchange result: new expiry, back to Connected.</summary>
    public void ApplyTokenRotation(DateTimeOffset newTokenExpiresAtUtc, DateTimeOffset rotatedAtUtc)
    {
        ThrowIfDisconnected();
        if (newTokenExpiresAtUtc <= rotatedAtUtc)
        {
            throw new InstagramDomainException("account.expiryInPast", "Rotated token expiry must be in the future.");
        }

        TokenExpiresAtUtc = newTokenExpiresAtUtc;
        Health = AccountHealth.Connected;
        HealthDetail = null;
    }

    public void MarkExpiringSoon() => Transition(AccountHealth.ExpiringSoon);

    public void MarkExpired() => Transition(AccountHealth.Expired);

    /// <summary>Marks the account revoked (user deauthorized the app / token invalidated).</summary>
    public void MarkRevoked(string detail) => Transition(AccountHealth.Revoked, detail);

    /// <summary>Marks actionable degraded state (password change, permission removal).</summary>
    public void MarkUnhealthy(string detail) => Transition(AccountHealth.Unhealthy, detail);

    /// <summary>
    /// Disconnects the account: terminal operator action that requires deleting all token
    /// material from the protected store (the use case performs the deletion).
    /// </summary>
    public void Disconnect(DateTimeOffset disconnectedAtUtc)
    {
        if (IsDisconnected)
        {
            throw new InstagramDomainException("account.disconnected", "Account is already disconnected.");
        }

        DisconnectedAtUtc = disconnectedAtUtc;
        TokenExpiresAtUtc = null;
    }

    private void Transition(AccountHealth health, string? detail = null)
    {
        ThrowIfDisconnected();
        Health = health;
        HealthDetail = detail is null ? null : detail.Length > 256 ? detail[..256] : detail;
    }

    private void ThrowIfDisconnected()
    {
        if (IsDisconnected)
        {
            throw new InstagramDomainException("account.disconnected", "State transitions are not possible after disconnect.");
        }
    }
}
