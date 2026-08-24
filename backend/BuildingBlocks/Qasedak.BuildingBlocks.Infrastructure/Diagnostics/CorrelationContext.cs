namespace Qasedak.BuildingBlocks.Infrastructure.Diagnostics;

/// <summary>Per-request correlation identity (immutable within the request).</summary>
public interface ICorrelationContext
{
    string CorrelationId { get; }
}

/// <summary>Default scoped implementation filled by the correlation middleware.</summary>
public sealed class CorrelationContext : ICorrelationContext
{
    public CorrelationContext(string correlationId)
    {
        CorrelationId = CorrelationIds.IsValid(correlationId)
            ? correlationId
            : throw new ArgumentException("Invalid correlation id.", nameof(correlationId));
    }

    public string CorrelationId { get; }
}

/// <summary>
/// Correlation id rules: 8..128 chars of [A-Za-z0-9-_]. Inbound ids from trusted clients
/// are honored; anything else is replaced by a fresh generated id.
/// </summary>
public static class CorrelationIds
{
    public const string HeaderName = "X-Correlation-Id";

    private static readonly System.Text.RegularExpressions.Regex Pattern =
        new("^[A-Za-z0-9_-]{8,128}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static bool IsValid(string? candidate) =>
        !string.IsNullOrWhiteSpace(candidate) && Pattern.IsMatch(candidate);

    /// <summary>URL-safe 22-char id derived from a GUIDv7 (time-ordered, collision-safe).</summary>
    public static string NewId()
    {
        var bytes = Guid.CreateVersion7().ToByteArray();
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
