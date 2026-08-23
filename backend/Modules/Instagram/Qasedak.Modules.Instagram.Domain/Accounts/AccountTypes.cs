namespace Qasedak.Modules.Instagram.Domain.Accounts;

/// <summary>
/// Which Meta integration path produced this connection, per ADR-006's dual-path decision.
/// </summary>
public enum ConnectionPath
{
    /// <summary>Business Login for Instagram — Page-free fast flow; long-lived ~60-day tokens.</summary>
    InstagramLogin = 1,

    /// <summary>Facebook Login for Business + Messenger Platform upgrade; never-expiring Page tokens.</summary>
    FacebookLogin = 2,
}

/// <summary>
/// Connection health surface exposed by the API contract (docs/product/meta-oauth-token-lifecycle.md §6).
/// Token values are never part of any state surfaced here.
/// </summary>
public enum AccountHealth
{
    Connected = 1,

    ExpiringSoon = 2,

    Expired = 3,

    Revoked = 4,

    Unhealthy = 5,
}
