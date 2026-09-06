using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.Effects;
using Qasedak.Modules.Instagram.Infrastructure.Graph;

namespace Qasedak.Modules.Instagram.Infrastructure.Effects;

/// <summary>
/// Focused read of the official IG Comment reference for the creation timestamp:
/// GET {graph}/{version}/{comment_id}?fields=timestamp (Bearer IG User token;
/// instagram_business_basic + instagram_business_manage_comments). The timestamp is
/// ISO 8601 and is the authoritative input for the 7-day Private Reply policy — the
/// webhook notification time is NOT comment creation time. This is the smallest
/// focused read needed; M13-013 owns general comment reconciliation.
///
/// The access token travels only in the Authorization header (never in the URL or
/// results), and a failed read degrades to <see cref="CommentReferenceReadResult.Unavailable"/>
/// so the one-sided notification guard remains the local fallback with Meta as final
/// authority. Per the official reference, comments on live media can only be read
/// while the broadcast is active — consistent with the Live policy (Meta decides).
/// </summary>
public sealed class GraphCommentReferenceReader : ICommentReferenceReader
{
    public const string HttpClientName = "MetaInstagramCommentReference";

    public GraphCommentReferenceReader(
        HttpClient http,
        IOptions<MetaGraphOptions> graphOptions)
    {
        _transport = new MetaGraphTransport(http, graphOptions.Value.TimeoutSeconds);
        _graph = graphOptions.Value;
    }

    private readonly MetaGraphTransport _transport;

    private readonly MetaGraphOptions _graph;

    public async Task<CommentReferenceReadResult> ReadCreatedAtUtcAsync(
        string accessToken,
        string commentId,
        CancellationToken cancellationToken = default)
    {
        var endpoint = MetaGraphUris.Versioned(_graph.GraphHost, _graph.ApiVersion, commentId, query: "fields=timestamp");

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var outcome = await _transport.SendAsync(request, cancellationToken);
        switch (outcome)
        {
            case MetaGraphCallResult.Success success:
                return ParseTimestamp(success.Document);
            case MetaGraphCallResult.Rejected rejected when rejected.Error.HttpStatusCode == 404:
                return new CommentReferenceReadResult.NotFound();
            case MetaGraphCallResult.Rejected:
            case MetaGraphCallResult.Unreachable:
            default:
                return new CommentReferenceReadResult.Unavailable();
        }
    }

    /// <summary>Official field value: ISO 8601 ("2017-05-19T23:27:28+0000"). Invalid/missing
    /// values degrade to Unavailable (never invented); the policy falls back to the guard.</summary>
    private static CommentReferenceReadResult ParseTimestamp(JsonDocument document)
    {
        using (document)
        {
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("timestamp", out var timestamp)
                && timestamp.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(timestamp.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var createdAt))
            {
                return new CommentReferenceReadResult.Found(createdAt);
            }
        }

        return new CommentReferenceReadResult.Unavailable();
    }
}
