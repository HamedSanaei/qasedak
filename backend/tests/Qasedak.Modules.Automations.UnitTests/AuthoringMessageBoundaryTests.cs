using System.Text;
using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;
using Xunit;

namespace Qasedak.Modules.Automations.UnitTests;

/// <summary>
/// Authoring-time bounds must be at least as strict as the shipped M13-010 provider
/// message contract (M13-012 correction §2, §5-12): PlainText kinds are bounded in UTF-8
/// BYTES, reveal GatePromptText in ≤640 characters, template button titles in ≤20
/// characters and the follow web-URL syntactically absolute http/https within 2000
/// characters. Every rejection happens at authoring — never deferred to runtime or to a
/// provider call.
/// </summary>
public sealed class AuthoringMessageBoundaryTests
{
    private static readonly string PersianChar = "گ"; // 2 UTF-8 bytes

    private static string Repeat(string unit, int count) => string.Concat(Enumerable.Repeat(unit, count));

    private static AutomationDefinition Direct(string text) => AutomationDefinition.Create(
        AutomationTrigger.InboundDirectMessage(), [new AutomationAction(ActionKind.DirectMessage, text)]);

    private static AutomationDefinition PrivateReply(string text) => AutomationDefinition.Create(
        AutomationTrigger.CommentCreated(), [new AutomationAction(ActionKind.SendPrivateReply, text)]);

    private static AutomationDefinition LegacyComment(string text) => AutomationDefinition.Create(
        AutomationTrigger.CommentCreated(), [new AutomationAction(ActionKind.SendDirectMessage, text)]);

    private static AutomationDefinition FollowUp(string text) => AutomationDefinition.Create(
        AutomationTrigger.InboundDirectMessage(),
        [new AutomationAction(ActionKind.ScheduleFollowUp, text, new ActionExtras(Delay: TimeSpan.FromMinutes(5)))]);

    private static AutomationDefinition RevealOpening(string text) => AutomationDefinition.Create(
        AutomationTrigger.CommentCreated(),
        [
            new AutomationAction(
                ActionKind.StartRevealFlow,
                text,
                new ActionExtras(Reveal: new RevealActionContent("gate", "دریافت", "reveal", FollowButtonTitle: "دنبال"))),
        ]);

    private static AutomationDefinition RevealWith(string gate, string postbackTitle, string revealText, string? followUrl = null, string? followTitle = null) =>
        AutomationDefinition.Create(
            AutomationTrigger.CommentCreated(),
            [
                new AutomationAction(
                    ActionKind.StartRevealFlow,
                    "opening",
                    new ActionExtras(Reveal: new RevealActionContent(gate, postbackTitle, revealText, followUrl, followTitle ?? "دنبال"))),
            ]);

    // ------------------------------------------------------------------ PlainText UTF-8 bytes

    [Fact]
    public void AsciiPlainTextAcceptsExactlyOneThousandBytes()
    {
        var oneThousand = new string('x', AutomationAction.MaxPlainTextBytes);
        Assert.Equal(1000, Encoding.UTF8.GetByteCount(oneThousand));
        Assert.NotNull(Direct(oneThousand));
    }

    [Fact]
    public void AsciiPlainTextRejectsOneThousandAndOneBytes()
    {
        var exception = Assert.Throws<AutomationsDomainException>(() => Direct(new string('x', AutomationAction.MaxPlainTextBytes + 1)));
        Assert.Equal("automation.actionTextTooLong", exception.RuleCode);
    }

    [Fact]
    public void PersianPlainTextBoundaryIsByteBasedNotCharacterBased()
    {
        // 500 × 2-byte Persian chars = exactly 1000 UTF-8 bytes → valid.
        var exactly = Repeat(PersianChar, 500);
        Assert.Equal(1000, Encoding.UTF8.GetByteCount(exactly));
        Assert.NotNull(Direct(exactly));

        // 1000 × 2-byte chars = 2000 bytes but only 1000 characters: the old
        // character-count rule accepted it — the UTF-8 byte rule must reject it.
        var twoThousandBytes = Repeat(PersianChar, 1000);
        Assert.Equal(1000, twoThousandBytes.Length);
        Assert.Equal(2000, Encoding.UTF8.GetByteCount(twoThousandBytes));
        var exception = Assert.Throws<AutomationsDomainException>(() => Direct(twoThousandBytes));
        Assert.Equal("automation.actionTextTooLong", exception.RuleCode);
    }

