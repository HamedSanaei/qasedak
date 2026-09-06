namespace Qasedak.Modules.Instagram.Application.Media;

/// <summary>
/// Qasedak-owned classification of selectable media (M13-006). Derived from the
/// provider <c>media_type</c> (IMAGE/VIDEO/REELS/CAROUSEL_ALBUM per the official
/// IG Media reference; REELS tolerated because real IG Login responses carry it).
/// Unknown future provider values map to <see cref="Unknown"/> and must never
/// crash deserialization. No raw Graph strings escape into this contract.
/// </summary>
public enum MediaKind
{
    Image,
    Video,
    Reel,
    Carousel,
    Unknown,
}

/// <summary>
/// One selectable top-level Instagram post/reel. Carousel children are nested and
/// bounded (<see cref="Children"/>); a child is never itself a selectable target.
/// Optional fields are nullable by contract: missing values mean
/// unavailable/unknown, never fabricated defaults.
/// </summary>
public sealed record MediaCatalogItem(
    string ProviderMediaId,
    string? Caption,
    MediaKind Kind,
    string? MediaProductType,
    DateTimeOffset? CreatedAtUtc,
    string? Permalink,
    string? MediaUrl,
    string? ThumbnailUrl,
    bool HasMediaPreview,
    bool HasThumbnail,
    int? LikeCount,
    int? CommentCount,
    IReadOnlyList<MediaCatalogChildItem>? Children);

/// <summary>Bounded nested preview data for carousel children (never a picker target).</summary>
public sealed record MediaCatalogChildItem(
    string ProviderMediaId,
    MediaKind Kind,
    string? MediaUrl,
    string? ThumbnailUrl,
    string? Permalink);

/// <summary>
/// One mapped provider page. <see cref="NextCursor"/> is the Qasedak opaque cursor
/// envelope for the next page (never a provider URL), or null when the provider
/// reports no further pages. <see cref="HasMore"/> is the truthful provider signal.
/// </summary>
public sealed record MediaCatalogPage(
    IReadOnlyList<MediaCatalogItem> Items,
    string? NextCursor,
    bool HasMore);

/// <summary>Outcome of a provider media call; never throws for provider behavior.</summary>
public abstract record MediaCatalogResult
{
    public sealed record Ok(MediaCatalogPage Page) : MediaCatalogResult;

    public sealed record Failed(string FailureCode, bool Transient) : MediaCatalogResult;
}

/// <summary>
/// Provider-facing Instagram media catalog port (M13-006). The caller is
/// responsible for exact-account authorization and protected-token retrieval;
/// this port addresses the provider by the verified professional account id.
/// Implementations construct versioned Graph requests through the shared M13-003
/// transport and never expose Meta DTOs or raw paging URLs.
/// </summary>
public interface IMediaCatalogClient
{
    /// <summary>
    /// Fetches one provider page of the account's media edge. <paramref name="accountId"/>
    /// binds returned cursors to the issuing ConnectedAccount; <paramref name="afterCursor"/>
    /// is the raw opaque provider cursor component (already validated and account-bound
    /// by the caller); null starts at the most recent media.
    /// </summary>
    Task<MediaCatalogResult> GetPageAsync(
        string accessToken,
        string providerAccountId,
        Guid accountId,
        int limit,
        string? afterCursor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bounded recent-N traversal: returns at most <paramref name="maxItems"/> top-level
    /// media records, stopping as soon as the cap is reached, deduplicated by provider
    /// media id, hard-capped by page count and total-item ceilings, cancellation-aware
    /// and loop-protected. For future consumers (overview/media picker backends).
    /// </summary>
    Task<MediaCatalogResult> GetRecentAsync(
        string accessToken,
        string providerAccountId,
        Guid accountId,
        int maxItems,
        CancellationToken cancellationToken = default);
}
