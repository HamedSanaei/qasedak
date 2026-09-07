using Qasedak.Modules.Instagram.Application.Messaging;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests;

/// <summary>
/// Central verified message-limit policy (M13-010): exact boundaries from the current
/// first-party contract (retrieved 2026-09-07). Invalid content is rejected locally —
/// the adapter proves zero provider calls separately.
/// </summary>
public sealed class MessageValidationPolicyTests
{
    // ------------------------------------------------------------- plain text (UTF-8 ≤ 1000 bytes)

    [Fact]
    public void PlainTextAtByteLimitIsValid()
    {
        var content = new InstagramMessageContent.PlainText(new string('a', 1000));

        Assert.True(MessageValidationPolicy.Validate(content).IsValid);
    }

    [Fact]
    public void PlainTextOneByteOverLimitIsInvalid()
    {
        var content = new InstagramMessageContent.PlainText(new string('a', 1001));

        var result = MessageValidationPolicy.Validate(content);

        Assert.False(result.IsValid);
        Assert.Equal(MessageContentValidationCode.TextTooLong, result.Code);
    }

    [Fact]
    public void PlainTextLimitIsUtf8BytesNotCharacters()
    {
        // 500 Persian characters = 1000 UTF-8 bytes (2 bytes each) → valid;
        // 501 characters = 1002 bytes → invalid even though character count is lower.
        Assert.True(MessageValidationPolicy.Validate(new InstagramMessageContent.PlainText(new string('گ', 500))).IsValid);
        Assert.Equal(
            MessageContentValidationCode.TextTooLong,
            MessageValidationPolicy.Validate(new InstagramMessageContent.PlainText(new string('گ', 501))).Code);
    }

    [Fact]
    public void EmptyPlainTextIsInvalid()
    {
        Assert.False(MessageValidationPolicy.Validate(new InstagramMessageContent.PlainText("")).IsValid);
    }

    // ------------------------------------------------------------- template text (≤ 640 chars)

    [Fact]
    public void TemplateTextAtCharacterLimitIsValid()
    {
        var content = Template(new string('t', 640), new InstagramMessageButton.Postback("b", "p"));

        Assert.True(MessageValidationPolicy.Validate(content).IsValid);
    }

    [Fact]
    public void TemplateTextOneOverLimitIsInvalid()
    {
        var content = Template(new string('t', 641), new InstagramMessageButton.Postback("b", "p"));

        var result = MessageValidationPolicy.Validate(content);

        Assert.False(result.IsValid);
        Assert.Equal(MessageContentValidationCode.TemplateTextTooLong, result.Code);
    }

