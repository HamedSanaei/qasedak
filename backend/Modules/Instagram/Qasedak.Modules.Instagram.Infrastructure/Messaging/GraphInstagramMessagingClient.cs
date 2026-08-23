using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.Messaging;

namespace Qasedak.Modules.Instagram.Infrastructure.Messaging;

/// <summary>Configuration for the messaging send API, bound from "Instagram:Meta".</summary>
public sealed class MetaMessagingOptions
{
    public const string SectionName = "Instagram:Meta";

    /// <summary>Base URL for Graph API endpoints.</summary>
    public string GraphBaseUrl { get; set; } = "https://graph.instagram.com";
}

/// <summary>
/// HTTP adapter for Instagram's documented messaging send contract:
/// POST {graph}/me/messages with Bearer page access token and body
/// {"recipient":{"id":"..."},"message":{"text":"..."}}. Graph error code 490 marks a
/// recipient outside the 24-hour customer service window. Failures are structured
/// results; access-token material never appears in failure details.
/// </summary>
public sealed class GraphInstagramMessagingClient(HttpClient http, IOptions<MetaMessagingOptions> options) : IInstagramMessagingClient
{
    public const string HttpClientName = "MetaInstagramMessaging";

    private const int WindowExpiredGraphCode = 490;

    private readonly HttpClient _http = http;

    private readonly string _endpoint = new Uri(new Uri(options.Value.GraphBaseUrl), "me/messages").ToString();

    public async Task<MessagingSendResult> SendTextAsync(
        string accessToken,
        string recipientProviderUserId,
        string text,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = JsonContent.Create(new MessagingSendPayload(
                new Recipient(recipientProviderUserId),
                new MessageBody(text))),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return MessagingSendResult.Fail(MessagingFailureReason.TransportFailure, "HTTP request failed.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return MessagingSendResult.Fail(MessagingFailureReason.TransportFailure, "HTTP request timed out.");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return ValidateSuccess(await ReadJsonAsync(response));
            }

            return FromMetaError(response.StatusCode, await ReadJsonAsync(response));
        }
    }

    private static MessagingSendResult ValidateSuccess(JsonDocument? document)
    {
        // Documented success: {"recipient_id":"...","message_id":"..."}. We require at least
        // a message id to treat the delivery as confirmed.
        if (document?.RootElement.TryGetProperty("message_id", out _) == true)
        {
            return MessagingSendResult.Ok();
        }

        return MessagingSendResult.Fail(MessagingFailureReason.MalformedResponse, "Payload did not match the documented shape.");
    }

    private static MessagingSendResult FromMetaError(HttpStatusCode statusCode, JsonDocument? document)
    {
        var root = document?.RootElement;
        var error = root is not null && root.Value.TryGetProperty("error", out var errorElement) ? errorElement : (JsonElement?)null;

        if (error is null)
        {
            return MessagingSendResult.Fail(MessagingFailureReason.RejectedByMeta, $"Meta returned status {(int)statusCode}.");
        }

        var code = error.Value.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var parsed) ? parsed : (int?)null;
        if (code == WindowExpiredGraphCode)
        {
            return MessagingSendResult.Fail(MessagingFailureReason.MessagingWindowExpired, "Recipient is outside the 24-hour messaging window.");
        }

        var type = error.Value.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        return MessagingSendResult.Fail(
            MessagingFailureReason.RejectedByMeta,
            $"{(int)statusCode} {type ?? "Unknown"} (code {(code?.ToString(CultureInfo.InvariantCulture) ?? "?")}).");
    }

    private static async Task<JsonDocument?> ReadJsonAsync(HttpResponseMessage response)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync();
            return await JsonDocument.ParseAsync(stream);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record MessagingSendPayload(Recipient Recipient, MessageBody Message);

    private sealed record Recipient(string Id);

    private sealed record MessageBody(string Text);
}
