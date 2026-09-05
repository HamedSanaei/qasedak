using System.Globalization;
using System.Text.Json;

namespace Qasedak.Modules.Instagram.Infrastructure.Graph;

/// <summary>
/// Canonical Meta Graph failure taxonomy shared by every Instagram adapter
/// (M13-003). Classification is a pure function of (HTTP status, provider
/// code/subcode, message) so adapters and tests agree on one semantics.
/// </summary>
public enum MetaGraphFailure
{
    /// <summary>Token is invalid, malformed or rejected for a non-expired session reason.</summary>
    AuthenticationInvalid,

    /// <summary>Token expired (code 190 with expiry semantics).</summary>
    TokenExpired,

    /// <summary>Token invalidated: revocation, deauthorization, password/role events.</summary>
    Revoked,

    /// <summary>A required permission is missing or was removed.</summary>
    PermissionLoss,

    /// <summary>Rate limited by Meta (codes 4/17/32/613, HTTP 429).</summary>
    RateLimited,

    /// <summary>
    /// Recipient outside the 24-hour messaging window (code 10 + subcode 2534022).
    /// Distinct so callers schedule instead of retrying blindly. The historical
    /// code-490 mapping has no official standing and must not be reintroduced.
    /// </summary>
    MessagingWindowExpired,

    /// <summary>Malformed request/parameter (code 100 family).</summary>
    InvalidRequest,

    /// <summary>Target object does not exist.</summary>
    NotFound,

    /// <summary>Meta-side transient failure (HTTP 5xx where nothing more specific matched).</summary>
    Transient,

    /// <summary>Network/timeout/cancellation-level failure; Meta never answered.</summary>
    TransportFailure,

    /// <summary>2xx response that did not carry the documented shape.</summary>
    MalformedResponse,

    /// <summary>Classified error that fits no modeled bucket.</summary>
    Unknown,
}

/// <summary>
/// Canonical Graph error envelope: HTTP status, provider code/subcode/type, a
/// bounded log-safe message and the provider trace id for Meta support.
/// Token/secret material is stripped at parse time and can never appear here.
/// </summary>
public sealed record MetaGraphError(
    int HttpStatusCode,
    int? Code,
    int? Subcode,
    string? Type,
    string Message,
    string? FbTraceId,
    bool HasJsonBody = true)
{
    /// <summary>Whether retrying later (with backoff) can succeed.</summary>
    public static bool IsRetryable(MetaGraphFailure failure) => failure is
        MetaGraphFailure.RateLimited or MetaGraphFailure.Transient or MetaGraphFailure.TransportFailure;
}

/// <summary>
/// Parses both official Graph error shapes into one envelope:
/// {"error":{"message","type","code","error_subcode","fbtrace_id"}} and the flat
/// OAuth shape {error_type, code, error_message}. Messages are bounded (300 chars)
/// and redacted; unknown shapes yield an empty envelope instead of throwing.
/// </summary>
public static class MetaGraphErrorParser
{
    public static MetaGraphError Parse(int httpStatusCode, JsonDocument? document)
    {
        if (document is null)
        {
            return new MetaGraphError(httpStatusCode, null, null, null, $"Meta returned status {httpStatusCode}.", null, HasJsonBody: false);
        }

        var root = document.RootElement;
        var error = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var nested)
            && nested.ValueKind == JsonValueKind.Object
            ? nested
            : root;

        var code = ReadInt(error, "code");
        var subcode = ReadInt(error, "error_subcode");
        var type = ReadString(error, "type") ?? ReadString(error, "error_type");
        var message = ReadString(error, "message") ?? ReadString(error, "error_message") ?? string.Empty;
        var trace = ReadString(error, "fbtrace_id");

