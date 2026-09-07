using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Automations.Domain.Definitions;
using Xunit;

namespace Qasedak.Modules.Automations.UnitTests;

/// <summary>
/// v2 evaluator semantics (M13-012 §17-27): source scope with MediaId/OriginalMediaId,
/// explicit EveryEvent/Keywords modes, whole-word opt-in, DM triggers without media scope.
/// </summary>
public sealed class AutomationEvaluatorV2Tests
{
    private static readonly DateTimeOffset Now = new(2026, 2, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AnySourceMatchesUnderExactAccountOnly()
    {
        var definition = AutomationDefinition.Create(
            AutomationTrigger.CommentCreated(),
            [new AutomationAction(ActionKind.SendPublicReply, "ok")]);

        // The evaluator is account-agnostic (the use case enforces exact-account binding);
        // any media id qualifies under AnySource.
        Assert.True(AutomationEvaluator.Evaluate(definition, Comment("17841400000000000_media_1")).Matched);
        Assert.True(AutomationEvaluator.Evaluate(definition, Comment("17841400000000000_media_2")).Matched);
    }

    [Fact]
    public void SpecificSourceMatchesMediaIdOrOriginalMediaId()
    {
        var definition = AutomationDefinition.Create(
            new AutomationTrigger(TriggerKind.CommentCreated, [], Source: SourceScope.SpecificSource, SourceMediaId: "17841400000000000_post_9"),
            [new AutomationAction(ActionKind.SendPublicReply, "ok")]);

        Assert.True(AutomationEvaluator.Evaluate(definition, Comment(mediaId: "17841400000000000_post_9")).Matched);
        Assert.True(AutomationEvaluator.Evaluate(definition, Comment(mediaId: "17841400000000000_ad_5", originalMediaId: "17841400000000000_post_9")).Matched);
        Assert.False(AutomationEvaluator.Evaluate(definition, Comment(mediaId: "17841400000000000_ad_5")).Matched);
        Assert.False(AutomationEvaluator.Evaluate(definition, Comment(mediaId: "17841400000000000_other")).Matched);
    }

    [Fact]
    public void DmTriggerIgnoresMediaScopeFields()
    {
        var definition = AutomationDefinition.Create(
            AutomationTrigger.InboundDirectMessage("price"),
            [new AutomationAction(ActionKind.DirectMessage, "here")]);

        // DM context carries no media fields; scope stays NotApplicable.
        Assert.True(AutomationEvaluator.Evaluate(definition, Dm("what is the price?", "mid_1")).Matched);
    }

    [Fact]
    public void EveryEventIgnoresKeywordsExplicitly()
    {
        var definition = AutomationDefinition.Create(
            new AutomationTrigger(TriggerKind.CommentCreated, ["price"], TextMatch: TextMatchMode.EveryEvent),
            [new AutomationAction(ActionKind.SendPublicReply, "ok")]);

        Assert.True(AutomationEvaluator.Evaluate(definition, Comment(text: "anything at all")).Matched);
    }

    [Fact]
    public void KeywordSubstringRemainsAnyOfCaseInsensitive()
    {
        var definition = AutomationDefinition.Create(
            new AutomationTrigger(TriggerKind.CommentCreated, ["قیمت", "offer"], TextMatch: TextMatchMode.Keywords),
            [new AutomationAction(ActionKind.SendPublicReply, "ok")]);

        Assert.True(AutomationEvaluator.Evaluate(definition, Comment(text: "قیمت چنده")).Matched);
        Assert.True(AutomationEvaluator.Evaluate(definition, Comment(text: "YOUR OFFER TODAY")).Matched);
        Assert.False(AutomationEvaluator.Evaluate(definition, Comment(text: "سلام")).Matched);
    }

    [Fact]
    public void WholeWordIsOptInAndStricterThanSubstring()
    {
        var substring = AutomationDefinition.Create(
            new AutomationTrigger(TriggerKind.CommentCreated, ["cat"], TextMatch: TextMatchMode.Keywords),
            [new AutomationAction(ActionKind.SendPublicReply, "ok")]);
        var wholeWord = AutomationDefinition.Create(
            new AutomationTrigger(TriggerKind.CommentCreated, ["cat"], TextMatch: TextMatchMode.Keywords, WholeWord: true),
            [new AutomationAction(ActionKind.SendPublicReply, "ok")]);

        // "catalog" contains "cat" as substring but not as a whole word.
        Assert.True(AutomationEvaluator.Evaluate(substring, Comment(text: "see my catalog")).Matched);
        Assert.False(AutomationEvaluator.Evaluate(wholeWord, Comment(text: "see my catalog")).Matched);
        Assert.True(AutomationEvaluator.Evaluate(wholeWord, Comment(text: "my cat 🐈")).Matched);
    }

    [Fact]
    public void DmTriggerMatchesOnMessageText()
    {
        var definition = AutomationDefinition.Create(
            AutomationTrigger.InboundDirectMessage("price"),
            [new AutomationAction(ActionKind.DirectMessage, "here")]);

        Assert.True(AutomationEvaluator.Evaluate(definition, Dm("PRICE please", "mid_1")).Matched);
        Assert.False(AutomationEvaluator.Evaluate(definition, Dm("hi there", "mid_2")).Matched);
    }

    private static TriggerContext Comment(string? mediaId = null, string? originalMediaId = null, string? text = "hello") =>
        new("evt-1", TriggerKind.CommentCreated, "comment-1", "sender-1", text, Now, mediaId, originalMediaId);

    private static TriggerContext Dm(string text, string mid) =>
        new(mid, TriggerKind.InboundDirectMessage, mid, "participant-1", text, Now);
}
