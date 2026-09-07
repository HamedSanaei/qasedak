using System.Text.Json;

namespace Qasedak.Modules.Instagram.Application.Webhooks;

/// <summary>
/// Translates canonical Meta webhook bodies into explicit integration events. Parsing is a
/// pure, side-effect-free application concern: no repository, token store, Graph call or
/// clock is touched here. Each webhook entry is normalized independently (multi-entry
/// fan-out never shares state), every fragment receives a deterministic identity derived
/// from the inbox event id plus entry/item position, and malformed siblings never poison
/// valid ones. Timestamps are provider milliseconds (current official shapes), with
/// entry.time as the documented fallback; no local wall-clock time is ever invented.
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
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("entry", out var entriesElement)
                || entriesElement.ValueKind != JsonValueKind.Array)
            {
                return new NormalizationOutcome([], []);
            }

            var entries = new List<EntryNormalization>();
            var entryIndex = 0;
            foreach (var entry in entriesElement.EnumerateArray())
            {
                // entry.id is the professional account (IG_ID) routing identity.
                var providerAccountId = ReadString(entry, "id");
                var entryEvents = new List<IIntegrationEvent>();
                var entryUnrecognized = new List<UnrecognizedWebhookFragment>();
                var entryIgnored = new List<IgnoredWebhookFragment>();

                CollectMessaging(entryEvents, entryUnrecognized, entryIgnored, eventId, providerAccountId, entry, entryIndex);
                CollectChanges(entryEvents, entryUnrecognized, entryIgnored, eventId, providerAccountId, entry, entryIndex);

                entries.Add(new EntryNormalization(entryIndex, providerAccountId, entryEvents, entryUnrecognized, entryIgnored));
                entryIndex++;
            }

            return new NormalizationOutcome(entries, []);
        }
    }

    /// <summary>
    /// Messaging dispatch by explicit fragment type (M13-008 fix): a messaging item without
    /// a "message" property is no longer treated as "messaging-without-message" — postbacks
    /// and read receipts have their own shapes. Known-but-unsupported fragments (edits,
    /// reactions, referrals) are observable non-triggering fragments, never inbound text.
    /// </summary>
    private static void CollectMessaging(
        List<IIntegrationEvent> events,
        List<UnrecognizedWebhookFragment> unrecognized,
        List<IgnoredWebhookFragment> ignored,
        string eventId,
        string? providerAccountId,
        JsonElement entry,
        int entryIndex)
    {
        if (!entry.TryEnumerateArray("messaging", out var messaging))
        {
            return;
        }

        var itemIndex = 0;
        foreach (var item in messaging)
        {
            CollectMessagingItem(events, unrecognized, ignored, eventId, providerAccountId, entry, entryIndex, itemIndex, item);
            itemIndex++;
        }
    }

    private static void CollectMessagingItem(
        List<IIntegrationEvent> events,
        List<UnrecognizedWebhookFragment> unrecognized,
        List<IgnoredWebhookFragment> ignored,
        string eventId,
        string? providerAccountId,
        JsonElement entry,
        int entryIndex,
        int itemIndex,
        JsonElement item)
    {
        var fragmentEventId = $"{eventId}:e{entryIndex}:m{itemIndex}";
        if (item.ValueKind != JsonValueKind.Object)
        {
            unrecognized.Add(new UnrecognizedWebhookFragment(fragmentEventId, "messaging-unknown-shape"));
            return;
        }

        var senderId = ReadStringFrom(item, "sender", "id");

        if (item.TryGetProperty("message", out var message))
        {
            if (message.ValueKind != JsonValueKind.Object)
            {
                unrecognized.Add(new UnrecognizedWebhookFragment(fragmentEventId, "message-malformed"));
                return;
            }

            // Inbound filters: these must never become automation-triggering text events.
            // Deleted first: a stale "text" coexisting in a deleted payload must not flow.
            if (IsTrue(message, "is_deleted"))
            {
                ignored.Add(new IgnoredWebhookFragment(fragmentEventId, "message-deleted"));
                return;
            }

            if (IsTrue(message, "is_echo"))
            {
                // Echoes mirror our own outbound sends; never customer inbound material.
                ignored.Add(new IgnoredWebhookFragment(fragmentEventId, "message-echo"));
                return;
            }

            if (IsTrue(message, "is_self"))
            {
                // Self messages (webhook previews/testing) must not trigger automations.
                ignored.Add(new IgnoredWebhookFragment(fragmentEventId, "message-self"));
                return;
            }

            if (IsTrue(message, "is_unsupported"))
            {
                ignored.Add(new IgnoredWebhookFragment(fragmentEventId, "message-unsupported"));
                return;
            }

            if (senderId is null)
            {
                ignored.Add(new IgnoredWebhookFragment(fragmentEventId, "message-sender-missing"));
                return;
            }

            var text = ReadString(message, "text");
            if (text is not null && text.Length > WebhookNormalizationPolicy.MaxInboundTextLength)
            {
                ignored.Add(new IgnoredWebhookFragment(fragmentEventId, "message-oversized"));
                return;
            }

            var quickReplyPayload = ReadStringFrom(message, "quick_reply", "payload");
            if (quickReplyPayload is not null && quickReplyPayload.Length > WebhookNormalizationPolicy.MaxQuickReplyPayloadLength)
            {
                // Quick-reply payload is optional metadata; an oversized one is dropped
                // without mutating the text (automation semantics unchanged).
                quickReplyPayload = null;
            }

            // Official inline-reply correlation (verified 2026-09-07): "reply_to.mid" is
            // the provider message id the user was replying to — the smallest field needed
            // for M13-011 continuation correlation. Bounded like every mid; an oversized
            // one is dropped (never truncated) without blocking the message itself.
            var repliedToMid = ReadStringFrom(message, "reply_to", "mid");
            if (repliedToMid is not null && repliedToMid.Length > WebhookNormalizationPolicy.MaxProviderMessageIdLength)
            {
                repliedToMid = null;
            }

            if (text is null && quickReplyPayload is null)
            {
                // Attachment-only / story-reply-only / ad-click-only: real content exists but
                // is unsupported for current text automation semantics — observable, safe.
                ignored.Add(new IgnoredWebhookFragment(fragmentEventId, "message-attachment-only"));
                return;
            }

            var timestamp = ReadProviderTimeMs(entry, item);
            if (timestamp is null)
            {
                ignored.Add(new IgnoredWebhookFragment(fragmentEventId, "message-timestamp-missing"));
                return;
            }

            events.Add(new InstagramMessageReceived(
                fragmentEventId, null, null, providerAccountId, senderId, text, timestamp.Value,
                ReadString(message, "mid"), repliedToMid, quickReplyPayload));
            return;
        }

        if (item.TryGetProperty("postback", out var postback))
        {
            if (postback.ValueKind != JsonValueKind.Object)
            {
                unrecognized.Add(new UnrecognizedWebhookFragment(fragmentEventId, "postback-malformed"));
                return;
            }

            if (senderId is null)
            {
                ignored.Add(new IgnoredWebhookFragment(fragmentEventId, "postback-sender-missing"));
                return;
            }

            var mid = ReadString(postback, "mid");
            var title = ReadString(postback, "title");
            var payload = ReadString(postback, "payload");
            if (mid is null || title is null || payload is null)
            {
                unrecognized.Add(new UnrecognizedWebhookFragment(fragmentEventId, "postback-incomplete"));
                return;
            }

            if (title.Length > WebhookNormalizationPolicy.MaxPostbackTitleLength
                || payload.Length > WebhookNormalizationPolicy.MaxPostbackPayloadLength)
            {
                // Never truncate: truncation would change automation semantics.
                unrecognized.Add(new UnrecognizedWebhookFragment(fragmentEventId, "postback-oversized"));
                return;
            }

            var timestamp = ReadProviderTimeMs(entry, item);
            if (timestamp is null)
            {
                ignored.Add(new IgnoredWebhookFragment(fragmentEventId, "postback-timestamp-missing"));
                return;
            }

            events.Add(new InstagramPostbackReceived(
                fragmentEventId, null, null, providerAccountId, senderId, mid, title, payload, timestamp.Value));
            return;
        }

        if (item.TryGetProperty("read", out var read))
        {
            if (read.ValueKind != JsonValueKind.Object)
            {
                unrecognized.Add(new UnrecognizedWebhookFragment(fragmentEventId, "read-malformed"));
                return;
            }

            if (senderId is null)
            {
                ignored.Add(new IgnoredWebhookFragment(fragmentEventId, "read-sender-missing"));
                return;
            }

            // Current official shape is read:{mid} — the message id read. A "watermark"
            // property (legacy Messenger shape) is deliberately never read; read receipts
            // carry no watermark semantics in the current Instagram contract (ADR-010).
            var mid = ReadString(read, "mid");
            if (mid is null)
            {
                unrecognized.Add(new UnrecognizedWebhookFragment(fragmentEventId, "read-incomplete"));
                return;
            }

            var timestamp = ReadProviderTimeMs(entry, item);
            if (timestamp is null)
            {
                ignored.Add(new IgnoredWebhookFragment(fragmentEventId, "read-timestamp-missing"));
                return;
            }

            events.Add(new InstagramMessageRead(
                fragmentEventId, null, null, providerAccountId, senderId, mid, timestamp.Value));
            return;
        }

        // Known-but-not-supported messaging fragments: observable, never inbound text.
        if (item.TryGetProperty("message_edit", out _))
        {
            ignored.Add(new IgnoredWebhookFragment(fragmentEventId, "message-edit"));
            return;
        }

        if (item.TryGetProperty("reaction", out _))
        {
            ignored.Add(new IgnoredWebhookFragment(fragmentEventId, "message-reaction"));
            return;
        }

        if (item.TryGetProperty("referral", out _))
        {
            ignored.Add(new IgnoredWebhookFragment(fragmentEventId, "messaging-referral"));
            return;
        }

        unrecognized.Add(new UnrecognizedWebhookFragment(fragmentEventId, "messaging-unknown-shape"));
    }

    private static void CollectChanges(
        List<IIntegrationEvent> events,
        List<UnrecognizedWebhookFragment> unrecognized,
        List<IgnoredWebhookFragment> ignored,
        string eventId,
        string? providerAccountId,
        JsonElement entry,
        int entryIndex)
    {
        if (!entry.TryEnumerateArray("changes", out var changes))
        {
            return;
        }

        var changeIndex = 0;
        foreach (var change in changes)
        {
            CollectChange(events, unrecognized, ignored, eventId, providerAccountId, entry, entryIndex, changeIndex, change);
            changeIndex++;
        }
    }

    private static void CollectChange(
        List<IIntegrationEvent> events,
        List<UnrecognizedWebhookFragment> unrecognized,
        List<IgnoredWebhookFragment> ignored,
        string eventId,
        string? providerAccountId,
        JsonElement entry,
        int entryIndex,
        int changeIndex,
        JsonElement change)
    {
        var fragmentEventId = $"{eventId}:e{entryIndex}:c{changeIndex}";
        if (change.ValueKind != JsonValueKind.Object)
        {
            unrecognized.Add(new UnrecognizedWebhookFragment(fragmentEventId, "malformed-fragment"));
            return;
        }

        var field = ReadString(change, "field");
        var value = change.TryGetProperty("value", out var valueElement) ? valueElement : default;

        switch (field)
        {
            case "comments" or "live_comments":
                // Current official shape: value:{id, from:{id,username}, text,
                // media:{id, media_product_type[, original_media_id for ad posts]}}.
                if (value.ValueKind != JsonValueKind.Object)
                {
                    unrecognized.Add(new UnrecognizedWebhookFragment(fragmentEventId, $"field:{field}-malformed"));
                    return;
                }

                var commentId = ReadString(value, "id");
                if (commentId is null)
                {
                    ignored.Add(new IgnoredWebhookFragment(fragmentEventId, "comment-id-missing"));
                    return;
                }

                var text = ReadString(value, "text");
                if (text is not null && text.Length > WebhookNormalizationPolicy.MaxCommentTextLength)
                {
                    ignored.Add(new IgnoredWebhookFragment(fragmentEventId, "comment-oversized"));
                    return;
                }

                var fromId = ReadStringFrom(value, "from", "id");
                var username = ReadStringFrom(value, "from", "username");
                if (username is not null && username.Length > WebhookNormalizationPolicy.MaxCommenterUsernameLength)
                {
                    // Display metadata only; oversized usernames are dropped, never identity.
                    username = null;
                }

                // The media id is preserved as an opaque provider id; it is never resolved,
                // never checked against the media catalog, and never a URL.
                var mediaId = ReadStringFrom(value, "media", "id");
                // Ad/boosted comments may carry the original media id separately; it is
                // preserved apart from media.id (never overwrites it) per the current
                // official comment shapes (Business Login example has no ad fields; the
                // FB-Login shape documents media.original_media_id for ad posts).
                var originalMediaId = ReadStringFrom(value, "media", "original_media_id");

                var commentTimestamp = ReadEntryTimeMs(entry);
                if (commentTimestamp is null)
                {
                    ignored.Add(new IgnoredWebhookFragment(fragmentEventId, "comment-timestamp-missing"));
                    return;
                }

                // M13-009: the Private Reply policy distinguishes Live comments (live_comments
                // field or media_product_type == "LIVE") — Live replies are only valid during
                // the broadcast and must never use the 7-day comment rule.
                var isLive = string.Equals(field, "live_comments", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ReadStringFrom(value, "media", "media_product_type"), "LIVE", StringComparison.OrdinalIgnoreCase);

                events.Add(new InstagramCommentCreated(
                    fragmentEventId, null, null, providerAccountId, commentId, fromId, username,
                    text, mediaId, originalMediaId, commentTimestamp.Value, isLive));
                return;

            case "mentions":
                // @mentions arrive inside comments on the Instagram-Login path; this legacy
                // field exists on the retained FB-Login path only.
                if (value.ValueKind != JsonValueKind.Object)
                {
                    unrecognized.Add(new UnrecognizedWebhookFragment(fragmentEventId, "field:mentions-malformed"));
                    return;
                }

                var mentionCommentId = ReadString(value, "comment_id");
                if (mentionCommentId is null)
                {
                    unrecognized.Add(new UnrecognizedWebhookFragment(fragmentEventId, "field:mentions-malformed"));
                    return;
                }

                var mentionTimestamp = ReadEntryTimeMs(entry);
                if (mentionTimestamp is null)
                {
                    ignored.Add(new IgnoredWebhookFragment(fragmentEventId, "mention-timestamp-missing"));
                    return;
                }

                events.Add(new InstagramMentionCreated(
                    fragmentEventId, null, null, providerAccountId, mentionCommentId, mentionTimestamp.Value));
                return;

            default:
                unrecognized.Add(new UnrecognizedWebhookFragment(fragmentEventId, $"field:{field ?? "none"}"));
                return;
        }
    }

    /// <summary>
    /// Provider event time: the fragment's own "timestamp" (messaging events, milliseconds
    /// per the current official examples) with the entry's "time" (notification-send time,
    /// also milliseconds) as the documented fallback. Returns null when neither is a valid
    /// provider time — callers then surface an ignored fragment; UtcNow is never used.
    /// </summary>
    private static DateTimeOffset? ReadProviderTimeMs(JsonElement entry, JsonElement fragment) =>
        ReadUnixMilliseconds(fragment, "timestamp") ?? ReadUnixMilliseconds(entry, "time");

    /// <summary>Comment events have no fragment timestamp in the current official shape; entry.time is the only provider time.</summary>
    private static DateTimeOffset? ReadEntryTimeMs(JsonElement entry) => ReadUnixMilliseconds(entry, "time");

    private static DateTimeOffset? ReadUnixMilliseconds(JsonElement element, string property)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var raw)
            && raw.ValueKind == JsonValueKind.Number
            && raw.TryGetInt64(out var milliseconds)
            && milliseconds >= WebhookNormalizationPolicy.MinValidTimestampMilliseconds
            && milliseconds <= WebhookNormalizationPolicy.MaxValidTimestampMilliseconds)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        }

        return null;
    }

    private static string? ReadString(JsonElement element, string property)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    private static string? ReadStringFrom(JsonElement element, string container, string property)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(container, out var inner)
            && inner.ValueKind == JsonValueKind.Object
            && inner.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    private static bool IsTrue(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.True;
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
