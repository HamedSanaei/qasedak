using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.OAuth;
using Qasedak.Modules.Instagram.Infrastructure.Graph;

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
/// HTTP adapter for the verified Meta OAuth token contract over the shared Graph
/// transport (M13-003):
/// - POST api.instagram.com/oauth/access_token (form: client_id, client_secret,
///   grant_type=authorization_code, redirect_uri, code) → {data:[{access_token,user_id,permissions}]}
/// - GET graph.instagram.com/access_token?grant_type=ig_exchange_token&amp;client_secret&amp;access_token
///   → {access_token, token_type, expires_in}
/// - GET graph.instagram.com/refresh_access_token?grant_type=ig_refresh_token&amp;access_token
///   → {access_token, token_type, expires_in}
/// The OAuth token endpoints stay unversioned per the official Business Login contract;
/// only versioned Graph paths take the configured version. Failures are returned as
/// structured results; secret/token values never appear in details.
/// </summary>
public sealed class GraphInstagramOAuthClient : IMetaOAuthClient
{
    /// <summary>Named HttpClient registration used by dependency injection.</summary>
    public const string HttpClientName = "MetaInstagramOAuth";

    private const string CodeExchangeGrant = "authorization_code";

    private const string ExchangeGrantType = "ig_exchange_token";

    private const string RefreshGrantType = "ig_refresh_token";

    private readonly MetaGraphTransport _transport;

    private readonly MetaOAuthOptions _options;

    public GraphInstagramOAuthClient(HttpClient http, IOptions<MetaOAuthOptions> options)
        : this(http, options, Microsoft.Extensions.Options.Options.Create(new MetaGraphOptions()))
    {
    }

    public GraphInstagramOAuthClient(HttpClient http, IOptions<MetaOAuthOptions> options, IOptions<MetaGraphOptions> graphOptions)
    {
        _transport = new MetaGraphTransport(http, graphOptions.Value.TimeoutSeconds);
        _options = options.Value;
    }

    public async Task<CodeExchangeResult> ExchangeCodeAsync(CodeExchangeRequest request, CancellationToken cancellationToken = default)
    {
        var outcome = await _transport.PostFormAsync(_options.CodeExchangeEndpoint, new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["grant_type"] = CodeExchangeGrant,
            ["redirect_uri"] = request.RedirectUri,
            ["code"] = request.Code,
        }, cancellationToken);
        return InterpretCodeExchangeResponse(outcome);
    }

    public Task<LongLivedTokenResult> ExchangeShortLivedForLongLivedAsync(string shortLivedAccessToken, CancellationToken cancellationToken = default) =>
        FetchLongLivedAsync(BuildGraphUrl("access_token", ExchangeGrantType, shortLivedAccessToken), cancellationToken);

    public Task<LongLivedTokenResult> RefreshLongLivedAsync(string longLivedAccessToken, CancellationToken cancellationToken = default) =>
        FetchLongLivedAsync(BuildGraphUrl("refresh_access_token", RefreshGrantType, longLivedAccessToken), cancellationToken);

    private async Task<LongLivedTokenResult> FetchLongLivedAsync(string url, CancellationToken cancellationToken)
    {
        var outcome = await _transport.GetAsync(url, cancellationToken);
        if (outcome is MetaGraphCallResult.Rejected rejected
            && !rejected.Error.HasJsonBody
            && rejected.Error.HttpStatusCode is >= 200 and < 300)
        {
            return LongLivedTokenResult.Fail(new MetaOAuthFailure(
                MetaOAuthFailureReason.MalformedResponse,
                $"Meta returned non-JSON with status {rejected.Error.HttpStatusCode}."));
        }

        if (outcome is not MetaGraphCallResult.Success success)
        {
            return LongLivedTokenResult.Fail(ToFailure(outcome));
        }

        using (success.Document)
        {
            var root = success.Document.RootElement;
            if (root.TryGetProperty("access_token", out var accessToken)
                && root.TryGetProperty("expires_in", out var expiresIn)
                && expiresIn.TryGetInt64(out var seconds))
            {
                return LongLivedTokenResult.Ok(new LongLivedToken(accessToken.GetString()!, seconds));
            }
        }

        return LongLivedTokenResult.Fail(new MetaOAuthFailure(MetaOAuthFailureReason.MalformedResponse, "Payload did not match the documented shape."));
    }

    private static MetaOAuthFailure ToFailure(MetaGraphCallResult outcome) => outcome switch
    {
        MetaGraphCallResult.Rejected rejected => FromMetaError(rejected.Error),
        _ => new MetaOAuthFailure(MetaOAuthFailureReason.TransportFailure, "HTTP request failed."),
    };

    private static CodeExchangeResult InterpretCodeExchangeResponse(MetaGraphCallResult outcome)
    {
        if (outcome is MetaGraphCallResult.Success success)
        {
            using (success.Document)
            {
                var root = success.Document.RootElement;

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
            }

            return CodeExchangeResult.Fail(new MetaOAuthFailure(MetaOAuthFailureReason.MalformedResponse, "Payload did not match the documented shape."));
        }

        if (outcome is MetaGraphCallResult.Rejected rejected && !rejected.Error.HasJsonBody)
        {
            // Preserved contract: a 2xx non-JSON answer is malformed; anything else is
            // a provider rejection carrying only the status.
            return rejected.Error.HttpStatusCode is >= 200 and < 300
                ? CodeExchangeResult.Fail(new MetaOAuthFailure(
                    MetaOAuthFailureReason.MalformedResponse,
                    $"Meta returned non-JSON with status {rejected.Error.HttpStatusCode}."))
                : CodeExchangeResult.Fail(ToFailure(outcome));
        }

        return CodeExchangeResult.Fail(ToFailure(outcome));
    }

    /// <summary>
    /// Maps the canonical envelope to a structured failure. Only bounded, redacted
    /// error metadata is kept — never token or secret material (stripped at parse).
    /// </summary>
    private static MetaOAuthFailure FromMetaError(MetaGraphError error)
    {
        if (!error.HasJsonBody)
        {
            return new MetaOAuthFailure(
                MetaOAuthFailureReason.RejectedByMeta,
                $"Meta returned non-JSON with status {error.HttpStatusCode}.");
        }

        var detail = $"{error.HttpStatusCode} {error.Type ?? "Unknown"}";
        return new MetaOAuthFailure(
            MetaOAuthFailureReason.RejectedByMeta,
            string.IsNullOrEmpty(error.Message) ? detail : detail + ": " + error.Message);
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
