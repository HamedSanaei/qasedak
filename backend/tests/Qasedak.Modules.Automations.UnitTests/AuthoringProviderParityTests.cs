using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;
using Qasedak.Modules.Instagram.Application.Messaging;
using Qasedak.Modules.Instagram.Application.RevealFlow;
using Xunit;

namespace Qasedak.Modules.Automations.UnitTests;

/// <summary>
/// Cross-module contract regression (M13-012 correction §17-18): the Automations module is
/// channel-neutral and must never reference Instagram production projects, so authoring
/// mirrors the M13-010 verified bounds. THIS test project is allowed to reference both
/// contracts and proves the invariant that keeps the mirror from drifting:
///
///     every NEW v2 definition accepted by Automations for an Instagram-backed
///     Direct / Private Reply / Reveal operation also passes the corresponding
///     M13-010 local MessageValidationPolicy after mapping,
///
/// and that every downstream constraint is rejected at authoring BEFORE persistence — the
/// M13-010 policy is never the first line of defense.
/// </summary>
public sealed class AuthoringProviderParityTests
{
    private const string PersianChar = "گ"; // 2 UTF-8 bytes

    private static string ExactlyPlainTextBytes(int bytes)
    {
        // Persian fills the UTF-8 budget at exactly 2 bytes per character.
        Assert.Equal(0, bytes % 2);
        return new string(PersianChar[0], bytes / 2);
    }

    private static void AssertValid(InstagramMessageContent content)
    {
        var result = MessageValidationPolicy.Validate(content);
        Assert.True(result.IsValid, $"M13-010 rejected an authoring-accepted definition: {result.Reason}");
    }

    // ------------------------------------------------------------------ positive parity

    [Fact]
    public void MaximumDirectMessagePassesProviderPlainTextValidation()
    {
        var text = ExactlyPlainTextBytes(MessageValidationPolicy.PlainTextMaxBytes);
        var definition = AutomationDefinition.Create(
            AutomationTrigger.InboundDirectMessage(), [new AutomationAction(ActionKind.DirectMessage, text)]);

        AssertValid(new InstagramMessageContent.PlainText(definition.Actions[0].MessageText));
    }

    [Fact]
    public void MaximumPrivateReplyPassesProviderPlainTextValidation()
    {
        var text = ExactlyPlainTextBytes(MessageValidationPolicy.PlainTextMaxBytes);
        var definition = AutomationDefinition.Create(
            AutomationTrigger.CommentCreated(), [new AutomationAction(ActionKind.SendPrivateReply, text)]);

        AssertValid(new InstagramMessageContent.PlainText(definition.Actions[0].MessageText));
    }

    [Fact]
    public void LegacyCommentSendDirectMessagePassesProviderPlainTextValidation()
    {
        // Historical CommentCreated + SendDirectMessage(1) routes to the Private Reply —
        // its text is a PlainText message in the M13-009 bridge.
        var text = ExactlyPlainTextBytes(MessageValidationPolicy.PlainTextMaxBytes);
        var definition = AutomationDefinition.Create(
            AutomationTrigger.CommentCreated(), [new AutomationAction(ActionKind.SendDirectMessage, text)]);

        AssertValid(new InstagramMessageContent.PlainText(definition.Actions[0].MessageText));
    }

    [Fact]
    public void DelayedDirectFollowUpPassesProviderPlainTextValidation()
    {
        var text = ExactlyPlainTextBytes(MessageValidationPolicy.PlainTextMaxBytes);
        var definition = AutomationDefinition.Create(
            AutomationTrigger.InboundDirectMessage(),
            [new AutomationAction(ActionKind.ScheduleFollowUp, text, new ActionExtras(Delay: TimeSpan.FromMinutes(5)))]);

        AssertValid(new InstagramMessageContent.PlainText(definition.Actions[0].MessageText));
    }

    [Fact]
    public void MaximumRevealConfigurationPassesEveryProviderMessage()
    {
        // Boundary-legal reveal configuration (M13-012 correction §20): the opening
        // Private Reply, the ButtonTemplate gate and the final Direct reveal must ALL
        // pass the M13-010 policy — including the 2000-char web-URL follow button.
        var opening = ExactlyPlainTextBytes(MessageValidationPolicy.PlainTextMaxBytes);
        var revealText = ExactlyPlainTextBytes(MessageValidationPolicy.PlainTextMaxBytes);
        var gate = new string('g', MessageValidationPolicy.ButtonTemplateTextMaxCharacters);
        var postbackTitle = new string('p', MessageValidationPolicy.ButtonTitleMaxCharacters);
        var followTitle = new string('f', MessageValidationPolicy.ButtonTitleMaxCharacters);
        var followUrl = "https://example.com/" + new string('a', MessageValidationPolicy.WebUrlMaxCharacters - 20);

        var definition = AutomationDefinition.Create(
            AutomationTrigger.CommentCreated(),
            [
                new AutomationAction(
                    ActionKind.StartRevealFlow,
                    opening,
                    new ActionExtras(Reveal: new RevealActionContent(gate, postbackTitle, revealText, followUrl, followTitle))),
            ]);
        var reveal = definition.Actions[0].Extras!.Reveal!;

        // Mirrors AutomationRevealBridge + RevealFlowCoordinator.ValidateContent exactly:
        // opening → PlainText; gate → ButtonTemplate(Postback + WebUrl); reveal → PlainText.
        AssertValid(new InstagramMessageContent.PlainText(definition.Actions[0].MessageText));
        AssertValid(new InstagramMessageContent.PlainText(reveal.RevealText));
        var buttons = new List<InstagramMessageButton>
        {
            new InstagramMessageButton.Postback(reveal.PostbackButtonTitle, RevealCorrelation.TokenPurpose + ".placeholder"),
            new InstagramMessageButton.WebUrl(reveal.FollowButtonTitle!, reveal.FollowUrl!),
        };
        AssertValid(new InstagramMessageContent.ButtonTemplate(reveal.GatePromptText, buttons));
    }

