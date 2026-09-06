using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.Effects;
using Qasedak.Modules.Instagram.Infrastructure.Graph;

namespace Qasedak.Modules.Instagram.Infrastructure.Effects;

/// <summary>
/// Instagram Login public comment reply adapter over the shared Graph transport
/// (M13-003). Current official contract (verified 2026-09-07):
/// POST {graph}/{version}/{comment_id}/replies with parameter message={text}, Bearer
/// IG User token; success is {"id":"<REPLY_COMMENT_ID>"}. Distinct from Private Reply —
/// no messaging recipient object is ever present. Not wired to automation config yet
/// (M13-012 owns that surface); the adapter and ledger independence are M13-009.
/// </summary>
public sealed class GraphCommentPublicReplyClient : ICommentPublicReplyClient
{
    public const string HttpClientName = "MetaInstagramPublicReply";

    public GraphCommentPublicReplyClient(
        HttpClient http,
        IOptions<MetaGraphOptions> graphOptions)
    {
        _transport = new MetaGraphTransport(http, graphOptions.Value.TimeoutSeconds);
        _graph = graphOptions.Value;
    }

    private readonly MetaGraphTransport _transport;

    private readonly MetaGraphOptions _graph;

    public async Task<PublicReplySendResult> SendPublicReplyAsync(
        string accessToken,
        string commentId,
        string text,
        CancellationToken cancellationToken = default)
    {
        var endpoint = MetaGraphUris.Versioned(_graph.GraphHost, _graph.ApiVersion, $"{commentId}/replies");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("message", text)]),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var outcome = await _transport.SendAsync(request, cancellationToken);
        return outcome switch
        {
            MetaGraphCallResult.Success success => ValidateSuccess(success.Document),
            MetaGraphCallResult.Rejected rejected => PublicReplySendResult.Fail(
                PublicReplyFailureReason.RejectedByMeta,
                RedactDetail(MetaGraphClassifier.Describe(MetaGraphClassifier.Classify(rejected.Error), rejected.Error), accessToken)),
            MetaGraphCallResult.Unreachable unreachable => PublicReplySendResult.Fail(
                PublicReplyFailureReason.TransportFailure, unreachable.Detail),
            _ => PublicReplySendResult.Fail(PublicReplyFailureReason.TransportFailure, "HTTP request failed."),
        };
    }

    /// <summary>Strips the bearer token and a bounded prefix from provider prose in failure details.</summary>
    private static string RedactDetail(string detail, string accessToken)
    {
        if (string.IsNullOrEmpty(detail) || string.IsNullOrEmpty(accessToken))
        {
            return detail;
        }

        var redacted = detail.Replace(accessToken, "[REDACTED]", StringComparison.Ordinal);
        if (accessToken.Length >= 8)
        {
            redacted = redacted.Replace(accessToken[..8], "[REDACTED]", StringComparison.Ordinal);
        }

        return redacted;
    }

    /// <summary>Documented success: {"id":"<REPLY_COMMENT_ID>"}.</summary>
    private static PublicReplySendResult ValidateSuccess(JsonDocument document)
    {
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                && id.GetString() is { Length: > 0 } replyCommentId)
            {
                return PublicReplySendResult.Ok(replyCommentId);
            }
        }

        return PublicReplySendResult.Fail(PublicReplyFailureReason.MalformedResponse, "Payload did not match the documented success shape.");
    }
}
