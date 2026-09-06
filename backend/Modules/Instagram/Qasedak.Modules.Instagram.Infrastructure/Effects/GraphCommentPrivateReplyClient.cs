using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.Effects;
using Qasedak.Modules.Instagram.Infrastructure.Graph;
using Qasedak.Modules.Instagram.Infrastructure.Messaging;

namespace Qasedak.Modules.Instagram.Infrastructure.Effects;

/// <summary>
/// Instagram Login comment Private Reply adapter over the shared Graph transport
/// (M13-003). Current official contract (verified 2026-09-07):
/// POST {graph}/{version}/{IG_ID}/messages with Bearer IG User token and body
/// {"recipient":{"comment_id":"<COMMENT_ID>"},"message":{"text":"<TEXT>"}};
/// success is {"recipient_id":"...","message_id":"..."}.
///
/// Addressing is comment-ID based — the commenter IGSID is NOT part of the request and
/// FromId == null never blocks a valid Private Reply. The access token travels only in
/// the Authorization header, never in the URL, error details, logs or results.
/// </summary>
public sealed class GraphCommentPrivateReplyClient : ICommentPrivateReplyClient
{
    public const string HttpClientName = "MetaInstagramPrivateReply";

    public GraphCommentPrivateReplyClient(
        HttpClient http,
        IOptions<MetaMessagingOptions> messagingOptions,
        IOptions<MetaGraphOptions> graphOptions)
    {
        _transport = new MetaGraphTransport(http, graphOptions.Value.TimeoutSeconds);
        _messaging = messagingOptions.Value;
        _graph = graphOptions.Value;
    }

    private readonly MetaGraphTransport _transport;

    private readonly MetaMessagingOptions _messaging;

    private readonly MetaGraphOptions _graph;

    public async Task<PrivateReplySendResult> SendPrivateReplyAsync(
        string accessToken,
        string providerAccountId,
        string commentId,
        string text,
        CancellationToken cancellationToken = default)
    {
        var endpoint = MetaGraphUris.Versioned(
            string.IsNullOrWhiteSpace(_messaging.GraphBaseUrl) ? _graph.GraphHost : _messaging.GraphBaseUrl,
            _graph.ApiVersion,
            $"{providerAccountId}/messages");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new PrivateReplyPayload(
                new Recipient(commentId),
                new MessageBody(text))),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var outcome = await _transport.SendAsync(request, cancellationToken);
        return outcome switch
        {
            MetaGraphCallResult.Success success => ValidateSuccess(success.Document),
            MetaGraphCallResult.Rejected rejected => PrivateReplySendResult.Fail(
                PrivateReplyFailureReason.RejectedByMeta,
                RedactDetail(MetaGraphClassifier.Describe(MetaGraphClassifier.Classify(rejected.Error), rejected.Error), accessToken)),
            MetaGraphCallResult.Unreachable unreachable => PrivateReplySendResult.Fail(
                PrivateReplyFailureReason.TransportFailure, unreachable.Detail),
            _ => PrivateReplySendResult.Fail(PrivateReplyFailureReason.TransportFailure, "HTTP request failed."),
        };
    }

    /// <summary>
    /// Provider prose can echo credential fragments back in error messages; the bearer
    /// token and a bounded prefix are stripped from every failure detail so token
    /// material never reaches logs or the effect ledger.
    /// </summary>
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

    /// <summary>
    /// Documented success: {"recipient_id":"...","message_id":"..."}. Missing success
    /// identity is a malformed success — the effect is marked Uncertain, never retried.
    /// </summary>
    private static PrivateReplySendResult ValidateSuccess(JsonDocument document)
    {
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("recipient_id", out var recipient)
                && recipient.ValueKind == JsonValueKind.String
                && recipient.GetString() is { Length: > 0 } recipientId
                && root.TryGetProperty("message_id", out var message)
                && message.ValueKind == JsonValueKind.String
                && message.GetString() is { Length: > 0 } messageId)
            {
                return PrivateReplySendResult.Ok(recipientId, messageId);
            }
        }

        return PrivateReplySendResult.Fail(PrivateReplyFailureReason.MalformedResponse, "Payload did not match the documented success shape.");
    }

    private sealed record PrivateReplyPayload(Recipient Recipient, MessageBody Message);

    /// <summary>comment_id addressing — never recipient.id.</summary>
    private sealed record Recipient([property: JsonPropertyName("comment_id")] string CommentId);

    private sealed record MessageBody(string Text);
}
