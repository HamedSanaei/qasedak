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

    /// <summary>
    /// Optimistic-concurrency counter (compare-and-swap): every successful local
    /// rotation bumps it, so a stale worker can never overwrite a newer token.
    /// Terminal disconnect bumps it too, so a stale in-flight rotation cannot
    /// commit ciphertext onto a disconnected account. Starts at 0 for legacy rows.
    /// </summary>
    public uint Version { get; private set; }

    /// <summary>When the current token was issued locally (connect or rotation); null for legacy rows.</summary>
    public DateTimeOffset? LastTokenIssuedAtUtc { get; private set; }

    /// <summary>Verified professional username; null until enrichment proves it.</summary>
    public string? Username { get; private set; }

    /// <summary>Verified display name; null when Meta omits it or enrichment never ran.</summary>
    public string? DisplayName { get; private set; }

    /// <summary>Verified avatar URL (Meta URLs expire); null when unavailable.</summary>
    public string? ProfilePictureUrl { get; private set; }

    /// <summary>
    /// Verified account type. Reserved: the official IG User reference (2026-04-22)
    /// exposes no account-type field, so enrichment currently leaves this null
    /// rather than inventing it.
    /// </summary>
    public string? AccountType { get; private set; }

    /// <summary>When profile fields were last proven by Meta; null when never enriched.</summary>
    public DateTimeOffset? ProfileUpdatedAtUtc { get; private set; }

    /// <summary>Webhook subscription health; Unknown until first subscribe/repair reports.</summary>
    public SubscriptionHealth SubscriptionHealth { get; private set; } = SubscriptionHealth.Unknown;

    /// <summary>Bounded detail for Partial/NeedsRepair states; null otherwise.</summary>
    public string? SubscriptionDetail { get; private set; }

    /// <summary>When subscription state was last checked against (or reported by) Meta.</summary>
    public DateTimeOffset? LastSubscriptionCheckUtc { get; private set; }

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
        account.LastTokenIssuedAtUtc = tokenExpiresAtUtc is null ? null : connectedAtUtc;
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
        DateTimeOffset? disconnectedAtUtc,
        uint version = 0,
        DateTimeOffset? lastTokenIssuedAtUtc = null,
        string? username = null,
        string? displayName = null,
        string? profilePictureUrl = null,
        string? accountType = null,
        DateTimeOffset? profileUpdatedAtUtc = null,
        SubscriptionHealth subscriptionHealth = SubscriptionHealth.Unknown,
        string? subscriptionDetail = null,
        DateTimeOffset? lastSubscriptionCheckUtc = null)
    {
        var account = new ConnectedAccount(id, workspaceId, providerUserId, path, connectedAtUtc);
        account._scopes.AddRange(scopes);
        account.Health = health;
        account.HealthDetail = healthDetail;
        account.TokenExpiresAtUtc = tokenExpiresAtUtc;
        account.DisconnectedAtUtc = disconnectedAtUtc;
        account.Version = version;
        account.LastTokenIssuedAtUtc = lastTokenIssuedAtUtc;
        account.Username = username;
        account.DisplayName = displayName;
        account.ProfilePictureUrl = profilePictureUrl;
        account.AccountType = accountType;
        account.ProfileUpdatedAtUtc = profileUpdatedAtUtc;
        account.SubscriptionHealth = subscriptionHealth;
        account.SubscriptionDetail = subscriptionDetail;
        account.LastSubscriptionCheckUtc = lastSubscriptionCheckUtc;
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
        LastTokenIssuedAtUtc = rotatedAtUtc;
        Version++;
        Health = AccountHealth.Connected;
        HealthDetail = null;
    }

    /// <summary>
    /// Records a Meta-proven profile. Identity fields are set only by enrichment flows
    /// that already verified the professional account id; this method stores display
    /// metadata, never identity.
    /// </summary>
    public void ApplyProfile(string username, string? displayName, string? profilePictureUrl, string? accountType, DateTimeOffset provenAtUtc)
    {
        ThrowIfDisconnected();
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InstagramDomainException("account.usernameRequired", "A verified username is required to record a profile.");
        }

        Username = username.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        ProfilePictureUrl = string.IsNullOrWhiteSpace(profilePictureUrl) ? null : profilePictureUrl.Trim();
        AccountType = string.IsNullOrWhiteSpace(accountType) ? null : accountType.Trim();
        ProfileUpdatedAtUtc = provenAtUtc;
    }

    /// <summary>Records a subscription check outcome; never invents Healthy.</summary>
    public void ApplySubscription(SubscriptionHealth health, string? detail, DateTimeOffset checkedAtUtc)
    {
        ThrowIfDisconnected();
        SubscriptionHealth = health;
        SubscriptionDetail = detail is null ? null : detail.Length > 256 ? detail[..256] : detail;
        LastSubscriptionCheckUtc = checkedAtUtc;
    }

    /// <summary>Explicit repair is a concurrency-authoritative mutation; unlike initial
    /// connection enrichment it advances the aggregate generation so simultaneous repair
    /// outcomes cannot overwrite one another with last-writer-wins semantics.</summary>
    public void ApplySubscriptionRepair(SubscriptionHealth health, string? detail, DateTimeOffset checkedAtUtc)
    {
        ApplySubscription(health, detail, checkedAtUtc);
        Version++;
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
    /// Bumps the concurrency counter so a stale concurrent rotation loses its
    /// compare-and-swap instead of resurrecting token material.
    /// </summary>
    public void Disconnect(DateTimeOffset disconnectedAtUtc)
    {
        if (IsDisconnected)
        {
            throw new InstagramDomainException("account.disconnected", "Account is already disconnected.");
        }

        DisconnectedAtUtc = disconnectedAtUtc;
        TokenExpiresAtUtc = null;
        Version++;
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
