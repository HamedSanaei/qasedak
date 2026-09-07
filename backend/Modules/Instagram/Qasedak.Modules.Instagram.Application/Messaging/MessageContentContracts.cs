namespace Qasedak.Modules.Instagram.Application.Messaging;

/// <summary>
/// Transport-free, Qasedak-owned content for Instagram outbound messages (M13-010).
/// Closed discriminated types: an unknown content kind cannot exist at compile time and
/// cannot be serialized into arbitrary Meta JSON. Addressing stays on the focused
/// operations (DirectMessage recipient.id / PrivateReply recipient.comment_id) — content
/// never carries a recipient.
/// </summary>
public abstract record InstagramMessageContent
{
    private protected InstagramMessageContent()
    {
    }

    /// <summary>
    /// Plain text message. Verified current IG Login limit: UTF-8, 1000 bytes or less
    /// ("Send Messages with IG Login", retrieved 2026-09-07).
    /// </summary>
    public sealed record PlainText(string Text) : InstagramMessageContent;

    /// <summary>
    /// Button template: prompt text (up to 640 characters) plus 1..3 postback/web-URL
    /// buttons ("Button Template with IG Login", retrieved 2026-09-07).
    /// </summary>
    public sealed record ButtonTemplate(string Text, IReadOnlyList<InstagramMessageButton> Buttons) : InstagramMessageContent;
}

/// <summary>
/// Closed set of button kinds supported by the current IG Login button template
/// contract: postback and web_url only. Declaration order is user-visible semantics and
/// is preserved by serialization.
/// </summary>
public abstract record InstagramMessageButton
{
    private protected InstagramMessageButton()
    {
    }

    /// <summary>
    /// postback button: sends the opaque <see cref="Payload"/> to the webhook as a
    /// messaging_postbacks notification when tapped. Payload is application-owned opaque
    /// data — the messaging layer never interprets it (M13-011 owns correlation).
    /// </summary>
    public sealed record Postback(string Title, string Payload) : InstagramMessageButton;

    /// <summary>web_url button: opens <see cref="Url"/> in the in-app browser.</summary>
    public sealed record WebUrl(string Title, string Url) : InstagramMessageButton;
}
