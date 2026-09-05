using System.Text.Json;

namespace Qasedak.Modules.Instagram.Application.Webhooks;

/// <summary>
/// Translates canonical Meta webhook bodies into explicit integration events. Parsing is
/// an application concern; Domain never sees transport models. Unknown shapes are surfaced
/// as <see cref="UnrecognizedWebhookFragment"/> instead of being dropped silently, and
/// malformed JSON yields a single unrecognized fragment so the inbox can still be closed.
/// </summary>
public sealed class MetaPayloadNormalizer
{
    public static NormalizationOutcome Normalize(string eventId, string topic, string bodyJson)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bodyJson);
        }
        catch (JsonException)
        {
            return new NormalizationOutcome(
                [],
                [new UnrecognizedWebhookFragment(eventId, "malformed-json")]);
        }

        using (document)
        {
            var events = new List<IIntegrationEvent>();
            var unrecognized = new List<UnrecognizedWebhookFragment>();

            if (!document.RootElement.TryEnumerateArray("entry", out var entries))
            {
                return new NormalizationOutcome(events, unrecognized);
            }

            foreach (var entry in entries)
            {
                // entry.id is the professional account (IG_ID) routing identity.
                var providerAccountId = entry.TryGetProperty("id", out var entryId) ? entryId.GetString() : null;
                CollectMessaging(events, unrecognized, eventId, providerAccountId, entry);
                CollectChanges(events, unrecognized, eventId, providerAccountId, entry);
            }

            return new NormalizationOutcome(events, unrecognized);
        }
    }

    private static void CollectMessaging(List<IIntegrationEvent> events, List<UnrecognizedWebhookFragment> unrecognized, string eventId, string? providerAccountId, JsonElement entry)
    {
        if (!entry.TryEnumerateArray("messaging", out var messaging))
        {
            return;
        }

        foreach (var message in messaging)
        {
            if (!message.TryGetProperty("message", out var payload))
            {
                unrecognized.Add(new UnrecognizedWebhookFragment(eventId, "messaging-without-message"));
                continue;
            }

            var text = payload.TryGetProperty("text", out var textElement) ? textElement.GetString() : null;
            var isEcho = payload.TryGetProperty("is_echo", out var echoElement) && echoElement.ValueKind == JsonValueKind.True;
            if (isEcho)
            {
                // Echoes mirror our own outbound sends; not inbound conversation material.
                continue;
            }

            var senderId = message.TryGetProperty("sender", out var sender) && sender.TryGetProperty("id", out var senderIdElement)
                ? senderIdElement.GetString()
                : null;
            var providerMessageId = payload.TryGetProperty("mid", out var midElement) ? midElement.GetString() : null;
            var timestamp = ReadUnixSeconds(message, "timestamp") ?? DateTimeOffset.UtcNow;
            events.Add(new InstagramMessageReceived(
                eventId, providerAccountId, senderId ?? "unknown", text, timestamp, providerMessageId));
        }
    }

    private static void CollectChanges(List<IIntegrationEvent> events, List<UnrecognizedWebhookFragment> unrecognized, string eventId, string? providerAccountId, JsonElement entry)
    {
        if (!entry.TryEnumerateArray("changes", out var changes))
        {
            return;
        }

        foreach (var change in changes)
        {
            var field = change.TryGetProperty("field", out var fieldElement) ? fieldElement.GetString() : null;
            var value = change.TryGetProperty("value", out var valueElement) ? valueElement : default;

            switch (field)
            {
                case "comments":
                    events.Add(new InstagramCommentCreated(
                        eventId,
                        providerAccountId,
                        value.ValueKind == JsonValueKind.Object && value.TryGetProperty("id", out var commentId) ? commentId.GetString() ?? "unknown" : "unknown",
                        value.ValueKind == JsonValueKind.Object && value.TryGetProperty("from", out var from) && from.TryGetProperty("id", out var fromId) ? fromId.GetString() : null,
                        value.ValueKind == JsonValueKind.Object && value.TryGetProperty("text", out var commentText) ? commentText.GetString() : null,
                        value.ValueKind == JsonValueKind.Object && ReadUnixSeconds(value, "created_time") is { } created ? created : DateTimeOffset.UtcNow));
                    break;

                case "mentions":
                    events.Add(new InstagramMentionCreated(
                        eventId,
                        providerAccountId,
                        value.ValueKind == JsonValueKind.Object && value.TryGetProperty("comment_id", out var mentionCommentId) ? mentionCommentId.GetString() ?? "unknown" : "unknown",
                        DateTimeOffset.UtcNow));
                    break;

                default:
                    unrecognized.Add(new UnrecognizedWebhookFragment(eventId, $"field:{field ?? "none"}"));
                    break;
            }
        }
    }

    private static DateTimeOffset? ReadUnixSeconds(JsonElement element, string property) =>
        element.TryGetProperty(property, out var raw)
        && raw.TryGetInt64(out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
}

internal static class JsonElementExtensions
{
    /// <summary>Enumerate a named array property without ValueKind ceremony.</summary>
    public static bool TryEnumerateArray(this JsonElement element, string property, out JsonElement.ArrayEnumerator enumerator)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var arrayElement)
            && arrayElement.ValueKind == JsonValueKind.Array)
        {
            enumerator = arrayElement.EnumerateArray();
            return true;
        }

        enumerator = default;
        return false;
    }
}
