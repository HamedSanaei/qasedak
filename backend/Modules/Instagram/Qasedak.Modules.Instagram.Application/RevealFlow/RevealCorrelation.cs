using System.Security.Cryptography;
using System.Text;

namespace Qasedak.Modules.Instagram.Application.RevealFlow;

/// <summary>
/// Correlation token for reveal-flow postbacks (M13-011). The raw token is
/// purpose-bound and versioned ("rv1." + 48 base64url chars = 36 bytes = 288 bits of
/// CSPRNG entropy) and appears ONLY inside the provider postback payload; the flow row
/// persists only its SHA-256 hash. After the gate-prompt Attempting marker the raw token
/// is deliberately NOT needed again — no automatic second prompt is ever sent — so losing
/// the raw value on a crash is safe by design.
/// </summary>
public static class RevealCorrelation
{
    /// <summary>Version + purpose prefix; the messaging layer never interprets beyond this.</summary>
    public const string TokenPurpose = "rv1";

    private const int RawTokenBytes = 36;

    /// <summary>Bounded length guard: prefix + base64url(36 bytes) = 3 + 48 = 51 chars.</summary>
    public const int MaxRawTokenLength = 64;

    public sealed record CreatedToken(string Raw, string Sha256Hash);

    public static CreatedToken Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(RawTokenBytes);
        var raw = TokenPurpose + "." + Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return new CreatedToken(raw, Hash(raw));
    }

    /// <summary>Canonical SHA-256 hex hash of the raw token — the only persisted form.</summary>
    public static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();

    /// <summary>
    /// Parses and bounds-checks a provider postback payload. Accepts exactly the rv1.
    /// purpose/version; any other prefix, unknown version, undersized entropy or oversized
    /// value is invalid (zero provider calls downstream).
    /// </summary>
    public static bool TryParse(string payload, out string rawToken)
    {
        rawToken = string.Empty;
        if (string.IsNullOrEmpty(payload)
            || payload.Length > MaxRawTokenLength
            || !payload.StartsWith(TokenPurpose + ".", StringComparison.Ordinal))
        {
            return false;
        }

        var encoded = payload[(TokenPurpose.Length + 1)..];
        if (encoded.Length < 16 || encoded.Length > 64)
        {
            // base64url(36 bytes) is 48 chars; allow only realistic lengths.
            return false;
        }

        try
        {
            var padded = encoded.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
            var decoded = Convert.FromBase64String(padded);
            if (decoded.Length < 16)
            {
                // At least 128 bits of entropy is required.
                return false;
            }

            rawToken = payload;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
