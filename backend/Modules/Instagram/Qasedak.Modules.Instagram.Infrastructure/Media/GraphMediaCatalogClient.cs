using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.Media;
using Qasedak.Modules.Instagram.Infrastructure.Graph;

namespace Qasedak.Modules.Instagram.Infrastructure.Media;

/// <summary>
/// M13-006 media catalog adapter over the shared M13-003 transport. Executes the
/// verified IG-Login media edge
/// <c>GET {graph}/{version}/{IG_ID}/media?fields=…&amp;limit=N[&amp;after=…]</c>
/// (IG Media reference, retrieved 2026-09-06; permission
/// <c>instagram_business_basic</c>, Bearer IG User token). Maps provider JSON into
/// Qasedak-owned records; Graph DTOs, paging URLs and tokens never escape.
///
/// Contract nuances honored: <c>media_product_type</c> is Facebook-Login-only and
/// never requested; <c>thumbnail_url</c> is VIDEO-only; <c>permalink</c> is not
/// valid for album children; <c>media_url</c> can be omitted (copyrighted media);
/// counts can be omitted (owner hid them) — all degrade to null, never invented.
/// Media records without a usable id are skipped with bounded observability.
/// </summary>
public sealed class GraphMediaCatalogClient(
    HttpClient http,
    IOptions<MetaGraphOptions> graphOptions,
    IMediaCursorCodec cursorCodec) : IMediaCatalogClient
{
    public const string HttpClientName = "MetaInstagramMediaCatalog";

    private readonly MetaGraphTransport _transport = new(http, graphOptions.Value.TimeoutSeconds);

    private readonly MetaGraphOptions _graph = graphOptions.Value;

    public GraphMediaCatalogClient(HttpClient http)
        : this(http, Microsoft.Extensions.Options.Options.Create(new MetaGraphOptions()), new MediaCatalogCursorCodec())
    {
    }

    public async Task<MediaCatalogResult> GetPageAsync(
        string accessToken,
        string providerAccountId,
        Guid accountId,
        int limit,
        string? afterCursor,
        CancellationToken cancellationToken = default)
    {
        var query = "fields=" + Uri.EscapeDataString(MediaCatalogPolicy.Fields)
            + "&limit=" + limit.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + (afterCursor is null ? string.Empty : "&after=" + Uri.EscapeDataString(afterCursor));

        var endpoint = MetaGraphUris.Versioned(_graph.GraphHost, _graph.ApiVersion, $"{providerAccountId}/media", query).ToString();

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var outcome = await _transport.SendAsync(request, cancellationToken);
        if (outcome is MetaGraphCallResult.Success success)
        {
            return InterpretPage(success.Document, accountId);
        }

        return outcome switch
        {
            MetaGraphCallResult.Rejected rejected => MapFailure(rejected.Error),
            _ => new MediaCatalogResult.Failed(MediaCatalogFailures.Unavailable, Transient: true),
        };
    }

    public async Task<MediaCatalogResult> GetRecentAsync(
        string accessToken,
        string providerAccountId,
        Guid accountId,
        int maxItems,
        CancellationToken cancellationToken = default)
    {
        if (maxItems <= 0 || maxItems > MediaCatalogPolicy.MaxRecentItems)
        {
            return new MediaCatalogResult.Failed(MediaCatalogFailures.InvalidLimit, Transient: false);
        }

        var items = new List<MediaCatalogItem>(Math.Min(maxItems, MediaCatalogPolicy.DefaultPageSize));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? providerCursor = null;
        string? previousCursor = null;

        for (var pageIndex = 0; pageIndex < MediaCatalogPolicy.MaxPages && items.Count < maxItems; pageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var limit = Math.Min(MediaCatalogPolicy.DefaultPageSize, maxItems - items.Count);
            var result = await GetPageAsync(accessToken, providerAccountId, accountId, limit, providerCursor, cancellationToken);
            if (result is not MediaCatalogResult.Ok ok)
            {
                return result;
            }

            foreach (var item in ok.Page.Items)
            {
                if (items.Count >= maxItems)
                {
                    break;
                }

                if (seen.Add(item.ProviderMediaId))
                {
                    items.Add(item);
                }
            }

            if (!ok.Page.HasMore || ok.Page.NextCursor is null)
            {
                break;
            }

            var next = cursorCodec.Decode(ok.Page.NextCursor, accountId);
            if (!next.Valid)
            {
                return new MediaCatalogResult.Failed(MediaCatalogFailures.Malformed, Transient: false);
            }

            // Cursor loop defense: the provider must progress; a repeated cursor is malformed.
            if (string.Equals(next.ProviderAfterCursor, previousCursor, StringComparison.Ordinal))
            {
                return new MediaCatalogResult.Failed(MediaCatalogFailures.Malformed, Transient: false);
            }

            previousCursor = next.ProviderAfterCursor;
            providerCursor = next.ProviderAfterCursor;
        }

        return new MediaCatalogResult.Ok(new MediaCatalogPage(items, NextCursor: null, HasMore: false));
    }

    private MediaCatalogResult InterpretPage(JsonDocument document, Guid accountId)
    {
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array)
            {
                return new MediaCatalogResult.Failed(MediaCatalogFailures.Malformed, Transient: false);
            }

            var items = new List<MediaCatalogItem>(Math.Min(data.GetArrayLength(), MediaCatalogPolicy.MaxPageSize + 1));
            foreach (var element in data.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var id = element.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(id))
                {
                    // A media record without a usable id is not selectable; skip with
                    // bounded observability instead of fabricating an id or failing the page.
                    continue;
                }

                items.Add(MapItem(element, id.Trim()));
            }

            string? nextCursor = null;
            var hasMore = false;
            if (root.TryGetProperty("paging", out var paging) && paging.ValueKind == JsonValueKind.Object
                && paging.TryGetProperty("cursors", out var cursors) && cursors.ValueKind == JsonValueKind.Object
                && cursors.TryGetProperty("after", out var after) && after.GetString() is { Length: > 0 } afterValue)
            {
                hasMore = true;
                nextCursor = cursorCodec.Encode(accountId, afterValue);
            }

            return new MediaCatalogResult.Ok(new MediaCatalogPage(items, nextCursor, hasMore));
        }
    }

    private static MediaCatalogItem MapItem(JsonElement element, string id)
    {
        var kind = KindOf(Optional(element, "media_type"));
        var children = element.TryGetProperty("children", out var childrenElement)
            ? MapChildren(childrenElement)
            : null;

        return new MediaCatalogItem(
            id,
            NullIfBlank(Optional(element, "caption")),
            kind,
            NullIfBlank(Optional(element, "media_product_type")),
            ParseTimestamp(Optional(element, "timestamp")),
            NullIfBlank(Optional(element, "permalink")),
            NullIfBlank(Optional(element, "media_url")),
            NullIfBlank(Optional(element, "thumbnail_url")),
            HasMediaPreview: NullIfBlank(Optional(element, "media_url")) is not null,
            HasThumbnail: NullIfBlank(Optional(element, "thumbnail_url")) is not null,
            CountOrNull(element, "like_count"),
            CountOrNull(element, "comments_count"),
            children);
    }

    private static List<MediaCatalogChildItem>? MapChildren(JsonElement childrenElement)
    {
        // Graph edges return { "data": [...] }; a bare array is tolerated for
        // forward compatibility.
        if (childrenElement.ValueKind == JsonValueKind.Object
            && childrenElement.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Array)
        {
            childrenElement = data;
        }

        if (childrenElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var children = new List<MediaCatalogChildItem>();
        foreach (var child in childrenElement.EnumerateArray())
        {
            if (children.Count >= MediaCatalogPolicy.MaxCarouselChildren || child.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = child.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            children.Add(new MediaCatalogChildItem(
                id.Trim(),
                KindOf(Optional(child, "media_type")),
                NullIfBlank(Optional(child, "media_url")),
                NullIfBlank(Optional(child, "thumbnail_url")),
                NullIfBlank(Optional(child, "permalink"))));
        }

        return children.Count == 0 ? null : children;
    }

    private static MediaKind KindOf(string? mediaType) => mediaType?.ToUpperInvariant() switch
    {
        "IMAGE" => MediaKind.Image,
        "VIDEO" => MediaKind.Video,
        "REELS" => MediaKind.Reel,
        "CAROUSEL_ALBUM" => MediaKind.Carousel,
        // Unknown/future provider values never crash; fail closed to Unknown.
        _ => MediaKind.Unknown,
    };

    private static DateTimeOffset? ParseTimestamp(string? timestamp) =>
        DateTimeOffset.TryParse(timestamp, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static int? CountOrNull(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var count)
            ? count
            : null;

    private static string? Optional(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static MediaCatalogResult.Failed MapFailure(MetaGraphError error) =>
        MetaGraphClassifier.Classify(error) switch
        {
            MetaGraphFailure.RateLimited =>
                new MediaCatalogResult.Failed(MediaCatalogFailures.RateLimited, Transient: true),
            MetaGraphFailure.PermissionLoss =>
                new MediaCatalogResult.Failed(MediaCatalogFailures.PermissionDenied, Transient: false),
            MetaGraphFailure.Transient or MetaGraphFailure.TransportFailure =>
                new MediaCatalogResult.Failed(MediaCatalogFailures.Unavailable, Transient: true),
            _ => new MediaCatalogResult.Failed(MediaCatalogFailures.Unavailable, Transient: false),
        };
}
