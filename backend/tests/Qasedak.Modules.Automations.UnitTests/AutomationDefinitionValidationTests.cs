using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;
using Xunit;

namespace Qasedak.Modules.Automations.UnitTests;

/// <summary>
/// Authoring-time rejection surface (M13-012 §30-31, §64): trigger/action compatibility,
/// private-reply effect conflicts, source scope and follow-up/reveal bounds. Invalid
/// definitions must never persist and fail only at webhook time.
/// </summary>
public sealed class AutomationDefinitionValidationTests
{
    [Fact]
    public void LegacySendDirectMessageAllowedOnBothTriggers()
    {
        Assert.NotNull(AutomationDefinition.Create(
            AutomationTrigger.CommentCreated(), [new AutomationAction(ActionKind.SendDirectMessage, "hi")]));
        Assert.NotNull(AutomationDefinition.Create(
            AutomationTrigger.InboundDirectMessage(), [new AutomationAction(ActionKind.SendDirectMessage, "hi")]));
    }

    [Theory]
    [InlineData(TriggerKind.CommentCreated, ActionKind.DirectMessage)]
    [InlineData(TriggerKind.InboundDirectMessage, ActionKind.SendPrivateReply)]
    [InlineData(TriggerKind.InboundDirectMessage, ActionKind.StartRevealFlow)]
    [InlineData(TriggerKind.InboundDirectMessage, ActionKind.SendPublicReply)]
    public void TriggerActionMatrixRejectsInvalidKinds(TriggerKind trigger, ActionKind action)
    {
        var exception = Assert.Throws<AutomationsDomainException>(() => AutomationDefinition.Create(
            new AutomationTrigger(trigger, []), [new AutomationAction(action, "hi")]));

        Assert.Equal("automation.actionNotAllowedForTrigger", exception.RuleCode);
    }

    [Theory]
    [InlineData(ActionKind.SendDirectMessage)] // legacy = Private Reply on a comment trigger
    [InlineData(ActionKind.SendPrivateReply)]
    [InlineData(ActionKind.StartRevealFlow)]
    public void ConflictingPrivateReplyEffectsAreRejected(ActionKind first)
    {
        var firstAction = first == ActionKind.StartRevealFlow
            ? new AutomationAction(first, "one", new ActionExtras(Reveal: new RevealActionContent("gate", "دریافت", "reveal", FollowButtonTitle: "دنبال")))
            : new AutomationAction(first, "one");
        var exception = Assert.Throws<AutomationsDomainException>(() => AutomationDefinition.Create(
            AutomationTrigger.CommentCreated(),
            [
                firstAction,
                new AutomationAction(ActionKind.SendPrivateReply, "two"),
            ]));

        Assert.Equal("automation.conflictingPrivateReplyEffects", exception.RuleCode);
    }

    [Fact]
    public void PrivateAndPublicEffectsMayCoexist()
    {
        var definition = AutomationDefinition.Create(
            AutomationTrigger.CommentCreated(),
            [
                new AutomationAction(ActionKind.SendPrivateReply, "private"),
                new AutomationAction(ActionKind.SendPublicReply, "public"),
            ]);

        Assert.Equal(2, definition.Actions.Count);
    }

    [Fact]
    public void DmTriggerCannotCarryMediaScope()
    {
        var exception = Assert.Throws<AutomationsDomainException>(() => AutomationDefinition.Create(
            new AutomationTrigger(TriggerKind.InboundDirectMessage, [], Source: SourceScope.SpecificSource, SourceMediaId: "media-1"),
            [new AutomationAction(ActionKind.DirectMessage, "hi")]));

        Assert.Equal("automation.dmSourceScopeNotApplicable", exception.RuleCode);
    }

    [Fact]
    public void SpecificSourceRequiresBoundedMediaId()
    {
        Assert.Throws<AutomationsDomainException>(() => AutomationDefinition.Create(
            new AutomationTrigger(TriggerKind.CommentCreated, [], Source: SourceScope.SpecificSource, SourceMediaId: "   "),
            [new AutomationAction(ActionKind.SendPublicReply, "hi")]));

        var tooLong = new string('x', AutomationAction.MaxSourceMediaIdLength + 1);
        var exception = Assert.Throws<AutomationsDomainException>(() => AutomationDefinition.Create(
            new AutomationTrigger(TriggerKind.CommentCreated, [], Source: SourceScope.SpecificSource, SourceMediaId: tooLong),
            [new AutomationAction(ActionKind.SendPublicReply, "hi")]));
        Assert.Equal("automation.sourceMediaIdTooLong", exception.RuleCode);
    }

    [Fact]
    public void WholeWordRequiresKeywordMode()
    {
        var exception = Assert.Throws<AutomationsDomainException>(() => AutomationDefinition.Create(
            new AutomationTrigger(TriggerKind.CommentCreated, [], WholeWord: true),
            [new AutomationAction(ActionKind.SendPublicReply, "hi")]));

        Assert.Equal("automation.wholeWordRequiresKeywords", exception.RuleCode);
    }

