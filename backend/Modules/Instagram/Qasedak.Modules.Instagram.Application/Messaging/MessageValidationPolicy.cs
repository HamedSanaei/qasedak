using System.Text;

namespace Qasedak.Modules.Instagram.Application.Messaging;

/// <summary>Stable local-validation verdict codes for outbound message content.</summary>
public enum MessageContentValidationCode
{
    Valid,

    /// <summary>Plain text exceeds the verified UTF-8 byte limit.</summary>
    TextTooLong,

    /// <summary>Button-template prompt text exceeds the verified character limit.</summary>
    TemplateTextTooLong,

    /// <summary>Button count outside the verified 1..3 range.</summary>
    ButtonCountInvalid,

    /// <summary>A button title exceeds the verified character limit.</summary>
    ButtonTitleTooLong,

    /// <summary>A postback payload exceeds the verified character limit.</summary>
    PayloadTooLong,

    /// <summary>A web-URL button URL is malformed, has an unsupported scheme or is oversized.</summary>
    UrlInvalid,
}

public sealed record MessageContentValidationResult(bool IsValid, MessageContentValidationCode Code, string Reason)
{
    public static MessageContentValidationResult Valid() => new(true, MessageContentValidationCode.Valid, "ok");

    public static MessageContentValidationResult Invalid(MessageContentValidationCode code, string reason) =>
        new(false, code, reason);
}

/// <summary>
/// ONE central verified source of truth for outbound message limits (M13-010).
///
/// Verified against current first-party Meta documentation (retrieved 2026-09-07):
/// - plain text: UTF-8, 1000 bytes or less — "Send Messages" (Instagram API with
///   Instagram Login);
/// - button-template text: UTF-8, up to 640 characters — "Button Template" (IG Login);
/// - buttons: 1-3 per template; types postback | web_url only — "Button Template" (IG Login);
/// - button title: 20 character limit; postback payload: 1000 character limit — the
///   Messenger Platform "Buttons" reference, which the IG Login Generic Template page
///   links as the button-object specification for Instagram templates;
/// - web-URL button: http/https URL; current IG pages state no numeric URL maximum —
///   <see cref="WebUrlMaxCharacters"/> is Qasedak's bounded safety cap (never a silent
///   truncation: over-limit content is rejected locally with zero provider calls).
///
/// Qasedak never silently truncates payloads, URLs or titles: changing them changes
/// behavior/correlation, so over-limit content is rejected before any provider call.
/// The provider remains the final enforcement authority for anything not locally provable.
/// </summary>
public static class MessageValidationPolicy
{
    /// <summary>Plain text messages: UTF-8 bytes, ≤ 1000.</summary>
    public const int PlainTextMaxBytes = 1000;

    /// <summary>Button-template prompt text: characters, ≤ 640.</summary>
    public const int ButtonTemplateTextMaxCharacters = 640;

    /// <summary>Buttons per template: 1..3.</summary>
    public const int MinButtonCount = 1;

    public const int MaxButtonCount = 3;

    /// <summary>Button title: characters, ≤ 20.</summary>
    public const int ButtonTitleMaxCharacters = 20;

    /// <summary>Postback payload: characters, ≤ 1000.</summary>
    public const int PostbackPayloadMaxCharacters = 1000;

    /// <summary>web-URL button URL: characters, ≤ 2000 (Qasedak bounded safety cap).</summary>
    public const int WebUrlMaxCharacters = 2000;

