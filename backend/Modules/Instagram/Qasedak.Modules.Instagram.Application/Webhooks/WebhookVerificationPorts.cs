namespace Qasedak.Modules.Instagram.Application.Webhooks;

/// <summary>
/// Verifies the authenticity of Meta webhook event notifications.
/// Per Meta's documented contract, event payloads carry a SHA256 HMAC of the raw request
/// body in the X-Hub-Signature-256 header, formatted as "sha256=&lt;lowercase hex&gt;",
/// keyed with the app secret.
/// </summary>
public interface IWebhookSignatureVerifier
{
    /// <summary>Verifies the signature header against the raw (unmodified) request body.</summary>
    /// <param name="rawBody">The exact bytes received; never re-serialized.</param>
    /// <param name="signatureHeader">The X-Hub-Signature-256 header value, if present.</param>
    WebhookSignatureResult Verify(byte[] rawBody, string? signatureHeader);
}

/// <summary>Outcome of validating a Meta webhook subscription verification request (GET handshake).</summary>
public readonly record struct WebhookSubscriptionResult(
    bool IsValid,
    WebhookSubscriptionFailure? Failure,
    string Challenge)
{
    public static WebhookSubscriptionResult Valid(string challenge) => new(true, null, challenge);

    public static WebhookSubscriptionResult Invalid(WebhookSubscriptionFailure failure) => new(false, failure, string.Empty);
}

/// <summary>Why a webhook subscription verification request was rejected.</summary>
public enum WebhookSubscriptionFailure
{
    /// <summary>hub.mode is missing or is not "subscribe".</summary>
    InvalidMode,

    /// <summary>hub.verify_token is missing or does not match the configured verification token.</summary>
    TokenMismatch,

    /// <summary>hub.challenge is missing and cannot be echoed back.</summary>
    MissingChallenge,
}

/// <summary>
/// Validates Meta's webhook subscription verification handshake: Meta issues a GET with
/// hub.mode=subscribe, hub.challenge, and hub.verify_token; the endpoint must confirm the
/// token and echo back the challenge verbatim.
/// </summary>
public interface IWebhookSubscriptionValidator
{
    WebhookSubscriptionResult Validate(string? mode, string? verifyToken, string? challenge);
}