    [Fact]
    public void KeywordModeRequiresAtLeastOneKeyword()
    {
        var exception = Assert.Throws<AutomationsDomainException>(() => AutomationDefinition.Create(
            new AutomationTrigger(TriggerKind.CommentCreated, [], TextMatch: TextMatchMode.Keywords),
            [new AutomationAction(ActionKind.SendPublicReply, "hi")]));

        Assert.Equal("automation.keywordRequired", exception.RuleCode);
    }

    [Fact]
    public void FollowUpDelayBoundsAreProductOwned()
    {
        var zero = Assert.Throws<AutomationsDomainException>(() => AutomationDefinition.Create(
            AutomationTrigger.InboundDirectMessage(),
            [new AutomationAction(ActionKind.ScheduleFollowUp, "later", new ActionExtras(Delay: TimeSpan.Zero))]));
        Assert.Equal("automation.followUpDelayInvalid", zero.RuleCode);

        var negative = Assert.Throws<AutomationsDomainException>(() => AutomationDefinition.Create(
            AutomationTrigger.InboundDirectMessage(),
            [new AutomationAction(ActionKind.ScheduleFollowUp, "later", new ActionExtras(Delay: TimeSpan.FromMinutes(-5)))]));
        Assert.Equal("automation.followUpDelayInvalid", negative.RuleCode);

        var multiYear = Assert.Throws<AutomationsDomainException>(() => AutomationDefinition.Create(
            AutomationTrigger.InboundDirectMessage(),
            [new AutomationAction(ActionKind.ScheduleFollowUp, "later", new ActionExtras(Delay: TimeSpan.FromDays(365)))]));
        Assert.Equal("automation.followUpDelayInvalid", multiYear.RuleCode);

        var valid = AutomationDefinition.Create(
            AutomationTrigger.InboundDirectMessage(),
            [new AutomationAction(ActionKind.ScheduleFollowUp, "later", new ActionExtras(Delay: TimeSpan.FromDays(7)))]);
        Assert.Equal(TimeSpan.FromDays(7), valid.Actions[0].Extras!.Delay);
    }

    [Fact]
    public void RevealContentIsValidated()
    {
        var missing = Assert.Throws<AutomationsDomainException>(() => AutomationDefinition.Create(
            AutomationTrigger.CommentCreated(),
            [new AutomationAction(ActionKind.StartRevealFlow, "opening")]));
        Assert.Equal("automation.revealContentRequired", missing.RuleCode);

        var emptyGate = Assert.Throws<AutomationsDomainException>(() => AutomationDefinition.Create(
            AutomationTrigger.CommentCreated(),
            [
                new AutomationAction(
                    ActionKind.StartRevealFlow,
                    "opening",
                    new ActionExtras(Reveal: new RevealActionContent("", "دریافت", "reveal"))),
            ]));
        Assert.Equal("automation.revealTextRequired", emptyGate.RuleCode);

        var badUrl = Assert.Throws<AutomationsDomainException>(() => AutomationDefinition.Create(
            AutomationTrigger.CommentCreated(),
            [
                new AutomationAction(
                    ActionKind.StartRevealFlow,
                    "opening",
                    new ActionExtras(Reveal: new RevealActionContent("gate", "دریافت", "reveal", FollowUrl: "not-a-url", FollowButtonTitle: "دنبال"))),
            ]));
        Assert.Equal("automation.revealFollowUrlScheme", badUrl.RuleCode);
    }

    [Fact]
    public void ExtrasOnPlainActionsAreRejected()
    {
        var exception = Assert.Throws<AutomationsDomainException>(() => AutomationDefinition.Create(
            AutomationTrigger.CommentCreated(),
            [new AutomationAction(ActionKind.SendPublicReply, "hi", new ActionExtras(Delay: TimeSpan.FromMinutes(10)))]));

        Assert.Equal("automation.actionExtrasNotApplicable", exception.RuleCode);
    }

    [Fact]
    public void FollowUpAndRevealDoNotMix()
    {
        var exception = Assert.Throws<AutomationsDomainException>(() => AutomationDefinition.Create(
            AutomationTrigger.CommentCreated(),
            [
                new AutomationAction(
                    ActionKind.ScheduleFollowUp,
                    "later",
                    new ActionExtras(Delay: TimeSpan.FromMinutes(5), Reveal: new RevealActionContent("g", "b", "r"))),
            ]));

        Assert.Equal("automation.actionExtrasNotApplicable", exception.RuleCode);
    }

    [Fact]
    public void EnumNumericValuesAreFrozenHistoricalContract()
    {
        // Persisted numeric contract (M13-012 §8): never renumber, only append.
        Assert.Equal(1, (int)TriggerKind.CommentCreated);
        Assert.Equal(2, (int)TriggerKind.InboundDirectMessage);
        Assert.Equal(1, (int)ActionKind.SendDirectMessage);
        Assert.Equal(2, (int)ActionKind.SendPrivateReply);
        Assert.Equal(3, (int)ActionKind.DirectMessage);
        Assert.Equal(4, (int)ActionKind.StartRevealFlow);
        Assert.Equal(5, (int)ActionKind.SendPublicReply);
        Assert.Equal(6, (int)ActionKind.ScheduleFollowUp);
    }
}
