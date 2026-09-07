using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.Reconciliation;
using Qasedak.Modules.Instagram.Infrastructure.Graph;

namespace Qasedak.Modules.Instagram.Infrastructure.Reconciliation;

/// <summary>
/// M13-013 Phase A adapter over the shared M13-003 transport for the verified
/// IG-Login comments edge
/// <c>GET {graph}/{version}/{IG_MEDIA_ID}/comments?fields=…&amp;limit=…[&amp;after=…]</c>
/// (IG Media Comments + IG Comment references, retrieved 2026-09-07; permissions
/// <c>instagram_business_basic</c> + <c>instagram_business_manage_comments</c>,
/// Bearer IG User token, host <c>graph.instagram.com</c>).
///
/// Cursor security (§27): the provider <c>paging.next</c> URL is NEVER followed or
/// stored. Only the bounded opaque <c>after</c> component is extracted and rebuilt
/// into a known versioned <c>graph.instagram.com</c> URI with the known media id and
/// fields. Oversized cursors and cursor loops are rejected with stable codes. Raw
/// Graph DTOs, paging URLs and token material never escape.
/// </summary>
public sealed class GraphInstagramCommentHistoryClient(
    HttpClient http,
    IOptions<MetaGraphOptions> graphOptions) : IInstagramCommentHistoryClient
{
    public const string HttpClientName = "MetaInstagramCommentHistory";

    private readonly MetaGraphTransport _transport = new(http, graphOptions.Value.TimeoutSeconds);

    private readonly MetaGraphOptions _graph = graphOptions.Value;

    public GraphInstagramCommentHistoryClient(HttpClient http)
        : this(http, Microsoft.Extensions.Options.Options.Create(new MetaGraphOptions()))
    {
    }

    public async Task<CommentHistoryResult> ListCommentsPageAsync(
        string accessToken,
        string providerAccountId,
        string mediaId,
        int limit,
        string? afterCursor,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mediaId) || mediaId.Length > 64)
        {
            return new CommentHistoryResult.Failed(CommentHistoryFailures.Malformed, Transient: false);
        }

        if (afterCursor is not null && afterCursor.Length > CommentReconciliationPolicy.MaxProviderCursorLength)
        {
            return new CommentHistoryResult.Failed(CommentHistoryFailures.CursorOversized, Transient: false);
        }

        var query = "fields=" + Uri.EscapeDataString(CommentReconciliationPolicy.Fields)
            + "&limit=" + limit.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + (afterCursor is null ? string.Empty : "&after=" + Uri.EscapeDataString(afterCursor));

        var endpoint = MetaGraphUris.Versioned(_graph.GraphHost, _graph.ApiVersion, $"{mediaId}/comments", query).ToString();

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var outcome = await _transport.SendAsync(request, cancellationToken);
        return outcome switch
        {
            MetaGraphCallResult.Success success => InterpretPage(success.Document),
            MetaGraphCallResult.Rejected rejected => MapFailure(rejected.Error),
            _ => new CommentHistoryResult.Failed(CommentHistoryFailures.Unavailable, Transient: true),
        };
    }

    private static CommentHistoryResult InterpretPage(JsonDocument document)
    {
        try
        {
            var root = document.RootElement;
            var comments = new List<ProviderCommentRow>();
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (!item.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String ||
                        string.IsNullOrWhiteSpace(id.GetString()))
                    {
                        continue; // a row without a usable comment id cannot be reconciled
                    }

                    var fromId = ReadString(item, "from", "id");
                    var username = ReadString(item, "from", "username");
                    var mediaId = ReadString(item, "media", "id") ?? string.Empty;
                    var timestamp = ReadTimestamp(item);
                    comments.Add(new ProviderCommentRow(
                        id.GetString()!,
                        fromId,
                        username,
                        ReadString(item, "text"),
                        timestamp,
                        mediaId,
                        ReadString(item, "parent_id"),
                        ReadBool(item, "hidden")));
                }
            }

            var (nextCursor, hasMore) = ReadNextCursor(root);
            return new CommentHistoryResult.Ok(new CommentHistoryPage(comments, nextCursor, hasMore));
        }
        catch (JsonException)
        {
            return new CommentHistoryResult.Failed(CommentHistoryFailures.Malformed, Transient: false);
        }
    }

    /// <summary>
    /// Extracts ONLY the bounded opaque <c>after</c> component from the provider
    /// paging envelope. A foreign/malformed <c>next</c> URL is never followed: if the
    /// component is missing the page is treated as final (bounded, fail-closed).
    /// </summary>
    private static (string? NextCursor, bool HasMore) ReadNextCursor(JsonElement root)
    {
        if (!root.TryGetProperty("paging", out var paging) || paging.ValueKind != JsonValueKind.Object)
        {
            return (null, false);
        }

        var hasMore = false;
        if (paging.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(next.GetString()))
        {
            hasMore = true;
        }

        if (!paging.TryGetProperty("cursors", out var cursors) || cursors.ValueKind != JsonValueKind.Object ||
            !cursors.TryGetProperty("after", out var after) || after.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(after.GetString()))
        {
            // paging.next exists but carries no bounded cursor component: fail closed —
            // never parse the URL itself.
            return (null, hasMore);
        }

        return (after.GetString(), hasMore);
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadString(JsonElement element, string outer, string inner) =>
        element.TryGetProperty(outer, out var nested) && nested.ValueKind == JsonValueKind.Object
            ? ReadString(nested, inner)
            : null;

    private static bool ReadBool(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static DateTimeOffset ReadTimestamp(JsonElement element)
    {
        var raw = ReadString(element, "timestamp");
        if (raw is not null && DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        // The provider always includes timestamp for listed comments; a missing value
        // cannot produce a truthfully-dated recovered comment. Sentinel means "unknown":
        // the sweep's age guard treats it as out-of-horizon (bounded, fail-closed).
        return DateTimeOffset.MinValue;
    }

    private static CommentHistoryResult.Failed MapFailure(MetaGraphError error) => MetaGraphClassifier.Classify(error) switch
    {
        MetaGraphFailure.RateLimited => new CommentHistoryResult.Failed(CommentHistoryFailures.RateLimited, Transient: true),
        MetaGraphFailure.PermissionLoss => new CommentHistoryResult.Failed(CommentHistoryFailures.PermissionLoss, Transient: false),
        MetaGraphFailure.AuthenticationInvalid or MetaGraphFailure.TokenExpired or MetaGraphFailure.Revoked =>
            new CommentHistoryResult.Failed(CommentHistoryFailures.Authentication, Transient: false),
        MetaGraphFailure.NotFound => new CommentHistoryResult.Failed(CommentHistoryFailures.NotFound, Transient: false),
        MetaGraphFailure.Transient or MetaGraphFailure.TransportFailure =>
            new CommentHistoryResult.Failed(CommentHistoryFailures.Unavailable, Transient: true),
        _ => new CommentHistoryResult.Failed(CommentHistoryFailures.Malformed, Transient: false),
    };
}
