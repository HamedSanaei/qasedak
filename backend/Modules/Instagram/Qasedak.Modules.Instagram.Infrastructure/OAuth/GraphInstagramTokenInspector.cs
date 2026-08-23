using System.Net;
using System.Text.Json;
using Qasedak.Modules.Instagram.Application.OAuth;

namespace Qasedak.Modules.Instagram.Infrastructure.OAuth;

/// <summary>
/// Live token inspection against GET graph.instagram.com/me?fields=id — the cheapest
/// authenticated call. Maps Meta's error payload to the OQ-3 taxonomy:
/// - code 190 "has expired" → Expired
/// - code 190 with invalidation subcodes (463/467) or "deauthorized" → Revoked
/// - code 10/200 permission errors → PermissionLoss
/// - rate limits (4/17/32), 5xx, transport and non-JSON responses → Transient.
/// Token values never appear in returned details.
/// </summary>
public sealed class GraphInstagramTokenInspector(HttpClient http) : IMetaTokenInspector
{
    /// <summary>Named HttpClient registration used by dependency injection.</summary>
    public const string HttpClientName = "MetaInstagramInspection";

    private readonly HttpClient _http = http;

    public async Task<TokenInspection> InspectAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(
                $"https://graph.instagram.com/me?fields=id&access_token={Uri.EscapeDataString(accessToken)}",
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            return TokenInspection.From(TokenInspectionKind.Transient, "Meta endpoint unreachable.");
        }

        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return TokenInspection.From(TokenInspectionKind.Transient, "Unreadable response body.");
        }

        if (response.IsSuccessStatusCode)
        {
            return TokenInspection.Healthy();
        }

        var code = TryReadNumber(body, "code");
        var subcode = TryReadNumber(body, "error_subcode");
        var message = TryReadString(body, "error_message") ?? string.Empty;

        return Classify((int)response.StatusCode, code, subcode, message);
    }

    /// <summary>Pure taxonomy mapping, exercised directly by deterministic fixtures.</summary>
    public static TokenInspection Classify(int statusCode, int? code, int? subcode, string message)
    {
        if (statusCode >= 500 || statusCode == 429)
        {
            return TokenInspection.From(TokenInspectionKind.Transient, $"Meta returned status {statusCode}.");
        }

        if (code == 190)
        {
            if (subcode is 463 or 467
                || message.Contains("invalidated", StringComparison.OrdinalIgnoreCase)
                || message.Contains("deauthoriz", StringComparison.OrdinalIgnoreCase))
            {
                return TokenInspection.From(TokenInspectionKind.Revoked, "The account owner revoked access.");
            }

            if (message.Contains("expired", StringComparison.OrdinalIgnoreCase))
            {
                return TokenInspection.From(TokenInspectionKind.Expired, "The access token has expired.");
            }

            // 190 without expiry/invalidation semantics: treat as revoked session.
            return TokenInspection.From(TokenInspectionKind.Revoked, "Meta rejected the session.");
        }

        if (code is 10 or 200
            || message.Contains("permission", StringComparison.OrdinalIgnoreCase))
        {
            return TokenInspection.From(TokenInspectionKind.PermissionLoss, "A required permission was removed or not granted.");
        }

        if (code is 4 or 17 or 32)
        {
            return TokenInspection.From(TokenInspectionKind.Transient, "Meta rate limit hit; retry later.");
        }

        // Unknown taxonomy: conservative transient so health is never wrongly degraded.
        return TokenInspection.From(TokenInspectionKind.Transient, $"Unclassified Meta response (status {statusCode}).");
    }

    private static int? TryReadNumber(string json, string property)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty(property, out var value)
                && value.TryGetInt32(out var parsed))
            {
                return parsed;
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string? TryReadString(string json, string property)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty(property, out var value)
                && value.GetString() is { } text)
            {
                return text.Length > 300 ? text[..300] : text;
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
