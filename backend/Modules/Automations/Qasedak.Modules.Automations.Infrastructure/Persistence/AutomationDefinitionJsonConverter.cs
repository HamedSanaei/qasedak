using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;

namespace Qasedak.Modules.Automations.Infrastructure.Persistence;

/// <summary>
/// Storage-format mapping for <see cref="AutomationDefinition"/> (M13-012 §10-11). No CLR
/// type names, no assembly-qualified names — the format is an explicit, versioned JSON
/// contract:
///
/// v1 (legacy rows written before M13-012): { Trigger: { Kind, KeywordFilters }, Conditions,
/// Actions: [ { Kind, MessageText } ] }. Deserialized with the historical semantics —
/// empty keyword filters ⇒ EveryEvent; non-empty filters ⇒ ANY-of case-insensitive
/// substring; AnySource; no whole-word; no extras.
///
/// v2 (current): the same envelope with an explicit "SchemaVersion": 2 and the full
/// trigger/action model (TextMatch, WholeWord, Source, SourceMediaId, Extras).
///
/// Unknown schema versions fail closed. New writes always carry SchemaVersion 2.
/// </summary>
public sealed class AutomationDefinitionJsonConverter : JsonConverter<AutomationDefinition>
{
    public override AutomationDefinition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        var schemaVersion = ReadIntProperty(root, "SchemaVersion") ?? 1;
        if (schemaVersion is not (1 or 2))
        {
            throw new JsonException($"Unsupported automation definition schema version '{schemaVersion}'.");
        }

        if (!root.TryGetProperty("Trigger", out var triggerElement))
        {
            throw new JsonException("Stored automation definition has no Trigger.");
        }

        var trigger = ReadTrigger(triggerElement, schemaVersion);
        var conditions = ReadConditions(root);
        var actions = ReadActions(root, schemaVersion);

