namespace Qasedak.Modules.Instagram.Application.Webhooks;

/// <summary>
/// Low-cardinality normalization observability (M13-008). Reason/kind strings come from a
/// fixed set defined in the normalizer — never account identifiers, participant ids or
/// payload content. Implementations must keep every tag category bounded.
/// </summary>
public interface IWebhookNormalizationObservability
{
    /// <summary>An integration event was normalized (kind: message | comment | postback | read | mention).</summary>
    void RecordNormalized(string kind);

    /// <summary>A recognized non-triggering fragment was skipped (reason: echo | self | deleted | unsupported | attachment-only | oversized | edit | reaction | referral | sender-missing | timestamp-missing | id-missing).</summary>
    void RecordIgnored(string reason);

    /// <summary>An unrecognized or unresolvable fragment was recorded (kind: malformed-json | unknown-shape | field:* | unresolved-account | ambiguous-account | ...).</summary>
    void RecordUnrecognized(string kind);
}
