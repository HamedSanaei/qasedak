using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.Webhooks;

namespace Qasedak.Modules.Instagram.Infrastructure.Webhooks;

/// <summary>Configuration for Meta webhook verification, bound from "Instagram:Meta".</summary>
public sealed class MetaWebhookOptions
{
    public const string SectionName = "Instagram:Meta";

    /// <summary>Meta App Secret used to key the payload HMAC.</summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>Shared secret echoed in hub.verify_token during subscription verification.</summary>
    public string VerifyToken { get; set; } = string.Empty;
}

/// <summary>
/// Computes HMAC-SHA256 over the raw request bytes with the configured app secret and compares
/// against the "sha256=&lt;hex&gt;" header value using a constant-time comparison.
/// The raw bytes must be exactly as received: Meta signs an escaped-unicode serialization of
/// the payload, so re-serialized bodies will not validate.
/// </summary>
public sealed class HmacWebhookSignatureVerifier : IWebhookSignatureVerifier
{
    private const string SignaturePrefix = "sha256=";

    private readonly byte[] _secretBytes;

    public HmacWebhookSignatureVerifier(IOptions<MetaWebhookOptions> options)
    {
        _secretBytes = System.Text.Encoding.UTF8.GetBytes(options.Value.AppSecret);
    }

    public WebhookSignatureResult Verify(byte[] rawBody, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader)
            || !signatureHeader.StartsWith(SignaturePrefix, StringComparison.Ordinal))
        {
            return WebhookSignatureResult.Invalid(WebhookSignatureFailure.InvalidSignatureHeader);
        }

        var provided = signatureHeader[SignaturePrefix.Length..];
        if (provided.Length != 64 || !IsLowerCaseHex(provided))
        {
            return WebhookSignatureResult.Invalid(WebhookSignatureFailure.InvalidSignatureHeader);
        }

        var expected = System.Security.Cryptography.HMACSHA256.HashData(_secretBytes, rawBody);
        var expectedHex = Convert.ToHexString(expected).ToLowerInvariant();
        var providedBytes = System.Text.Encoding.UTF8.GetBytes(provided);
        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expectedHex);

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes)
            ? WebhookSignatureResult.Valid()
            : WebhookSignatureResult.Invalid(WebhookSignatureFailure.SignatureMismatch);
    }

    private static bool IsLowerCaseHex(string value)
    {
        foreach (var c in value)
        {
            var isHexDigit = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
            if (!isHexDigit)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Validates Meta's documented subscription handshake: hub.mode must be "subscribe", the
/// hub.verify_token must equal the configured token, and hub.challenge must be echoed verbatim.
/// </summary>
public sealed class MetaWebhookSubscriptionValidator : IWebhookSubscriptionValidator
{
    private const string ExpectedMode = "subscribe";

    private readonly MetaWebhookOptions _options;

    public MetaWebhookSubscriptionValidator(IOptions<MetaWebhookOptions> options)
    {
        _options = options.Value;
    }

    public WebhookSubscriptionResult Validate(string? mode, string? verifyToken, string? challenge)
    {
        if (!string.Equals(mode, ExpectedMode, StringComparison.Ordinal))
        {
            return WebhookSubscriptionResult.Invalid(WebhookSubscriptionFailure.InvalidMode);
        }

        if (string.IsNullOrEmpty(_options.VerifyToken)
            || string.IsNullOrEmpty(verifyToken)
            || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(verifyToken),
                System.Text.Encoding.UTF8.GetBytes(_options.VerifyToken)))
        {
            return WebhookSubscriptionResult.Invalid(WebhookSubscriptionFailure.TokenMismatch);
        }

        return string.IsNullOrEmpty(challenge)
            ? WebhookSubscriptionResult.Invalid(WebhookSubscriptionFailure.MissingChallenge)
            : WebhookSubscriptionResult.Valid(challenge);
    }
}
