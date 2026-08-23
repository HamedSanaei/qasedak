namespace Qasedak.Modules.Instagram.Application.Webhooks;

/// <summary>Outcome of validating an inbound Meta webhook event notification.</summary>
public readonly record struct WebhookSignatureResult(bool IsValid, WebhookSignatureFailure? Failure)
{
    public static WebhookSignatureResult Valid() => new(true, null);

    public static WebhookSignatureResult Invalid(WebhookSignatureFailure failure) => new(false, failure);
}

/// <summary>Why a webhook event notification failed authenticity validation.</summary>
public enum WebhookSignatureFailure
{
    /// <summary>The X-Hub-Signature-256 header is absent or malformed.</summary>
    InvalidSignatureHeader,

    /// <summary>The signature does not match the HMAC computed over the raw payload.</summary>
    SignatureMismatch,
}
