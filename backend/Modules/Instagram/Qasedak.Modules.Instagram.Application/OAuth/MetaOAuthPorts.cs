namespace Qasedak.Modules.Instagram.Application.OAuth;

/// <summary>Input for exchanging an authorization code for a short-lived token.</summary>
public sealed record CodeExchangeRequest(string Code, string RedirectUri);

/// <summary>Successful code exchange: short-lived token plus app-scoped identity.</summary>
public sealed record CodeExchangeSuccess(
    string AccessToken,
    string InstagramUserId,
    IReadOnlyList<string> GrantedPermissions);

/// <summary>Why a Meta OAuth HTTP interaction failed.</summary>
public enum MetaOAuthFailureReason
{
    /// <summary>Meta answered with an OAuth error payload (e.g. invalid/expired/used code).</summary>
    RejectedByMeta,

    /// <summary>The HTTP call itself failed (network, timeout, non-JSON body).</summary>
    TransportFailure,

    /// <summary>A 2xx response did not carry the documented payload shape.</summary>
    MalformedResponse,
}

/// <summary>Structured failure of a Meta OAuth interaction; never contains secret values.</summary>
public sealed record MetaOAuthFailure(MetaOAuthFailureReason Reason, string Detail)
{
    public override string ToString() => $"{Reason}: {Detail}";
}

/// <summary>Outcome of the code → short-lived-token exchange.</summary>
public readonly record struct CodeExchangeResult(
    CodeExchangeSuccess? Success,
    MetaOAuthFailure? Failure)
{
    public static CodeExchangeResult Ok(CodeExchangeSuccess success) => new(success, null);

    public static CodeExchangeResult Fail(MetaOAuthFailure failure) => new(null, failure);
}

/// <summary>Outcome of short-lived→long-lived exchange and of refresh calls.</summary>
public readonly record struct LongLivedTokenResult(
    LongLivedToken? Success,
    MetaOAuthFailure? Failure)
{
    public static LongLivedTokenResult Ok(LongLivedToken token) => new(token, null);

    public static LongLivedTokenResult Fail(MetaOAuthFailure failure) => new(null, failure);
}

/// <summary>A long-lived Instagram User access token with its remaining validity.</summary>
public sealed record LongLivedToken(string AccessToken, long ExpiresInSeconds);

/// <summary>
/// Meta OAuth HTTP adapter boundary. All calls are server-side only (the app secret is
/// involved); implementations must never throw for Meta-rejected requests and must never
/// include token or secret values in failure details.
/// </summary>
public interface IMetaOAuthClient
{
    /// <summary>
    /// Exchanges a single-use authorization code (valid one hour) for a short-lived
    /// Instagram User access token, app-scoped user id and granted permissions.
    /// </summary>
    Task<CodeExchangeResult> ExchangeCodeAsync(CodeExchangeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges a valid short-lived token for a ~60-day long-lived token
    /// (grant_type=ig_exchange_token). Server-side only.
    /// </summary>
    Task<LongLivedTokenResult> ExchangeShortLivedForLongLivedAsync(string shortLivedAccessToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes a valid long-lived token for another 60 days (grant_type=ig_refresh_token).
    /// Preconditions enforced by Meta: token ≥24h old, still valid, instagram_business_basic granted.
    /// </summary>
    Task<LongLivedTokenResult> RefreshLongLivedAsync(string longLivedAccessToken, CancellationToken cancellationToken = default);
}
