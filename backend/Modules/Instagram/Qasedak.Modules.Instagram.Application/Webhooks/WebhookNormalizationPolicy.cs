namespace Qasedak.Modules.Instagram.Application.Webhooks;

/// <summary>
/// Single source of bounds for webhook normalization (M13-008). Provider-supplied payload
/// fields are bounded defensively; oversized values surface as non-triggering fragments —
/// they are never silently truncated where truncation would change automation semantics.
/// </summary>
public static class WebhookNormalizationPolicy
{
    /// <summary>
    /// Defensive ceiling for inbound message text. The official inbound text limit is a
    /// carried-over assumption (§6 of the Meta contract); the Conversations projection
    /// truncates at 1000 chars anyway. Anything beyond this ceiling is degenerate and
    /// becomes an ignored "message-oversized" fragment — normalization never mutates text.
    /// </summary>
    public const int MaxInboundTextLength = 10_000;

    /// <summary>Defensive ceiling for comment text; oversized comments become ignored fragments.</summary>
    public const int MaxCommentTextLength = 10_000;

    /// <summary>
    /// Postback payload bound (defensive; button payloads are provider-limited far below
    /// this). Oversized payloads become an unrecognized "postback-oversized" fragment —
    /// never truncated, since truncation would change automation semantics.
    /// </summary>
    public const int MaxPostbackPayloadLength = 1_000;

    /// <summary>Postback title bound — button text is verified at ≤640 chars (contract §3.2).</summary>
    public const int MaxPostbackTitleLength = 640;

    /// <summary>Quick-reply payload bound (defensive). Oversized payloads are dropped from the event as metadata only.</summary>
    public const int MaxQuickReplyPayloadLength = 1_000;

    /// <summary>Commenter username bound; usernames are display metadata (verified ≤30 in practice).</summary>
    public const int MaxCommenterUsernameLength = 256;

    /// <summary>
    /// Valid provider timestamp window (milliseconds since epoch). Current official
    /// examples are 13-digit millisecond values (2017+); anything outside this window is
    /// malformed (wrong units or out of range) and falls back to entry.time, then to an
    /// ignored fragment — never a local wall-clock guess.
    /// </summary>
    public const long MinValidTimestampMilliseconds = 978_307_200_000; // 2001-01-01T00:00:00Z

    public const long MaxValidTimestampMilliseconds = 4_102_444_800_000; // 2100-01-01T00:00:00Z
}
