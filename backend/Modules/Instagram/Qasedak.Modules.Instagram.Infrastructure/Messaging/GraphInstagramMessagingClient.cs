using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.Messaging;
using Qasedak.Modules.Instagram.Infrastructure.Graph;

namespace Qasedak.Modules.Instagram.Infrastructure.Messaging;

/// <summary>Configuration for the messaging send API, bound from "Instagram:Meta".</summary>
public sealed class MetaMessagingOptions
{
    public const string SectionName = "Instagram:Meta";

    /// <summary>Base URL for Graph API endpoints.</summary>
    public string GraphBaseUrl { get; set; } = "https://graph.instagram.com";
}

/// <summary>
/// HTTP adapter for Instagram's documented messaging send contract over the shared
/// Graph transport (M13-003): POST {graph}/{version}/me/messages with a Bearer
/// Instagram User token and body {"recipient":{"id":"..."},"message":{"text":"..."}}.
/// The 24-hour window signal is the official code 10 + subcode 2534022 (the
/// historical code-490 mapping has no official standing and is not used).
/// Failures are structured results; access-token material never appears in details.
/// </summary>
public sealed class GraphInstagramMessagingClient : IInstagramMessagingClient
{
    public const string HttpClientName = "MetaInstagramMessaging";

    public GraphInstagramMessagingClient(HttpClient http, IOptions<MetaMessagingOptions> messagingOptions)
        : this(http, messagingOptions, Microsoft.Extensions.Options.Options.Create(new MetaGraphOptions()))
    {
    }

    public GraphInstagramMessagingClient(
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

    public async Task<MessagingSendResult> SendTextAsync(
        string accessToken,
        string recipientProviderUserId,
        string text,
        CancellationToken cancellationToken = default)
    {
        var endpoint = MetaGraphUris.Versioned(
            string.IsNullOrWhiteSpace(_messaging.GraphBaseUrl) ? _graph.GraphHost : _messaging.GraphBaseUrl,
            _graph.ApiVersion,
            "me/messages");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new MessagingSendPayload(
                new Recipient(recipientProviderUserId),
                new MessageBody(text))),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var outcome = await _transport.SendAsync(request, cancellationToken);
        return outcome switch
        {
            MetaGraphCallResult.Success success => ValidateSuccess(success.Document),
            MetaGraphCallResult.Rejected rejected => FromMetaError(rejected.Error),
            MetaGraphCallResult.Unreachable unreachable => MessagingSendResult.Fail(
                MessagingFailureReason.TransportFailure, unreachable.Detail),
            _ => MessagingSendResult.Fail(MessagingFailureReason.TransportFailure, "HTTP request failed."),
        };
    }

    private static MessagingSendResult ValidateSuccess(JsonDocument document)
    {
        // Documented success: {"recipient_id":"...","message_id":"..."}. We require at least
        // a message id to treat the delivery as confirmed.
        using (document)
        {
            if (document.RootElement.TryGetProperty("message_id", out _))
            {
                return MessagingSendResult.Ok();
            }
        }

        return MessagingSendResult.Fail(MessagingFailureReason.MalformedResponse, "Payload did not match the documented shape.");
    }

    private static MessagingSendResult FromMetaError(MetaGraphError error)
    {
        var failure = MetaGraphClassifier.Classify(error);
        return failure switch
        {
            MetaGraphFailure.MessagingWindowExpired => MessagingSendResult.Fail(
                MessagingFailureReason.MessagingWindowExpired,
                MetaGraphClassifier.Describe(failure, error)),
            _ => MessagingSendResult.Fail(
                MessagingFailureReason.RejectedByMeta,
                MetaGraphClassifier.Describe(failure, error)),
        };
    }

    private sealed record MessagingSendPayload(Recipient Recipient, MessageBody Message);

    private sealed record Recipient(string Id);

    private sealed record MessageBody(string Text);
}
