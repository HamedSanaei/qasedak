namespace Qasedak.Modules.Instagram.Application.Media;

/// <summary>
/// Stable media-catalog failure codes (M13-006). Account-level failures reuse the
/// M13-005 codes (<see cref="Accounts.AccountFailures"/>) so one failure surface
/// stays authoritative; media-specific codes cover cursor/limit validation,
/// provider classification and malformed responses.
/// </summary>
public static class MediaCatalogFailures
{
    /// <summary>The client-supplied cursor is missing, malformed, oversized or bound to another account.</summary>
    public const string InvalidCursor = "media.invalidCursor";

    /// <summary>The requested page size is invalid (zero/negative or above the hard maximum).</summary>
    public const string InvalidLimit = "media.invalidLimit";

    /// <summary>A required provider permission is missing or was removed.</summary>
    public const string PermissionDenied = "media.permissionDenied";

    /// <summary>The provider rate-limited the request; the caller may retry later.</summary>
    public const string RateLimited = "media.rateLimited";

    /// <summary>The provider returned a payload that did not match the documented shape.</summary>
    public const string Malformed = "media.malformed";

    /// <summary>Transient provider/transport failure; retry later without permanent degradation.</summary>
    public const string Unavailable = "media.unavailable";
}