    [Fact]
    public void EmojiPlainTextBoundaryIsByteBasedNotCharacterBased()
    {
        // 250 × 4-byte emoji = 500 UTF-16 units but exactly 1000 UTF-8 bytes → valid.
        var exactly = Repeat("😀", 250);
        Assert.Equal(1000, Encoding.UTF8.GetByteCount(exactly));
        Assert.NotNull(Direct(exactly));

        // 251 × 4-byte emoji = 1004 UTF-8 bytes → rejected on bytes.
        var over = Repeat("😀", 251);
        Assert.Equal(1004, Encoding.UTF8.GetByteCount(over));
        var exception = Assert.Throws<AutomationsDomainException>(() => Direct(over));
        Assert.Equal("automation.actionTextTooLong", exception.RuleCode);
    }

    [Theory]
    [InlineData(ActionKind.SendPrivateReply)]
    [InlineData(ActionKind.SendDirectMessage)] // legacy comment trigger → Private Reply
    [InlineData(ActionKind.ScheduleFollowUp)]  // delayed Direct text
    [InlineData(ActionKind.StartRevealFlow)]   // opening Private Reply text
    public void EveryPlainTextMappedKindEnforcesUtf8Bytes(ActionKind kind)
    {
        // 1000 chars of Persian (2000 bytes) must be rejected for every PlainText kind.
        var overBytes = Repeat(PersianChar, 1000);
        Assert.True(overBytes.Length <= AutomationAction.MaxMessageLength);

        AutomationDefinition Build() => kind switch
        {
            ActionKind.SendPrivateReply => PrivateReply(overBytes),
            ActionKind.SendDirectMessage => LegacyComment(overBytes),
            ActionKind.ScheduleFollowUp => FollowUp(overBytes),
            _ => RevealOpening(overBytes),
        };

        var exception = Assert.Throws<AutomationsDomainException>(() => Build());
        Assert.Equal("automation.actionTextTooLong", exception.RuleCode);
    }

    // ------------------------------------------------------------------ Public reply stays distinct

    [Fact]
    public void PublicCommentReplyKeepsProductCharacterCapNotByteRule()
    {
        // 600 Persian chars = 1200 UTF-8 bytes but ≤1000 characters: public comment reply
        // is a different provider operation (comment text, not a Direct-message send), so
        // the M13-010 PlainText byte bound must NOT apply.
        var longPersian = Repeat(PersianChar, 600);
        Assert.Equal(1200, Encoding.UTF8.GetByteCount(longPersian));
        var definition = AutomationDefinition.Create(
            AutomationTrigger.CommentCreated(), [new AutomationAction(ActionKind.SendPublicReply, longPersian)]);
        Assert.Equal(longPersian, definition.Actions[0].MessageText);

        var overChars = new string('x', AutomationAction.MaxMessageLength + 1);
        var exception = Assert.Throws<AutomationsDomainException>(() => AutomationDefinition.Create(
            AutomationTrigger.CommentCreated(), [new AutomationAction(ActionKind.SendPublicReply, overChars)]));
        Assert.Equal("automation.actionTextTooLong", exception.RuleCode);
    }

    // ------------------------------------------------------------------ Reveal template matrix

    [Fact]
    public void GatePromptTextAcceptsSixHundredFortyCharacters()
    {
        var gate = new string('g', AutomationAction.MaxGatePromptTextLength);
        Assert.NotNull(RevealWith(gate, "postback", "reveal", "https://example.com", "follow"));
    }