        return new AutomationDefinition(trigger, conditions, actions, schemaVersion);
    }

    public override void Write(Utf8JsonWriter writer, AutomationDefinition value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteNumber("SchemaVersion", value.SchemaVersion == 1 ? 1 : 2);

        writer.WritePropertyName("Trigger");
        WriteTrigger(writer, value.Trigger);

        writer.WritePropertyName("Conditions");
        writer.WriteStartArray();
        foreach (var condition in value.Conditions)
        {
            writer.WriteStartObject();
            writer.WriteString("Field", condition.Field.ToString());
            writer.WriteString("Operator", condition.Operator.ToString());
            writer.WriteString("ExpectedValue", condition.ExpectedValue);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WritePropertyName("Actions");
        writer.WriteStartArray();
        foreach (var action in value.Actions)
        {
            writer.WriteStartObject();
            writer.WriteString("Kind", action.Kind.ToString());
            writer.WriteString("MessageText", action.MessageText);
            if (action.Extras is { } extras)
            {
                writer.WritePropertyName("Extras");
                writer.WriteStartObject();
                if (extras.Delay is { } delay)
                {
                    writer.WriteString("Delay", delay.ToString("c", CultureInfo.InvariantCulture));
                }

                if (extras.Reveal is { } reveal)
                {
                    writer.WritePropertyName("Reveal");
                    writer.WriteStartObject();
                    writer.WriteString("GatePromptText", reveal.GatePromptText);
                    writer.WriteString("PostbackButtonTitle", reveal.PostbackButtonTitle);
                    writer.WriteString("RevealText", reveal.RevealText);
                    if (reveal.FollowUrl is not null)
                    {
                        writer.WriteString("FollowUrl", reveal.FollowUrl);
                    }

                    if (reveal.FollowButtonTitle is not null)
                    {
                        writer.WriteString("FollowButtonTitle", reveal.FollowButtonTitle);
                    }

                    writer.WriteString("FollowGateMode", reveal.FollowGateMode.ToString());
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteEndObject();
    }

    private static AutomationTrigger ReadTrigger(JsonElement element, int schemaVersion)
    {
        var kind = ReadEnum<TriggerKind>(element, "Kind") ?? throw new JsonException("Stored trigger has no Kind.");
        var keywords = ReadStringList(element, "KeywordFilters");

        if (schemaVersion == 1)
        {
            // Historical semantics: the empty-array convention becomes explicit.
            return new AutomationTrigger(
                kind,
                keywords,
                keywords.Count > 0 ? TextMatchMode.Keywords : TextMatchMode.EveryEvent,
                WholeWord: false,
                SourceScope.AnySource,
                null);
        }

        return new AutomationTrigger(
            kind,
            keywords,
            ReadEnum<TextMatchMode>(element, "TextMatch") ?? TextMatchMode.EveryEvent,
            ReadBool(element, "WholeWord") ?? false,
            ReadEnum<SourceScope>(element, "Source") ?? SourceScope.AnySource,
            ReadNullableString(element, "SourceMediaId"));
    }

    private static List<AutomationCondition> ReadConditions(JsonElement root)
    {
        var conditions = new List<AutomationCondition>();
        if (!root.TryGetProperty("Conditions", out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return conditions;
        }

        foreach (var item in element.EnumerateArray())
        {
            var field = ReadEnum<ConditionField>(item, "Field") ?? throw new JsonException("Stored condition has no Field.");
            var op = ReadEnum<ConditionOperator>(item, "Operator") ?? throw new JsonException("Stored condition has no Operator.");
            conditions.Add(new AutomationCondition(field, op, ReadNullableString(item, "ExpectedValue") ?? string.Empty));
        }

        return conditions;
    }

    private static List<AutomationAction> ReadActions(JsonElement root, int schemaVersion)
    {
        if (!root.TryGetProperty("Actions", out var element) || element.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Stored automation definition has no Actions.");
        }

        var actions = new List<AutomationAction>();
        foreach (var item in element.EnumerateArray())
        {
            var kind = ReadEnum<ActionKind>(item, "Kind") ?? throw new JsonException("Stored action has no Kind.");
            var text = ReadNullableString(item, "MessageText") ?? string.Empty;

            if (schemaVersion == 1)
            {
                actions.Add(new AutomationAction(kind, text));
                continue;
            }

            var extras = item.TryGetProperty("Extras", out var extrasElement) && extrasElement.ValueKind == JsonValueKind.Object
                ? ReadExtras(extrasElement)
                : null;
            actions.Add(new AutomationAction(kind, text, extras));
        }

        return actions;
    }

    private static ActionExtras ReadExtras(JsonElement element)
    {
        TimeSpan? delay = null;
        if (element.TryGetProperty("Delay", out var delayElement) && delayElement.ValueKind == JsonValueKind.String)
        {
            if (!TimeSpan.TryParse(delayElement.GetString(), CultureInfo.InvariantCulture, out var parsed))
            {
                throw new JsonException("Stored action Delay is not a valid duration.");
            }

            delay = parsed;
        }

        RevealActionContent? reveal = null;
        if (element.TryGetProperty("Reveal", out var revealElement) && revealElement.ValueKind == JsonValueKind.Object)
        {
            reveal = new RevealActionContent(
                ReadNullableString(revealElement, "GatePromptText") ?? string.Empty,
                ReadNullableString(revealElement, "PostbackButtonTitle") ?? string.Empty,
                ReadNullableString(revealElement, "RevealText") ?? string.Empty,
                ReadNullableString(revealElement, "FollowUrl"),
                ReadNullableString(revealElement, "FollowButtonTitle"),
                ReadEnum<RevealFollowGateMode>(revealElement, "FollowGateMode") ?? RevealFollowGateMode.Disabled);
        }

        return new ActionExtras(delay, reveal);
    }

    private static void WriteTrigger(Utf8JsonWriter writer, AutomationTrigger trigger)
    {
        writer.WriteStartObject();
        writer.WriteString("Kind", trigger.Kind.ToString());
        writer.WritePropertyName("KeywordFilters");
        writer.WriteStartArray();
        foreach (var keyword in trigger.KeywordFilters)
        {
            writer.WriteStringValue(keyword);
        }

        writer.WriteEndArray();
        writer.WriteString("TextMatch", trigger.TextMatch.ToString());
        writer.WriteBoolean("WholeWord", trigger.WholeWord);
        writer.WriteString("Source", trigger.Source.ToString());
        if (trigger.SourceMediaId is not null)
        {
            writer.WriteString("SourceMediaId", trigger.SourceMediaId);
        }

        writer.WriteEndObject();
    }

    private static List<string> ReadStringList(JsonElement element, string property)
    {
        var list = new List<string>();
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                list.Add(item.GetString() ?? string.Empty);
            }
        }

        return list;
    }

    private static string? ReadNullableString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? ReadBool(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static int? ReadIntProperty(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => null,
        };
    }

    private static TEnum? ReadEnum<TEnum>(JsonElement element, string property) where TEnum : struct, Enum
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String when Enum.TryParse<TEnum>(value.GetString(), ignoreCase: true, out var parsed) => parsed,
            JsonValueKind.Number when value.TryGetInt32(out var number) && Enum.IsDefined(typeof(TEnum), number) => (TEnum)(object)number,
            _ => null,
        };
    }
}