    // ------------------------------------------------------------- button count (1..3)

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void ButtonCountOutsideVerifiedRangeIsInvalid(int count)
    {
        var content = Template("prompt", Enumerable.Range(0, count).Select(i => (InstagramMessageButton)new InstagramMessageButton.Postback($"b{i}", "p")).ToArray());

        var result = MessageValidationPolicy.Validate(content);

        Assert.False(result.IsValid);
        Assert.Equal(MessageContentValidationCode.ButtonCountInvalid, result.Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void ButtonCountWithinVerifiedRangeIsValid(int count)
    {
        var content = Template("prompt", Enumerable.Range(0, count).Select(i => (InstagramMessageButton)new InstagramMessageButton.Postback($"b{i}", "p")).ToArray());

        Assert.True(MessageValidationPolicy.Validate(content).IsValid);
    }

    // ------------------------------------------------------------- postback title/payload

    [Fact]
    public void PostbackTitleAtLimitIsValidAndOneOverIsInvalid()
    {
        Assert.True(MessageValidationPolicy.Validate(Template(new InstagramMessageButton.Postback(new string('t', 20), "p"))).IsValid);
        Assert.Equal(
            MessageContentValidationCode.ButtonTitleTooLong,
            MessageValidationPolicy.Validate(Template(new InstagramMessageButton.Postback(new string('t', 21), "p"))).Code);
    }

    [Fact]
    public void PostbackPayloadAtLimitIsValidAndOneOverIsInvalid()
    {
        Assert.True(MessageValidationPolicy.Validate(Template(new InstagramMessageButton.Postback("t", new string('p', 1000)))).IsValid);
        Assert.Equal(
            MessageContentValidationCode.PayloadTooLong,
            MessageValidationPolicy.Validate(Template(new InstagramMessageButton.Postback("t", new string('p', 1001)))).Code);
    }

    // ------------------------------------------------------------- web_url title/url

    [Fact]
    public void WebUrlTitleAtLimitIsValidAndOneOverIsInvalid()
    {
        Assert.True(MessageValidationPolicy.Validate(Template(new InstagramMessageButton.WebUrl(new string('t', 20), "https://example.com"))).IsValid);
        Assert.Equal(
            MessageContentValidationCode.ButtonTitleTooLong,
            MessageValidationPolicy.Validate(Template(new InstagramMessageButton.WebUrl(new string('t', 21), "https://example.com"))).Code);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com")]
    public void HttpAndHttpsUrlsAreValid(string url)
    {
        Assert.True(MessageValidationPolicy.Validate(Template(new InstagramMessageButton.WebUrl("Visit", url))).IsValid);
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("/relative/path")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/file")]
    [InlineData("https://example.com/\u0000evil")]
    public void InvalidOrUnsupportedUrlsAreRejected(string url)
    {
        var result = MessageValidationPolicy.Validate(Template(new InstagramMessageButton.WebUrl("Visit", url)));

        Assert.False(result.IsValid);
        Assert.Equal(MessageContentValidationCode.UrlInvalid, result.Code);
    }

    [Fact]
    public void UrlBeyondBoundedSafetyCapIsRejectedNeverTruncated()
    {
        var url = "https://example.com/" + new string('x', MessageValidationPolicy.WebUrlMaxCharacters);

        var result = MessageValidationPolicy.Validate(Template(new InstagramMessageButton.WebUrl("Visit", url)));

        Assert.False(result.IsValid);
        Assert.Equal(MessageContentValidationCode.UrlInvalid, result.Code);
    }

    // ------------------------------------------------------------- mixed + order preservation

    [Fact]
    public void MixedPostbackAndWebUrlInOneTemplateIsValid()
    {
        var content = Template(
            "prompt",
            new InstagramMessageButton.Postback("A", "payload-a"),
            new InstagramMessageButton.WebUrl("B", "https://example.com"));

        Assert.True(MessageValidationPolicy.Validate(content).IsValid);
    }

    [Fact]
    public void ButtonOrderIsPreservedByPolicyAndSerialization()
    {
        var content = Template(
            "prompt",
            new InstagramMessageButton.WebUrl("first", "https://a.example"),
            new InstagramMessageButton.Postback("second", "p2"));

        var buttons = Assert.IsType<InstagramMessageContent.ButtonTemplate>(content).Buttons;
        Assert.Equal(2, buttons.Count);
        Assert.IsType<InstagramMessageButton.WebUrl>(buttons[0]);
        Assert.IsType<InstagramMessageButton.Postback>(buttons[1]);
    }

    // ------------------------------------------------------------- private reply gating

    [Fact]
    public void OnlyPlainTextIsSupportedForPrivateReply()
    {
        Assert.True(MessageValidationPolicy.IsSupportedForPrivateReply(new InstagramMessageContent.PlainText("hi")));
        Assert.False(MessageValidationPolicy.IsSupportedForPrivateReply(
            new InstagramMessageContent.ButtonTemplate("t", [new InstagramMessageButton.Postback("b", "p")])));
    }

    private static InstagramMessageContent.ButtonTemplate Template(string text, params InstagramMessageButton[] buttons) =>
        new(text, buttons);

    private static InstagramMessageContent.ButtonTemplate Template(params InstagramMessageButton[] buttons) =>
        new("prompt", buttons);
}
