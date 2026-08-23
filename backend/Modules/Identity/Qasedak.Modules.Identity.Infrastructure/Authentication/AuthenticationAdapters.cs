using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Qasedak.BuildingBlocks.Application;
using Qasedak.Modules.Identity.Application.Authentication;
using Qasedak.Modules.Identity.Domain.Users;

namespace Qasedak.Modules.Identity.Infrastructure.Authentication;

/// <summary>Authentication adapter options, bound from "Identity:Auth".</summary>
public sealed class IdentityAuthOptions
{
    public const string SectionName = "Identity:Auth";

    /// <summary>PBKDF2 iteration count. 210_000 follows current OWASP guidance for SHA-256.</summary>
    public int Pbkdf2Iterations { get; set; } = 210_000;

    /// <summary>Symmetric signing key for tokens; must be at least 32 characters.</summary>
    public string TokenSigningKey { get; set; } = string.Empty;

    /// <summary>Token lifetime in hours. Default: 12.</summary>
    public int TokenLifetimeHours { get; set; } = 12;
}

/// <summary>
/// PBKDF2 (HMAC-SHA256) password hashing with per-hash random salt. Storage format:
/// "pbkdf2-sha256.&lt;iterations&gt;.&lt;base64 salt&gt;.&lt;base64 subkey&gt;" so verification
/// parameters travel with the hash.
/// </summary>
public sealed class Pbkdf2PasswordHasher(IOptions<IdentityAuthOptions> options) : IPasswordHasher
{
    private const string FormatId = "pbkdf2-sha256";

    private const int SaltSize = 16;

    private const int SubkeySize = 32;

    private readonly int _iterations = options.Value.Pbkdf2Iterations >= 100_000
        ? options.Value.Pbkdf2Iterations
        : throw new ArgumentOutOfRangeException(nameof(options), "PBKDF2 iterations must be at least 100_000.");

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var subkey = Rfc2898DeriveBytes.Pbkdf2(password, salt, _iterations, HashAlgorithmName.SHA256, SubkeySize);
        return $"{FormatId}.{_iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(subkey)}";
    }

    public bool Verify(string password, string storedHash)
    {
        var parts = storedHash.Split('.');
        if (parts.Length != 4 || parts[0] != FormatId
            || !int.TryParse(parts[1], out var iterations) || iterations < 1)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>
/// Compact signed-token issuer: base64url(JSON payload) + "." + base64url(HMACSHA256).
/// Payload binds user id, canonical email, issued-at and expiry; validation enforces both
/// signature (constant time) and lifetime against the injected clock. Configuration is
/// resolved per use so an unconfigured host still boots (health endpoints stay up);
/// the first token operation fails loudly instead.
/// </summary>
public sealed class HmacSecurityTokenIssuer(IOptionsMonitor<IdentityAuthOptions> options, IClock clock)
    : ISecurityTokenIssuer
{
    private readonly IClock _clock = clock;

    public SecurityToken Issue(Guid userId, EmailAddress email)
    {
        var (signingKey, lifetime) = ResolveConfiguration();
        var expiresAtUtc = _clock.UtcNow.Add(lifetime);
        var payload = new TokenPayload(userId, email.Value, _clock.UtcNow.ToUnixTimeSeconds(), expiresAtUtc.ToUnixTimeSeconds());
        var payloadBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload);
        var signature = Sign(signingKey, payloadBytes);
        var value = Base64UrlEncode(payloadBytes) + "." + Base64UrlEncode(signature);
        return new SecurityToken(value, expiresAtUtc);
    }

    public TokenValidationResult Validate(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return default;
        }

        var separatorIndex = token.IndexOf('.');
        if (separatorIndex <= 0 || separatorIndex == token.Length - 1)
        {
            return default;
        }

        byte[] payloadBytes;
        byte[] signature;
        try
        {
            payloadBytes = Base64UrlDecode(token[..separatorIndex]);
            signature = Base64UrlDecode(token[(separatorIndex + 1)..]);
        }
        catch (FormatException)
        {
            return default;
        }

        var (signingKey, _) = ResolveConfiguration();
        var expectedSignature = Sign(signingKey, payloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(signature, expectedSignature))
        {
            return default;
        }

        TokenPayload? payload;
        try
        {
            payload = System.Text.Json.JsonSerializer.Deserialize<TokenPayload>(payloadBytes);
        }
        catch (System.Text.Json.JsonException)
        {
            return default;
        }

        if (payload is null || payload.Exp <= _clock.UtcNow.ToUnixTimeSeconds())
        {
            return default;
        }

        return new TokenValidationResult(true, payload.Sub, payload.Email);
    }

    private (byte[] SigningKey, TimeSpan Lifetime) ResolveConfiguration()
    {
        var value = options.CurrentValue;
        if (string.IsNullOrEmpty(value.TokenSigningKey) || value.TokenSigningKey.Length < 32)
        {
            throw new InvalidOperationException("Identity:Auth:TokenSigningKey must be configured with at least 32 characters.");
        }

        return (System.Text.Encoding.UTF8.GetBytes(value.TokenSigningKey), TimeSpan.FromHours(value.TokenLifetimeHours));
    }

    private static byte[] Sign(byte[] signingKey, byte[] data) => new HMACSHA256(signingKey).ComputeHash(data);

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String((padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            _ => padded,
        });
    }

    private sealed record TokenPayload(Guid Sub, string Email, long Iat, long Exp);
}
