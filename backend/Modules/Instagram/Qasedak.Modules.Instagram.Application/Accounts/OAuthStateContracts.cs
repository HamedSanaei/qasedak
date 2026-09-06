namespace Qasedak.Modules.Instagram.Application.Accounts;

/// <summary>
/// Server-owned OAuth authorization state policy: short-lived, workspace- and
/// redirect-bound, single-use. Raw state values are transient security material:
/// only their SHA-256 hash is persisted, and values never enter logs.
/// </summary>
public static class OAuthStatePolicy
{
    /// <summary>How long an issued state remains consumable.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>Random bytes per issued state (256 bits).</summary>
    public const int StateByteLength = 32;
}

/// <summary>One issued OAuth state: the raw value is returned to the caller exactly once.</summary>
public sealed record OAuthStateIssuance(string State, DateTimeOffset ExpiresAtUtc);

/// <summary>Outcome of attempting to consume one OAuth state.</summary>
public enum OAuthStateConsumption
{
    /// <summary>Valid, bound and atomically consumed; proceed to token exchange.</summary>
    Consumed,

    /// <summary>No such state was ever issued (tampered or unknown).</summary>
    NotFound,

    /// <summary>Issued but past its expiry.</summary>
    Expired,

    /// <summary>Already consumed by an earlier callback (replay).</summary>
    AlreadyConsumed,

    /// <summary>Issued for a different workspace.</summary>
    WorkspaceMismatch,

    /// <summary>Callback redirect differs from issuance.</summary>
    RedirectMismatch,
}

/// <summary>
/// Durable OAuth-state store. Implementations must back multi-instance deployment
/// (no process memory) and consume atomically so concurrent callbacks race safely.
/// </summary>
public interface IOAuthStateStore
{
    /// <summary>Issues fresh state bound to one workspace + redirect URI.</summary>
    Task<OAuthStateIssuance> IssueAsync(Guid workspaceId, string redirectUri, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates workspace/redirect/expiry/replay in that order, then atomically
    /// consumes. Exactly one concurrent caller can observe Consumed.
    /// </summary>
    Task<OAuthStateConsumption> ConsumeAsync(string state, Guid workspaceId, string redirectUri, DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <summary>Stable failure codes for OAuth-state validation.</summary>
public static class OAuthStateFailures
{
    public const string InvalidState = "oauth.invalidState";

    public const string ExpiredState = "oauth.expiredState";

    public const string ReplayedState = "oauth.replayedState";

    public const string WorkspaceMismatch = "oauth.workspaceMismatch";

    public const string RedirectMismatch = "oauth.redirectMismatch";
}
