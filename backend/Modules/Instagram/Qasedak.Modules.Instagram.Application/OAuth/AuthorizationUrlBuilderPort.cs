namespace Qasedak.Modules.Instagram.Application.OAuth;

/// <summary>
/// The Instagram Business Login scope set Qasedak requests, per the verified token-lifecycle
/// contract (docs/product/meta-oauth-token-lifecycle.md §3). Order matters only for
/// deterministic URLs; Meta treats the list as unordered.
/// </summary>
public static class InstagramAuthorizationScopes
{
    public const string Basic = "instagram_business_basic";

    public const string ContentPublish = "instagram_business_content_publish";

    public const string ManageMessages = "instagram_business_manage_messages";

    public const string ManageComments = "instagram_business_manage_comments";

    /// <summary>Scopes requested for the fast Business Login connection flow.</summary>
    public static readonly string[] Default =
    [
        Basic,
        ContentPublish,
        ManageMessages,
        ManageComments,
    ];
}

/// <summary>Input for building the Business Login authorization URL.</summary>
public sealed record AuthorizationUrlRequest(string RedirectUri, string State);

/// <summary>The absolute URL the user agent must open to start Business Login.</summary>
public sealed record AuthorizationUrl(string Value);

/// <summary>
/// Builds the Instagram Business Login embed/authorization URL:
/// https://www.instagram.com/oauth/authorize with client_id, redirect_uri,
/// response_type=code, comma-separated scopes and an anti-CSRF state value.
/// </summary>
public interface IAuthorizationUrlBuilder
{
    AuthorizationUrl Build(AuthorizationUrlRequest request);
}
