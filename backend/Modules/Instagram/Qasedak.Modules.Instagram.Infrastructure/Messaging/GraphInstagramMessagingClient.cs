using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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
/// Instagram User token and body {"recipient":{"id":"<IGSID>"},"message":...}.
/// M13-010 adds typed content — plain text or button template (postback / web_url
/// buttons, verified "Button Template with IG Login", retrieved 2026-09-07) — with
/// local validation before any provider call and typed provider success identity
/// (recipient_id + message_id). The 24-hour window signal is the official code 10 +
/// subcode 2534022. Failures are structured results; access-token material and message
/// content never appear in details or logs.
/// </summary>
public sealed class GraphInstagramMessagingClient : IInstagramMessagingClient
{
    public const string HttpClientName = "MetaInstagramMessaging";

    /// <summary>Exact documented request shapes; null members are omitted entirely.</summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public GraphInstagramMessagingClient(HttpClient http, IOptions<MetaMessagingOptions> messagingOptions)
        : this(http, messagingOptions, Microsoft.Extensions.Options.Options.Create(new MetaGraphOptions()), null)
    {
    }

    public GraphInstagramMessagingClient(
        HttpClient http,
        IOptions<MetaMessagingOptions> messagingOptions,
        IOptions<MetaGraphOptions> graphOptions,
        IMessageSendObservability? observability = null)
    {
        _transport = new MetaGraphTransport(http, graphOptions.Value.TimeoutSeconds);
        _messaging = messagingOptions.Value;
        _graph = graphOptions.Value;
        _observability = observability;
    }

    private readonly MetaGraphTransport _transport;

    private readonly MetaMessagingOptions _messaging;

    private readonly MetaGraphOptions _graph;

    private readonly IMessageSendObservability? _observability;

    public Task<MessagingSendResult> SendTextAsync(
        string accessToken,
        string recipientProviderUserId,
        string text,
        CancellationToken cancellationToken = default) =>
        SendDirectAsync(accessToken, recipientProviderUserId, new InstagramMessageContent.PlainText(text), cancellationToken);

    public async Task<MessagingSendResult> SendDirectAsync(
        string accessToken,
        string recipientProviderUserId,
        InstagramMessageContent content,
        CancellationToken cancellationToken = default)
    {
        // Central verified limits, enforced locally BEFORE any provider call:
        // invalid content produces zero provider traffic and never reaches Meta.
        var validation = MessageValidationPolicy.Validate(content);
        if (!validation.IsValid)
        {
            Observe(content, "rejected", validation.Code.ToString());
            return MessagingSendResult.Fail(MessagingFailureReason.LocalValidation, "message." + validation.Code);
        }

        var payload = BuildPayload(recipientProviderUserId, content);
        var endpoint = MetaGraphUris.Versioned(
            string.IsNullOrWhiteSpace(_messaging.GraphBaseUrl) ? _graph.GraphHost : _messaging.GraphBaseUrl,
            _graph.ApiVersion,
            "me/messages");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload, options: SerializerOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var outcome = await _transport.SendAsync(request, cancellationToken);
        return outcome switch
        {
            MetaGraphCallResult.Success success => ValidateSuccess(success.Document, content),
            MetaGraphCallResult.Rejected rejected => FromMetaError(rejected.Error, accessToken, content),
            MetaGraphCallResult.Unreachable unreachable => FailObserved(
                content, MessagingFailureReason.TransportFailure, unreachable.Detail),
            _ => FailObserved(content, MessagingFailureReason.TransportFailure, "HTTP request failed."),
        };
    }

    private static MessagingSendPayload BuildPayload(string recipientProviderUserId, InstagramMessageContent content)
    {
        var recipient = new Recipient(recipientProviderUserId);
        return content switch
        {
            InstagramMessageContent.PlainText { Text: var text } =>
                new MessagingSendPayload(recipient, new MessageBody(Text: text, Attachment: null)),
            InstagramMessageContent.ButtonTemplate { Text: var text, Buttons: var buttons } =>
                new MessagingSendPayload(
                    recipient,
                    new MessageBody(
                        Text: null,
                        Attachment: new Attachment(
                            "template",
                            new ButtonTemplatePayload(
                                "button",
                                text,
                                buttons.Select(ToButtonPayload).ToList())))),
            _ => throw new InvalidOperationException("unreachable: content validated above"),
        };
    }