    [Fact]
    public void GatePromptTextRejectsSixHundredFortyOneCharacters()
    {
        var exception = Assert.Throws<AutomationsDomainException>(() =>
            RevealWith(new string('g', AutomationAction.MaxGatePromptTextLength + 1), "postback", "reveal"));
        Assert.Equal("automation.gatePromptTooLong", exception.RuleCode);
    }

    [Fact]
    public void PostbackButtonTitleAcceptsTwentyAndRejectsTwentyOne()
    {
        Assert.NotNull(RevealWith("gate", new string('p', AutomationAction.MaxButtonTitleLength), "reveal"));

        var exception = Assert.Throws<AutomationsDomainException>(() =>
            RevealWith("gate", new string('p', AutomationAction.MaxButtonTitleLength + 1), "reveal"));
        Assert.Equal("automation.revealButtonTitleTooLong", exception.RuleCode);
    }

    [Fact]
    public void FollowButtonTitleAcceptsTwentyAndRejectsTwentyOne()
    {
        Assert.NotNull(RevealWith("gate", "postback", "reveal", "https://example.com", new string('f', AutomationAction.MaxButtonTitleLength)));

        var exception = Assert.Throws<AutomationsDomainException>(() =>
            RevealWith("gate", "postback", "reveal", "https://example.com", new string('f', AutomationAction.MaxButtonTitleLength + 1)));
        Assert.Equal("automation.revealButtonTitleTooLong", exception.RuleCode);
    }

    // ------------------------------------------------------------------ Follow URL

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com")]
    [InlineData("https://example.com/path?q=1#frag")]
    public void ValidFollowUrlsAreAccepted(string url)
    {
        Assert.NotNull(RevealWith("gate", "postback", "reveal", url, "follow"));
    }

    [Fact]
    public void FollowUrlAcceptsExactlyTwoThousandCharacters()
    {
        var url = "https://example.com/" + new string('a', AutomationAction.MaxFollowUrlLength - 20);
        Assert.Equal(AutomationAction.MaxFollowUrlLength, url.Length);
        Assert.NotNull(RevealWith("gate", "postback", "reveal", url, "follow"));
    }

    [Fact]
    public void FollowUrlRejectsTwoThousandAndOneCharacters()
    {
        var url = "https://example.com/" + new string('a', AutomationAction.MaxFollowUrlLength - 20 + 1);
        Assert.Equal(AutomationAction.MaxFollowUrlLength + 1, url.Length);
        var exception = Assert.Throws<AutomationsDomainException>(() =>
            RevealWith("gate", "postback", "reveal", url, "follow"));
        Assert.Equal("automation.revealFollowUrlTooLong", exception.RuleCode);
    }

    [Theory]
    [InlineData("https://")]
    [InlineData("https://[")]
    [InlineData("https:// example.com")]
    [InlineData("relative/path")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://example.com/\u0007")]
    public void MalformedFollowUrlsAreRejectedBySyntaxNotPrefix(string url)
    {
        var exception = Assert.Throws<AutomationsDomainException>(() =>
            RevealWith("gate", "postback", "reveal", url, "follow"));
        Assert.Equal("automation.revealFollowUrlScheme", exception.RuleCode);
    }

    [Fact]
    public void FollowUrlRejectsControlCharactersEvenWhenSyntacticallyAbsolute()
    {
        var url = "https://example.com/path\nmore";
        var exception = Assert.Throws<AutomationsDomainException>(() =>
            RevealWith("gate", "postback", "reveal", url, "follow"));
        Assert.Equal("automation.revealFollowUrlScheme", exception.RuleCode);
    }

    // ------------------------------------------------------------------ Reveal final text bytes

    [Fact]
    public void RevealTextEnforcesUtf8Bytes()
    {
        var exactly = Repeat(PersianChar, 500); // 1000 bytes
        Assert.NotNull(RevealWith("gate", "postback", exactly));

        var overBytes = Repeat(PersianChar, 1000); // 2000 bytes, 1000 chars
        var exception = Assert.Throws<AutomationsDomainException>(() => RevealWith("gate", "postback", overBytes));
        Assert.Equal("automation.revealTextTooLong", exception.RuleCode);
    }
}
