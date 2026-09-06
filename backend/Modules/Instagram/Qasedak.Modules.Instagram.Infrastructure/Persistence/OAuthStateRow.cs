namespace Qasedak.Modules.Instagram.Infrastructure.Persistence;

/// <summary>
/// Persistence row for one server-issued OAuth state. Only the SHA-256 hash of the
/// opaque state is stored — the raw value is transient and never persisted or logged.
/// </summary>
public sealed class OAuthStateRow
{
    public string StateHash { get; init; } = string.Empty;

    public Guid WorkspaceId { get; init; }

    public string RedirectUri { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset ExpiresAtUtc { get; init; }

    public DateTimeOffset? ConsumedAtUtc { get; set; }
}