    private static ButtonPayload ToButtonPayload(InstagramMessageButton button) => button switch
    {
        InstagramMessageButton.Postback { Title: var title, Payload: var payload } =>
            new ButtonPayload(Type: "postback", Title: title, Payload: payload, Url: null),
        InstagramMessageButton.WebUrl { Title: var title, Url: var url } =>
            new ButtonPayload(Type: "web_url", Title: title, Payload: null, Url: url),
        _ => throw new InvalidOperationException("unreachable: button validated above"),
    };

    private MessagingSendResult ValidateSuccess(JsonDocument document, InstagramMessageContent content)
    {
        // Documented success: {"recipient_id":"...","message_id":"..."}. Missing success
        // identity is a malformed success, never a fabricated success.
        using (document)
        {
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("recipient_id", out var recipient)
                && recipient.ValueKind == JsonValueKind.String
                && recipient.GetString() is { Length: > 0 } recipientId
                && document.RootElement.TryGetProperty("message_id", out var message)
                && message.ValueKind == JsonValueKind.String
                && message.GetString() is { Length: > 0 } messageId)
            {
                Observe(content, "succeeded", null);
                return MessagingSendResult.Ok(recipientId, messageId);
            }
        }

        return FailObserved(content, MessagingFailureReason.MalformedResponse, "Payload did not match the documented shape.");
    }

    private MessagingSendResult FromMetaError(MetaGraphError error, string accessToken, InstagramMessageContent content)
    {
        var failure = MetaGraphClassifier.Classify(error);
        var detail = RedactDetail(MetaGraphClassifier.Describe(failure, error), accessToken);
        return failure switch
        {
            MetaGraphFailure.MessagingWindowExpired => FailObserved(
                content, MessagingFailureReason.MessagingWindowExpired, detail),
            _ => FailObserved(content, MessagingFailureReason.RejectedByMeta, detail),
        };
    }

    private MessagingSendResult FailObserved(InstagramMessageContent content, MessagingFailureReason reason, string detail)
    {
        Observe(content, "failed", reason.ToString());
        return MessagingSendResult.Fail(reason, detail);
    }

    private void Observe(InstagramMessageContent content, string outcome, string? failureCategory)
    {
        if (_observability is null)
        {
            return;
        }

        var (contentKind, buttonKind) = content switch
        {
            InstagramMessageContent.PlainText => ("text", "none"),
            InstagramMessageContent.ButtonTemplate { Buttons: var buttons } when buttons.Count == 1 =>
                ("button_template", buttons[0] is InstagramMessageButton.Postback ? "postback" : "web_url"),
            InstagramMessageContent.ButtonTemplate => ("button_template", "mixed"),
            _ => ("unknown", "none"),
        };
        _observability.DirectSend(contentKind, buttonKind, outcome, failureCategory);
    }

    /// <summary>
    /// Provider prose can echo credential fragments back in error messages; the bearer
    /// token and a bounded prefix are stripped from every failure detail so token
    /// material never reaches logs, results or the effect ledger.
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

    private sealed record MessagingSendPayload(Recipient Recipient, MessageBody Message);

    private sealed record Recipient(string Id);

    private sealed record MessageBody(string? Text, Attachment? Attachment);

    private sealed record Attachment(string Type, ButtonTemplatePayload Payload);

    private sealed record ButtonTemplatePayload(
        [property: JsonPropertyName("template_type")] string TemplateType,
        string Text,
        List<ButtonPayload> Buttons);

    private sealed record ButtonPayload(
        string Type,
        string? Title,
        string? Payload,
        string? Url);
}
