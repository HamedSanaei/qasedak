namespace Qasedak.Modules.Instagram.Application.Media;

/// <summary>
/// Central bounded media-catalog policy (M13-006). One source of truth for page
/// sizes, traversal ceilings and cursor bounds — no magic constants scattered
/// across endpoints/adapters/tests. Values derive from the current provider
/// contract (Graph default limit 25, hard max 100 per the Graph API results
/// guide) and Qasedak picker UX: conservative bounded values, never provider max.
/// </summary>
public static class MediaCatalogPolicy
{
    /// <summary>Server default page size when the client sends no limit.</summary>
    public const int DefaultPageSize = 25;

    /// <summary>Hard per-request page size ceiling (provider max is 100).</summary>
    public const int MaxPageSize = 50;

    /// <summary>Hard ceiling for recent-N traversal results.</summary>
    public const int MaxRecentItems = 200;

    /// <summary>Maximum provider pages fetched in one bounded traversal.</summary>
    public const int MaxPages = 20;

    /// <summary>Maximum nested carousel children surfaced for preview.</summary>
    public const int MaxCarouselChildren = 10;

    /// <summary>Maximum encoded (base64url) cursor length accepted from clients.</summary>
    public const int MaxEncodedCursorLength = 2048;

    /// <summary>Maximum decoded raw provider cursor component length.</summary>
    public const int MaxProviderCursorLength = 512;

    /// <summary>Current cursor envelope version; bump on incompatible shape changes.</summary>
    public const int CursorVersion = 1;

    /// <summary>
    /// Verified IG-Login media fields (IG Media reference, retrieved 2026-09-06).
    /// <c>media_product_type</c> is deliberately absent: the official reference
    /// marks it Facebook-Login-only, so it is never requested on this path.
    /// </summary>
    public const string Fields = "id,caption,media_type,media_url,thumbnail_url,permalink,timestamp,like_count,comments_count,children{id,media_type,media_url,thumbnail_url,permalink}";
}
