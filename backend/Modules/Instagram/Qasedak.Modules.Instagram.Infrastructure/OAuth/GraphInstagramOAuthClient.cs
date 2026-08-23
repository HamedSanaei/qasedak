using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.OAuth;

namespace Qasedak.Modules.Instagram.Infrastructure.OAuth;

/// <summary>Configuration for Meta OAuth, bound from "Instagram:Meta".</summary>
public sealed class MetaOAuthOptions
{
    public const string SectionName = "Instagram:Meta";

    /// <summary>The app's Instagram App ID (numeric string used as client_id).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The app's Instagram App Secret. Server-side only, never logged.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Comma-separated scopes requested during Business Login.</summary>
    public string Scopes { get; set; } = string.Join(",", InstagramAuthorizationScopes.Default);

    /// <summary>Authorize endpoint per Meta's Business Login documentation.</summary>
    public string AuthorizeEndpoint { get; set; } = "https://www.instagram.com/oauth/authorize";

    /// <summary>Code → short-lived token endpoint (POST form).</summary>
    public string CodeExchangeEndpoint { get; set; } = "https://api.instagram.com/oauth/access_token";

    /// <summary>Base URL for long-lived/refresh endpoints (GET).</summary>
    public string GraphBaseUrl { get; set; } = "https://graph.instagram.com";
}

/// <summary>
/// Builds the Business Login authorization URL exactly as documented: client_id,
/// redirect_uri (must match a configured OAuth redirect URI verbatim), response_type=code,
/// comma-separated scopes and the optional-but-recommended state value which Meta echoes
/// back on redirect (anti-CSRF support confirmed in the official query-string table).
/// </summary>
public sealed class InstagramAuthorizationUrlBuilder(IOptions<MetaOAuthOptions> options) : IAuthorizationUrlBuilder
{
    public AuthorizationUrl Build(AuthorizationUrlRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RedirectUri))
        {
            throw new ArgumentException("Redirect URI is required.", nameof(request));
        }

        var o = options.Value;
        var sb = new StringBuilder(o.AuthorizeEndpoint);
        sb.Append("?client_id=").Append(Uri.EscapeDataString(o.ClientId));
        sb.Append("&redirect_uri=").Append(Uri.EscapeDataString(request.RedirectUri));
        sb.Append("&response_type=code");
        sb.Append("&scope=").Append(Uri.EscapeDataString(o.Scopes));

        if (!string.IsNullOrEmpty(request.State))
        {
            sb.Append("&state=").Append(Uri.EscapeDataString(request.State));
        }

        return new AuthorizationUrl(sb.ToString());
    }
}

/// <summary>
/// HTTP adapter for the verified Meta OAuth token contract:
/// - POST api.instagram.com/oauth/access_token (form: client_id, client_secret,
///   grant_type=authorization_code, redirect_uri, code) → {data:[{access_token,user_id,permissions}]}
/// - GET graph.instagram.com/access_token?grant_type=ig_exchange_token&amp;client_secret&amp;access_token
///   → {access_token, token_type, expires_in}
/// - GET graph.instagram.com/refresh_access_token?grant_type=ig_refresh_token&amp;access_token
///   → {access_token, token_type, expires_in}
/// Failures are returned as structured results; secret/token values never appear in details.
/// </summary>
public sealed class GraphInstagramOAuthClient : IMetaOAuthClient
{
    /// <summary>Named HttpClient registration used by dependency injection.</summary>
    public const string HttpClientName = "MetaInstagramOAuth";

    private const string CodeExchangeGrant = "authorization_code";

    private const string ExchangeGrantType = "ig_exchange_token";

    private const string RefreshGrantType = "ig_refresh_token";

    private readonly HttpClient _http;

    private readonly MetaOAuthOptions _options;

    public GraphInstagramOAuthClient(HttpClient http, IOptions<MetaOAuthOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<CodeExchangeResult> ExchangeCodeAsync(CodeExchangeRequest request, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["grant_type"] = CodeExchangeGrant,
            ["redirect_uri"] = request.RedirectUri,
            ["code"] = request.Code,
        });

