using System.Text.Json;
using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Automations.Domain;
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

    [Fact]
    public void HistoricalV1RowExceedingNewAuthoringBoundsStaysReadable()
    {
        // Frozen pre-M13-012 rows must remain readable even when their content exceeds
        // today's UTF-8 byte authoring bound (M13-012 correction §14): deserialization
        // materializes history — it never re-runs authoring validation.
        var overByteText = new string('گ', 1000); // 1000 chars = 2000 UTF-8 bytes
        var json = "{\"Trigger\":{\"Kind\":\"CommentCreated\",\"KeywordFilters\":[]}," +
                   "\"Conditions\":[],\"Actions\":[{\"Kind\":\"SendDirectMessage\",\"MessageText\":\"" +
                   overByteText + "\"}]}";

        var definition = AutomationDefinitionSerializer.Deserialize(json);

        Assert.Equal(1, definition.SchemaVersion);
        Assert.Equal(overByteText, definition.Actions[0].MessageText);
        // The same content is no longer authorable as new v2 — and that is exactly right.
        Assert.Throws<AutomationsDomainException>(() => AutomationDefinition.Create(
            AutomationTrigger.CommentCreated(), [new AutomationAction(ActionKind.SendDirectMessage, overByteText)]));
    }

    [Fact]
    public void HistoricalV2RowExceedingNewAuthoringBoundsStaysReadable()
    {
        // The deployed M13-012 API could have persisted v2 rows at the old limits
        // (21-40 char titles, 2001-2048 URLs, 641-1000 gate prompts, char-not-byte
        // plain text). Those rows are history: readable, never rewritten, and their
        // execution fails closed through the existing downstream local provider
        // validation (M13-012 correction §15). No destructive migration.
        var longGate = new string('g', 641);
        var longTitle = new string('p', 21);
        var overByteText = new string('گ', 1000); // 2000 UTF-8 bytes
        var longUrl = "https://example.com/" + new string('a', 1981); // 2001 chars
        var json = "{\"SchemaVersion\":2," +
                   "\"Trigger\":{\"Kind\":\"CommentCreated\",\"KeywordFilters\":[],\"TextMatch\":\"EveryEvent\",\"WholeWord\":false,\"Source\":\"AnySource\"}," +
                   "\"Conditions\":[]," +
                   "\"Actions\":[{\"Kind\":\"StartRevealFlow\",\"MessageText\":\"opening\",\"Extras\":{\"Reveal\":{" +
                   "\"GatePromptText\":\"" + longGate + "\",\"PostbackButtonTitle\":\"" + longTitle + "\"," +
                   "\"RevealText\":\"" + overByteText + "\",\"FollowUrl\":\"" + longUrl + "\"," +
                   "\"FollowButtonTitle\":\"follow\",\"FollowGateMode\":\"Disabled\"}}}]}";

        var definition = AutomationDefinitionSerializer.Deserialize(json);

        Assert.Equal(2, definition.SchemaVersion);
        var reveal = definition.Actions[0].Extras!.Reveal!;
        Assert.Equal(longGate, reveal.GatePromptText);
        Assert.Equal(longTitle, reveal.PostbackButtonTitle);
        Assert.Equal(overByteText, reveal.RevealText);
        Assert.Equal(longUrl, reveal.FollowUrl);

        // The corrected authoring boundary rejects the same content as NEW v2.
        Assert.Throws<AutomationsDomainException>(() => AutomationDefinition.Create(
            AutomationTrigger.CommentCreated(),
            [
                new AutomationAction(
                    ActionKind.StartRevealFlow,
                    "opening",
                    new ActionExtras(Reveal: new RevealActionContent(longGate, longTitle, overByteText, longUrl, "follow"))),
            ]));
    }

    private static TriggerContext Context(string text) =>
        new("evt-1", TriggerKind.CommentCreated, "comment-1", "sender-1", text, new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.Zero));
}
