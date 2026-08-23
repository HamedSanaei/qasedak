using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Automations.Domain.Definitions;
using Xunit;

namespace Qasedak.Modules.Automations.UnitTests;

/// <summary>
/// Exhaustive deterministic-evaluator matrix: trigger kinds, keyword filters (any-of,
/// case-insensitivity, empty), condition operators/fields, AND semantics, action ordering
/// and repeat-call determinism.
/// </summary>
public sealed class AutomationEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 2, 10, 8, 0, 0, TimeSpan.Zero);

    private static TriggerContext Context(string? text = "what is the price?", string? sender = "sender-1") =>
        new("evt-1", TriggerKind.CommentCreated, "comment-1", sender, text, Now);

    private static AutomationDefinition Definition(
        string[]? keywords = null,
        AutomationCondition[]? conditions = null,
        string[]? actions = null) =>
        AutomationDefinition.Create(
            AutomationTrigger.CommentCreated(keywords ?? []),
            conditions ?? [],
            (actions ?? ["reply"]).Select(a => new AutomationAction(ActionKind.SendDirectMessage, a)));

    [Fact]
    public void MatchingKindWithNoFiltersMatchesAndReturnsOrderedActions()
    {
        var evaluation = AutomationEvaluator.Evaluate(Definition(actions: ["a", "b", "c"]), Context());

        Assert.True(evaluation.Matched);
        Assert.Null(evaluation.NonMatchReason);
        Assert.Equal(["a", "b", "c"], evaluation.OrderedActions.Select(x => x.MessageText));
    }

    [Fact]
    public void KindMismatchNeverMatches()
    {
        var evaluation = AutomationEvaluator.Evaluate(Definition(), Context());
        Assert.True(evaluation.Matched); // Sanity: same kind matches.

        // No other TriggerKind values exist yet; when added they must mismatch here.
        var mentionLike = Context() with { Kind = (TriggerKind)999 };
        Assert.False(AutomationEvaluator.Evaluate(Definition(), mentionLike).Matched);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingProducerEventIdIsRejected(string? eventId)
    {
        Assert.Throws<Domain.AutomationsDomainException>(
            () => AutomationEvaluator.Evaluate(Definition(), Context() with { EventId = eventId! }));
    }

    [Theory]
    [InlineData("PRICE", true)]
    [InlineData("price", true)]
    [InlineData("What is the PRICE of this?", true)]
    [InlineData("no relevant content", false)]
    public void KeywordFilterIsAnyOfCaseInsensitive(string text, bool expected)
    {
        var evaluation = AutomationEvaluator.Evaluate(
            Definition(keywords: ["price", "cost"], actions: ["dm"]),
            Context(text));

        Assert.Equal(expected, evaluation.Matched);
        if (!expected)
        {
            Assert.Equal("trigger.keywordFilter", evaluation.NonMatchReason);
        }
    }

    [Fact]
    public void EmptyKeywordFilterMatchesEverythingButNullTextStillMatches()
    {
        Assert.True(AutomationEvaluator.Evaluate(Definition(), Context(text: null)).Matched);
        Assert.True(AutomationEvaluator.Evaluate(Definition(), Context(text: "")).Matched);
    }

    [Fact]
    public void KeywordFilterWithNullTextNeverPasses()
    {
        var evaluation = AutomationEvaluator.Evaluate(Definition(keywords: ["buy"]), Context(text: null));

        Assert.False(evaluation.Matched);
        Assert.Equal("trigger.keywordFilter", evaluation.NonMatchReason);
    }

    [Fact]
    public void ContainsConditionIsCaseInsensitiveSubstring()
    {
        var definition = Definition(conditions: [AutomationCondition.TextContains("Refund")]);

        Assert.True(AutomationEvaluator.Evaluate(definition, Context(text: "i want a REFUND now")).Matched);
        Assert.False(AutomationEvaluator.Evaluate(definition, Context(text: "just asking")).Matched);
    }

    [Fact]
    public void EqualsConditionTrimsThenComparesOrdinally()
    {
        var definition = Definition(conditions: [AutomationCondition.TextEquals("stop")]);

        Assert.True(AutomationEvaluator.Evaluate(definition, Context(text: "  stop  ")).Matched);
        // Ordinal: case differences fail.
        Assert.False(AutomationEvaluator.Evaluate(definition, Context(text: "STOP")).Matched);
    }

    [Fact]
    public void SenderIdConditionEvaluatesAgainstSenderField()
    {
        var definition = Definition(conditions:
            [new AutomationCondition(ConditionField.SenderId, ConditionOperator.Equals, "sender-9")]);

        Assert.True(AutomationEvaluator.Evaluate(definition, Context(sender: "sender-9")).Matched);
        Assert.False(AutomationEvaluator.Evaluate(definition, Context(sender: "other")).Matched);
        Assert.False(AutomationEvaluator.Evaluate(definition, Context(sender: null)).Matched);
    }

    [Fact]
    public void MultipleConditionsRequireAllToHold()
    {
        var definition = Definition(conditions:
        [
            AutomationCondition.TextContains("price"),
            new AutomationCondition(ConditionField.SenderId, ConditionOperator.Equals, "vip-1"),
        ]);

        Assert.True(AutomationEvaluator.Evaluate(definition, Context(text: "price?", sender: "vip-1")).Matched);
        Assert.False(AutomationEvaluator.Evaluate(definition, Context(text: "price?", sender: "anon")).Matched);
        Assert.False(AutomationEvaluator.Evaluate(definition, Context(text: "hello", sender: "vip-1")).Matched);
    }

    [Fact]
    public void SameInputsProduceIdenticalVerdictsAcrossRepeatedCalls()
    {
        var definition = Definition(
            keywords: ["price"],
            conditions: [AutomationCondition.TextContains("price")],
            actions: ["first", "second"]);

        var first = AutomationEvaluator.Evaluate(definition, Context());
        for (var i = 0; i < 25; i++)
        {
            var again = AutomationEvaluator.Evaluate(definition, Context());
            Assert.Equal(first, again);
            Assert.Equal(first.OrderedActions.Select(a => a.MessageText), again.OrderedActions.Select(a => a.MessageText));
        }
    }
}