        using var response = await PostAsync(_options.CodeExchangeEndpoint, content, cancellationToken);
        return await InterpretCodeExchangeResponseAsync(response);
    }

    public Task<LongLivedTokenResult> ExchangeShortLivedForLongLivedAsync(string shortLivedAccessToken, CancellationToken cancellationToken = default) =>
        FetchLongLivedAsync(BuildGraphUrl("access_token", ExchangeGrantType, shortLivedAccessToken), cancellationToken);

    public Task<LongLivedTokenResult> RefreshLongLivedAsync(string longLivedAccessToken, CancellationToken cancellationToken = default) =>
        FetchLongLivedAsync(BuildGraphUrl("refresh_access_token", RefreshGrantType, longLivedAccessToken), cancellationToken);

    private async Task<LongLivedTokenResult> FetchLongLivedAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await GetAsync(url, cancellationToken);
        if (response is null)
        {
            return LongLivedTokenResult.Fail(new MetaOAuthFailure(MetaOAuthFailureReason.TransportFailure, "HTTP request failed."));
        }

        using var document = await ReadJsonAsync(response, cancellationToken);
        if (document is null)
        {
            return NonJsonResult(response, out var malformed)
                ? LongLivedTokenResult.Fail(malformed!)
                : LongLivedTokenResult.Fail(new MetaOAuthFailure(MetaOAuthFailureReason.RejectedByMeta, $"Meta returned status {(int)response.StatusCode}."));
        }

        var root = document.RootElement;
        if (!response.IsSuccessStatusCode)
        {
            return LongLivedTokenResult.Fail(FromMetaError(response.StatusCode, root));
        }

        if (root.TryGetProperty("access_token", out var accessToken)
            && root.TryGetProperty("expires_in", out var expiresIn)
            && expiresIn.TryGetInt64(out var seconds))
        {
            return LongLivedTokenResult.Ok(new LongLivedToken(accessToken.GetString()!, seconds));
        }

        return LongLivedTokenResult.Fail(new MetaOAuthFailure(MetaOAuthFailureReason.MalformedResponse, "Payload did not match the documented shape."));
    }

    private static async Task<CodeExchangeResult> InterpretCodeExchangeResponseAsync(HttpResponseMessage? response)
    {
        if (response is null)
        {
            return CodeExchangeResult.Fail(new MetaOAuthFailure(MetaOAuthFailureReason.TransportFailure, "HTTP request failed."));
        }

        using var document = await ReadJsonAsync(response, CancellationToken.None);
        if (document is null)
        {
            return CodeExchangeResult.Fail(new MetaOAuthFailure(
                response.IsSuccessStatusCode ? MetaOAuthFailureReason.MalformedResponse : MetaOAuthFailureReason.RejectedByMeta,
                $"Meta returned non-JSON with status {(int)response.StatusCode}."));
        }

        var root = document.RootElement;
        if (!response.IsSuccessStatusCode)
        {
            return CodeExchangeResult.Fail(FromMetaError(response.StatusCode, root));
        }

        // Success payload wraps the object in a top-level "data" array.
        if (root.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Array
            && data.GetArrayLength() > 0
            && data[0].TryGetProperty("access_token", out var accessToken))
        {
            var first = data[0];
            var userId = first.TryGetProperty("user_id", out var userIdElement)
                ? userIdElement.ToString()
                : string.Empty;
            var permissions = first.TryGetProperty("permissions", out var permissionsElement)
                && permissionsElement.GetString() is { } raw
                ? raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : [];
            return CodeExchangeResult.Ok(new CodeExchangeSuccess(accessToken.GetString()!, userId, permissions));
        }

        return CodeExchangeResult.Fail(new MetaOAuthFailure(MetaOAuthFailureReason.MalformedResponse, "Payload did not match the documented shape."));
    }

    private async Task<HttpResponseMessage?> PostAsync(string url, FormUrlEncodedContent content, CancellationToken cancellationToken)
    {
        try
        {
            return await _http.PostAsync(url, content, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private async Task<HttpResponseMessage?> GetAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            return await _http.GetAsync(url, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static async Task<JsonDocument?> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool NonJsonResult(HttpResponseMessage response, out MetaOAuthFailure? failure)
    {
        failure = new MetaOAuthFailure(
            response.IsSuccessStatusCode ? MetaOAuthFailureReason.MalformedResponse : MetaOAuthFailureReason.RejectedByMeta,
            $"Meta returned non-JSON with status {(int)response.StatusCode}.");
        return true;
    }

    /// <summary>
    /// Maps Meta's OAuth error payload ({error_type, code, error_message}) to a structured
    /// failure. Only error metadata is kept — never echo token or secret material.
    /// </summary>
    private static MetaOAuthFailure FromMetaError(HttpStatusCode statusCode, JsonElement body)
    {
        var errorType = body.TryGetProperty("error_type", out var type) ? type.GetString() : null;
        var message = body.TryGetProperty("error_message", out var messageElement) ? messageElement.GetString() : null;
        var detail = $"{(int)statusCode} {errorType ?? "Unknown"}";
        return new MetaOAuthFailure(MetaOAuthFailureReason.RejectedByMeta, Sanitize(detail, message));
    }

    /// <summary>Appends a bounded, redacted Meta message; strips any bearer-ish substrings.</summary>
    private static string Sanitize(string detail, string? metaMessage)
    {
        if (string.IsNullOrEmpty(metaMessage))
        {
            return detail;
        }

        var safe = metaMessage.Length > 300 ? metaMessage[..300] : metaMessage;
        // Defense-in-depth: never propagate anything that looks like credential material.
        if (safe.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || safe.Contains("token=", StringComparison.OrdinalIgnoreCase)
            || safe.StartsWith("EAAC", StringComparison.Ordinal)
            || safe.StartsWith("IG", StringComparison.Ordinal))
        {
            return detail + " (message withheld)";
        }

        return detail + ": " + safe;
    }

    private string BuildGraphUrl(string path, string grantType, string accessToken)
    {
        var builder = new UriBuilder(new Uri(new Uri(_options.GraphBaseUrl), path))
        {
            Query = $"grant_type={Uri.EscapeDataString(grantType)}" +
                    $"&client_secret={Uri.EscapeDataString(_options.ClientSecret)}" +
                    $"&access_token={Uri.EscapeDataString(accessToken)}",
        };
        return builder.Uri.AbsoluteUri;
    }
}
