using System.Security.Cryptography;

namespace Qasedak.BuildingBlocks.Infrastructure.Diagnostics;

/// <summary>
/// Centralized privacy redaction for structured logs and audit records. Secrets are
/// replaced by a stable non-reversible marker that preserves length class for debugging
/// without ever exposing content. Hashes use SHA-256 over a salted value so the same
/// secret always maps to the same fingerprint (correlatable, not reversible).
/// </summary>
public static class Sensitive
{
    public const string MarkerPrefix = "[redacted:";

    /// <summary>Full redaction: reveals only the length class.</summary>
    public static string Redact(string? secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return string.Empty;
        }

        return $"{MarkerPrefix}len={secret.Length}]";
    }

    /// <summary>Partial mask for identifiers where a suffix aids support (e.g. provider ids).</summary>
    public static string MaskTail(string? identifier, int visibleTail = 4)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return string.Empty;
        }

        var tail = Math.Min(visibleTail, identifier.Length);
        return identifier.Length <= tail
            ? Redact(identifier)
            : string.Create(identifier.Length, (identifier, tail), static (span, state) =>
            {
                span.Fill('*');
                state.identifier.AsSpan(^state.tail..).CopyTo(span[^state.tail..]);
            });
    }

    /// <summary>Deterministic fingerprint for correlating repeated secrets without storing them.</summary>
    public static string Fingerprint(string secret)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("qasedak::" + secret));
        return "fp_" + Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}