    public static MessageContentValidationResult Validate(InstagramMessageContent content)
    {
        switch (content)
        {
            case null:
                return MessageContentValidationResult.Invalid(MessageContentValidationCode.TextTooLong, "message content required");
            case InstagramMessageContent.PlainText { Text: null or "" }:
                return MessageContentValidationResult.Invalid(MessageContentValidationCode.TextTooLong, "message text required");
            case InstagramMessageContent.PlainText { Text: var text }:
                return Encoding.UTF8.GetByteCount(text) <= PlainTextMaxBytes
                    ? MessageContentValidationResult.Valid()
                    : MessageContentValidationResult.Invalid(
                        MessageContentValidationCode.TextTooLong,
                        $"plain text exceeds the {PlainTextMaxBytes}-byte limit");
            case InstagramMessageContent.ButtonTemplate { Text: null or "" }:
                return MessageContentValidationResult.Invalid(MessageContentValidationCode.TemplateTextTooLong, "template text required");
            case InstagramMessageContent.ButtonTemplate { Text: var templateText, Buttons: var buttons }:
                if (templateText.Length > ButtonTemplateTextMaxCharacters)
                {
                    return MessageContentValidationResult.Invalid(
                        MessageContentValidationCode.TemplateTextTooLong,
                        $"button-template text exceeds the {ButtonTemplateTextMaxCharacters}-character limit");
                }

                if (buttons.Count is < MinButtonCount or > MaxButtonCount)
                {
                    return MessageContentValidationResult.Invalid(
                        MessageContentValidationCode.ButtonCountInvalid,
                        $"button count must be between {MinButtonCount} and {MaxButtonCount}");
                }

                for (var i = 0; i < buttons.Count; i++)
                {
                    var result = ValidateButton(buttons[i]);
                    if (!result.IsValid)
                    {
                        return result with { Reason = $"buttons[{i}]: {result.Reason}" };
                    }
                }

                return MessageContentValidationResult.Valid();
            default:
                // Closed type set — unknown kinds cannot exist, but fail closed anyway.
                return MessageContentValidationResult.Invalid(MessageContentValidationCode.UrlInvalid, "unsupported content kind");
        }
    }

    private static MessageContentValidationResult ValidateButton(InstagramMessageButton button)
    {
        switch (button)
        {
            case InstagramMessageButton.Postback { Title: var title, Payload: var payload }:
                if (string.IsNullOrEmpty(title) || title.Length > ButtonTitleMaxCharacters)
                {
                    return MessageContentValidationResult.Invalid(
                        MessageContentValidationCode.ButtonTitleTooLong,
                        $"postback button title must be 1..{ButtonTitleMaxCharacters} characters");
                }

                if (string.IsNullOrEmpty(payload) || payload.Length > PostbackPayloadMaxCharacters)
                {
                    return MessageContentValidationResult.Invalid(
                        MessageContentValidationCode.PayloadTooLong,
                        $"postback payload must be 1..{PostbackPayloadMaxCharacters} characters");
                }

                return MessageContentValidationResult.Valid();
            case InstagramMessageButton.WebUrl { Title: var title, Url: var url }:
                if (string.IsNullOrEmpty(title) || title.Length > ButtonTitleMaxCharacters)
                {
                    return MessageContentValidationResult.Invalid(
                        MessageContentValidationCode.ButtonTitleTooLong,
                        $"web_url button title must be 1..{ButtonTitleMaxCharacters} characters");
                }

                if (IsInvalidUrl(url))
                {
                    return MessageContentValidationResult.Invalid(
                        MessageContentValidationCode.UrlInvalid,
                        "web_url button URL must be an absolute http/https URL within the bounded length");
                }

                return MessageContentValidationResult.Valid();
            default:
                return MessageContentValidationResult.Invalid(MessageContentValidationCode.UrlInvalid, "unsupported button kind");
        }
    }

    private static bool IsInvalidUrl(string url)
    {
        if (string.IsNullOrEmpty(url) || url.Length > WebUrlMaxCharacters || url.Any(char.IsControl))
        {
            return true;
        }

        return !Uri.TryCreate(url, UriKind.Absolute, out var uri)
               || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps);
    }

    /// <summary>
    /// M13-010 provider-gating rule: the current first-party Private Reply guide
    /// documents only message:{text} — button templates on the recipient.comment_id path
    /// are NOT independently verified, so only plain text is supported there. This must
    /// never be inferred from Direct Message template support.
    /// </summary>
    public static bool IsSupportedForPrivateReply(InstagramMessageContent content) => content is InstagramMessageContent.PlainText;
}
