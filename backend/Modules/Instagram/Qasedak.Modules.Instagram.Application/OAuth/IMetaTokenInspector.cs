namespace Qasedak.Modules.Instagram.Application.OAuth;

/// <summary>
/// Classification of a live token inspection against Meta. The taxonomy is the OQ-3
/// resolution: error codes observed on token use map to exactly one of these kinds;
/// transient conditions are explicitly distinguished so health is never degraded by noise.
/// </summary>
public enum TokenInspectionKind
{
    /// <summary>Meta accepted the token.</summary>
    Healthy,

    /// <summary>Token expired (e.g. code 190 "has expired"); reconnect required.</summary>
    Expired,

    /// <summary>User deauthorized the app / session invalidated (code 190 subcodes 463/467).</summary>
    Revoked,

    /// <summary>Permission removed or no longer granted (code 10/200); actionable state required.</summary>
    PermissionLoss,

    /// <summary>Rate limit, 5xx or transport problem; health must remain untouched and retry later.</summary>
    Transient,
}

/// <summary>Result of inspecting one access token against Meta.</summary>
public readonly record struct TokenInspection(TokenInspectionKind Kind, string? Detail)
{
    public static TokenInspection Healthy() => new(TokenInspectionKind.Healthy, null);

    public static TokenInspection From(TokenInspectionKind kind, string detail) => new(kind, detail);
}

/// <summary>
/// Inspects a raw access token against a lightweight Meta endpoint. Implementations never
/// throw for Meta-rejected tokens and never include token values in details.
/// </summary>
public interface IMetaTokenInspector
{
    Task<TokenInspection> InspectAsync(string accessToken, CancellationToken cancellationToken = default);
}