        return new MetaGraphError(httpStatusCode, code, subcode, type, Sanitize(message), trace);
    }

    /// <summary>Appends the trace suffix used in log-safe failure details.</summary>
    public static string WithTrace(string detail, string? fbTraceId) =>
        string.IsNullOrEmpty(fbTraceId) ? detail : $"{detail} (trace {fbTraceId})";

    private static int? ReadInt(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.GetString() is { } text
            ? text
            : null;

    /// <summary>Bounds Meta prose and strips anything shaped like credential material.</summary>
    internal static string Sanitize(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return string.Empty;
        }

        var safe = message.Length > 300 ? message[..300] : message;
        if (safe.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || safe.Contains("token=", StringComparison.OrdinalIgnoreCase)
            || safe.Contains("access_token", StringComparison.OrdinalIgnoreCase)
            || safe.StartsWith("EAAC", StringComparison.Ordinal)
            || safe.StartsWith("IG", StringComparison.Ordinal))
        {
            return "(message withheld)";
        }

        return safe;
    }
}

/// <summary>
/// Deterministic classifier over the canonical envelope. Mirrors the OQ-3 health
/// taxonomy (190/10/200/4/17/32 semantics preserved) and adds the official
/// window signal (10/2534022); unknown shapes stay Unknown so callers fail closed.
/// </summary>
public static class MetaGraphClassifier
{
    public static MetaGraphFailure Classify(MetaGraphError error) =>
        Classify(error.HttpStatusCode, error.Code, error.Subcode, error.Message);

    public static MetaGraphFailure Classify(int statusCode, int? code, int? subcode, string message)
    {
        message ??= string.Empty;

        if (code == 10 && subcode == 2534022)
        {
            return MetaGraphFailure.MessagingWindowExpired;
        }

        if (code == 190)
        {
            if (subcode is 463 or 467
                || message.Contains("invalidated", StringComparison.OrdinalIgnoreCase)
                || message.Contains("deauthoriz", StringComparison.OrdinalIgnoreCase))
            {
                return MetaGraphFailure.Revoked;
            }

            if (message.Contains("expired", StringComparison.OrdinalIgnoreCase))
            {
                return MetaGraphFailure.TokenExpired;
            }

            // 190 without expiry/invalidation semantics: treat as revoked session.
            return MetaGraphFailure.Revoked;
        }

        if (code is 10 or 200
            || message.Contains("permission", StringComparison.OrdinalIgnoreCase))
        {
            return MetaGraphFailure.PermissionLoss;
        }

        if (code is 4 or 17 or 32 or 613 || statusCode == 429)
        {
            return MetaGraphFailure.RateLimited;
        }

        if (statusCode == 404)
        {
            return MetaGraphFailure.NotFound;
        }

        if (code == 100)
        {
            return MetaGraphFailure.InvalidRequest;
        }

        if (statusCode >= 500)
        {
            return MetaGraphFailure.Transient;
        }

        return MetaGraphFailure.Unknown;
    }

    public static string Describe(MetaGraphFailure failure, MetaGraphError error)
    {
        var code = error.Code?.ToString(CultureInfo.InvariantCulture) ?? "?";
        var subcode = error.Subcode?.ToString(CultureInfo.InvariantCulture);
        var identity = subcode is null ? $"(code {code})" : $"(code {code}, subcode {subcode})";
        return failure switch
        {
            MetaGraphFailure.MessagingWindowExpired =>
                MetaGraphErrorParser.WithTrace($"Recipient is outside the 24-hour messaging window {identity}.", error.FbTraceId),
            MetaGraphFailure.RateLimited =>
                MetaGraphErrorParser.WithTrace($"Meta rate limit hit {identity}; retry later.", error.FbTraceId),
            MetaGraphFailure.InvalidRequest =>
                MetaGraphErrorParser.WithTrace($"{error.HttpStatusCode} {error.Type ?? "Unknown"} {identity}.", error.FbTraceId),
            _ => MetaGraphErrorParser.WithTrace(
                string.IsNullOrEmpty(error.Message)
                    ? $"Meta returned status {error.HttpStatusCode}."
                    : $"{error.HttpStatusCode} {error.Type ?? "Unknown"} {identity}: {error.Message}",
                error.FbTraceId),
        };
    }
}
