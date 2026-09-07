using System.Text.Json;
using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Automations.Domain.Definitions;
using Qasedak.Modules.Automations.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Modules.Automations.UnitTests;

/// <summary>
/// Storage-format compatibility (M13-012 §10-11): rows produced by the pre-M13-012 format
/// (v1, string enums, no schema version) must deserialize with identical legacy semantics;
/// v2 rows round-trip; unknown versions fail closed. Frozen versions are never rewritten.
/// </summary>
public sealed class AutomationDefinitionSerializerTests
{
    private const string LegacyCommentV1Json = """
        {
          "Trigger": { "Kind": "CommentCreated", "KeywordFilters": ["قیمت", "خرید"] },
          "Conditions": [ { "Field": "CommentText", "Operator": "Contains", "ExpectedValue": "قیمت" } ],
          "Actions": [ { "Kind": "SendDirectMessage", "MessageText": "سلام 👋" } ]
        }
        """;

    private const string LegacyEveryCommentV1Json = """
        {
          "Trigger": { "Kind": "CommentCreated", "KeywordFilters": [] },
          "Conditions": [],
          "Actions": [ { "Kind": "SendDirectMessage", "MessageText": "پاسخ خودکار" } ]
        }
        """;

    [Fact]
    public void LegacyV1NonEmptyKeywordsDeserializeAsKeywordSubstringMode()
    {
        var definition = AutomationDefinitionSerializer.Deserialize(LegacyCommentV1Json);

        Assert.Equal(1, definition.SchemaVersion);
        Assert.Equal(TriggerKind.CommentCreated, definition.Trigger.Kind);
        Assert.Equal(TextMatchMode.Keywords, definition.Trigger.TextMatch);
        Assert.False(definition.Trigger.WholeWord);
        Assert.Equal(SourceScope.AnySource, definition.Trigger.Source);
        Assert.Null(definition.Trigger.SourceMediaId);
        Assert.Equal(["قیمت", "خرید"], definition.Trigger.KeywordFilters);
        var action = Assert.Single(definition.Actions);
        Assert.Equal(ActionKind.SendDirectMessage, action.Kind);
        Assert.Null(action.Extras);
    }

    [Fact]
    public void LegacyV1EmptyKeywordsDeserializeAsEveryEvent()
    {
        var definition = AutomationDefinitionSerializer.Deserialize(LegacyEveryCommentV1Json);

        Assert.Equal(1, definition.SchemaVersion);
        Assert.Equal(TextMatchMode.EveryEvent, definition.Trigger.TextMatch);
        Assert.Empty(definition.Trigger.KeywordFilters);
    }

    [Fact]
    public void LegacyV1BehavesIdenticallyToPreM13012Evaluation()
    {
        var definition = AutomationDefinitionSerializer.Deserialize(LegacyCommentV1Json);

        // Pre-M13-012 semantics: ANY-of case-insensitive substring on the comment text.
        var evaluation = AutomationEvaluator.Evaluate(definition, Context("قیمت چنده؟"));
        Assert.True(evaluation.Matched);
        Assert.False(AutomationEvaluator.Evaluate(definition, Context("سلام")).Matched);
    }

    [Fact]
    public void V2RoundTripPreservesAllFields()
    {
        var definition = AutomationDefinition.Create(
            new AutomationTrigger(
                TriggerKind.CommentCreated,
                ["offer"],
                TextMatchMode.Keywords,
                WholeWord: true,
                SourceScope.SpecificSource,
                "17841400000000000_media_42"),
            [
                new AutomationAction(ActionKind.SendPublicReply, "public reply"),
                new AutomationAction(
                    ActionKind.ScheduleFollowUp,
                    "follow up",
                    new ActionExtras(Delay: TimeSpan.FromMinutes(30))),
                new AutomationAction(
                    ActionKind.StartRevealFlow,
                    "opening",
                    new ActionExtras(Reveal: new RevealActionContent(
                        "gate prompt", "دریافت", "reveal text", "https://example.com", "دنبال کردن"))),
                new AutomationAction(ActionKind.SendPublicReply, "second public"),
            ]);

        var json = AutomationDefinitionSerializer.Serialize(definition);
        var roundTripped = AutomationDefinitionSerializer.Deserialize(json);

        Assert.Equal(2, roundTripped.SchemaVersion);
        Assert.Equal(TriggerKind.CommentCreated, roundTripped.Trigger.Kind);
        Assert.Equal(["offer"], roundTripped.Trigger.KeywordFilters);
        Assert.Equal(TextMatchMode.Keywords, roundTripped.Trigger.TextMatch);
        Assert.True(roundTripped.Trigger.WholeWord);
        Assert.Equal(SourceScope.SpecificSource, roundTripped.Trigger.Source);
        Assert.Equal("17841400000000000_media_42", roundTripped.Trigger.SourceMediaId);
        Assert.Equal(4, roundTripped.Actions.Count);
        Assert.Equal("public reply", roundTripped.Actions[0].MessageText);
        Assert.Equal(TimeSpan.FromMinutes(30), roundTripped.Actions[1].Extras!.Delay);
        Assert.Equal("gate prompt", roundTripped.Actions[2].Extras!.Reveal!.GatePromptText);
        Assert.Equal("https://example.com", roundTripped.Actions[2].Extras!.Reveal!.FollowUrl);
        Assert.Equal("second public", roundTripped.Actions[3].MessageText);
    }

    [Fact]
    public void UnknownSchemaVersionFailsClosed()
    {
        const string unknown = """
            { "SchemaVersion": 99,
              "Trigger": { "Kind": "CommentCreated", "KeywordFilters": [], "TextMatch": "EveryEvent", "WholeWord": false, "Source": "AnySource" },
              "Conditions": [], "Actions": [ { "Kind": "SendDirectMessage", "MessageText": "x" } ] }
            """;

        Assert.Throws<JsonException>(() => AutomationDefinitionSerializer.Deserialize(unknown));
    }

    [Fact]
    public void NumericEnumsFromLegacyStorageAreAccepted()
    {
        // Old tooling/imports may store enums as numbers; the reader is tolerant.
        const string numeric = """
            { "Trigger": { "Kind": 1, "KeywordFilters": ["a"] },
              "Conditions": [ { "Field": 1, "Operator": 1, "ExpectedValue": "a" } ],
              "Actions": [ { "Kind": 1, "MessageText": "x" } ] }
            """;

        var definition = AutomationDefinitionSerializer.Deserialize(numeric);

        Assert.Equal(TriggerKind.CommentCreated, definition.Trigger.Kind);
        Assert.Equal(TextMatchMode.Keywords, definition.Trigger.TextMatch);
        Assert.Equal(ConditionField.CommentText, definition.Conditions[0].Field);
        Assert.Equal(ActionKind.SendDirectMessage, definition.Actions[0].Kind);
    }

    private static TriggerContext Context(string text) =>
        new("evt-1", TriggerKind.CommentCreated, "comment-1", "sender-1", text, new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.Zero));
}