    // ------------------------------------------------------------------ negative parity

    [Fact]
    public void OverBytePlainTextIsRejectedByAuthoringBeforeProviderValidation()
    {
        // 1000 Persian characters = 2000 bytes: rejected at authoring.
        var overBytes = new string(PersianChar[0], MessageValidationPolicy.PlainTextMaxBytes);
        Assert.Throws<AutomationsDomainException>(() => AutomationDefinition.Create(
            AutomationTrigger.InboundDirectMessage(), [new AutomationAction(ActionKind.DirectMessage, overBytes)]));
    }

    [Fact]
    public void OverLengthGatePromptIsRejectedByAuthoringBeforeProviderValidation()
    {
        var gate = new string('g', MessageValidationPolicy.ButtonTemplateTextMaxCharacters + 1);
        Assert.Throws<AutomationsDomainException>(() => AutomationDefinition.Create(
            AutomationTrigger.CommentCreated(),
            [
                new AutomationAction(
                    ActionKind.StartRevealFlow,
                    "opening",
                    new ActionExtras(Reveal: new RevealActionContent(gate, "postback", "reveal", FollowButtonTitle: "دنبال"))),
            ]));
    }

    [Fact]
    public void OverLengthButtonTitleIsRejectedByAuthoringBeforeProviderValidation()
    {
        var title = new string('p', MessageValidationPolicy.ButtonTitleMaxCharacters + 1);
        Assert.Throws<AutomationsDomainException>(() => AutomationDefinition.Create(
            AutomationTrigger.CommentCreated(),
            [
                new AutomationAction(
                    ActionKind.StartRevealFlow,
                    "opening",
                    new ActionExtras(Reveal: new RevealActionContent("gate", title, "reveal", FollowButtonTitle: "دنبال"))),
            ]));
    }

    [Fact]
    public void OverLengthFollowUrlIsRejectedByAuthoringBeforeProviderValidation()
    {
        var url = "https://example.com/" + new string('a', MessageValidationPolicy.WebUrlMaxCharacters - 20 + 1);
        Assert.Throws<AutomationsDomainException>(() => AutomationDefinition.Create(
            AutomationTrigger.CommentCreated(),
            [
                new AutomationAction(
                    ActionKind.StartRevealFlow,
                    "opening",
                    new ActionExtras(Reveal: new RevealActionContent("gate", "postback", "reveal", url, "follow"))),
            ]));
    }

    [Theory]
    [InlineData("https://[")]
    [InlineData("https://")]
    [InlineData("https:// example.com")]
    [InlineData("relative/path")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://example.com/\u0007")]
    public void MalformedOrUnsupportedSchemeUrlsAreRejectedByAuthoring(string url)
    {
        Assert.Throws<AutomationsDomainException>(() => AutomationDefinition.Create(
            AutomationTrigger.CommentCreated(),
            [
                new AutomationAction(
                    ActionKind.StartRevealFlow,
                    "opening",
                    new ActionExtras(Reveal: new RevealActionContent("gate", "postback", "reveal", url, "follow"))),
            ]));
    }

    [Fact]
    public void PublicCommentReplyIsNotSubjectToDirectTemplateValidation()
    {
        // The public-reply edge is a different provider operation: a comment text that
        // exceeds the M13-010 PlainText BYTE budget but stays within the product
        // character cap must remain authorable — proving the byte rule never leaks into
        // public replies (M13-012 correction §6, §22).
        var overPlainTextBytes = new string(PersianChar[0], MessageValidationPolicy.PlainTextMaxBytes);
        Assert.True(overPlainTextBytes.Length <= 1000);

        var definition = AutomationDefinition.Create(
            AutomationTrigger.CommentCreated(),
            [new AutomationAction(ActionKind.SendPublicReply, overPlainTextBytes)]);

        Assert.Equal(overPlainTextBytes, definition.Actions[0].MessageText);
    }
}
