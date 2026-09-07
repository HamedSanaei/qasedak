using Qasedak.Modules.Instagram.Application.Webhooks;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests;

/// <summary>
/// Normalization fixtures (M13-008): the current official Meta shapes — comments /
/// live_comments changes, messaging message/postback/read fragments — map to explicit
/// transport-free integration events; echoes, self messages, deletions, unsupported and
/// attachment-only fragments are observable non-triggering fragments; unknown and
/// malformed shapes surface as unrecognized fragments; timestamps are provider
/// milliseconds with a deterministic entry.time fallback and never a local wall clock.
/// </summary>
public sealed class MetaPayloadNormalizerTests
{
    private const string AccountId = "17841408147298714";

    private static List<IIntegrationEvent> EventsOf(NormalizationOutcome outcome) =>
        outcome.Entries.SelectMany(e => e.Events).ToList();

    private static List<UnrecognizedWebhookFragment> UnrecognizedOf(NormalizationOutcome outcome) =>
        outcome.TopLevelUnrecognized.Concat(outcome.Entries.SelectMany(e => e.Unrecognized)).ToList();

    private static List<IgnoredWebhookFragment> IgnoredOf(NormalizationOutcome outcome) =>
        outcome.Entries.SelectMany(e => e.Ignored).ToList();

    private static string MessagingBody(string payload) =>
        "{\"object\":\"instagram\",\"entry\":[{\"id\":\"" + AccountId + "\",\"time\":1502905976963,\"messaging\":[" + payload + "]}]}";

    private static string MessagingItem(string payload) =>
        "{\"sender\":{\"id\":\"user-42\"},\"recipient\":{\"id\":\"" + AccountId + "\"},\"timestamp\":1502905976377," + payload + "}";

    // ------------------------------------------------------------------ messages

    [Fact]
    public void TextMessageMapsToMessageReceivedWithMillisecondTimestamp()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-1", "instagram",
            MessagingBody(MessagingItem("\"message\":{\"mid\":\"m-1\",\"text\":\"hello there\"}")));

        var message = Assert.IsType<InstagramMessageReceived>(Assert.Single(EventsOf(outcome)));
        Assert.Equal("evt-1:e0:m0", message.EventId);
        Assert.Equal(AccountId, message.ProviderAccountId);
        Assert.Equal("user-42", message.SenderId);
        Assert.Equal("hello there", message.Text);
        Assert.Equal("m-1", message.ProviderMessageId);
        Assert.Null(message.QuickReplyPayload);
        // Official-scale milliseconds, not seconds: 1502905976377 ms → the correct instant.
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1502905976377), message.SentAtUtc);
        Assert.Empty(UnrecognizedOf(outcome));
        Assert.Empty(IgnoredOf(outcome));
    }

    [Fact]
    public void EchoMessageIsObservableIgnoredFragmentNeverAnEvent()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-2", "instagram",
            MessagingBody(MessagingItem("\"message\":{\"is_echo\":true,\"mid\":\"m-2\",\"text\":\"we said this\"}")));

        Assert.Empty(EventsOf(outcome));
        Assert.Equal("message-echo", Assert.Single(IgnoredOf(outcome)).Reason);
    }

    [Fact]
    public void SelfMessageIsObservableIgnoredFragmentNeverAnEvent()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-2b", "instagram",
            MessagingBody(MessagingItem("\"message\":{\"is_self\":true,\"mid\":\"m-2b\",\"text\":\"preview\"}")));

        Assert.Empty(EventsOf(outcome));
        Assert.Equal("message-self", Assert.Single(IgnoredOf(outcome)).Reason);
    }

    [Fact]
    public void DeletedMessageIsIgnoredEvenWhenStaleTextCoexists()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-2c", "instagram",
            MessagingBody(MessagingItem("\"message\":{\"is_deleted\":true,\"mid\":\"m-2c\",\"text\":\"stale text\"}")));

        Assert.Empty(EventsOf(outcome));
        Assert.Equal("message-deleted", Assert.Single(IgnoredOf(outcome)).Reason);
    }

    [Fact]
    public void UnsupportedMessageIsObservableIgnoredFragment()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-2d", "instagram",
            MessagingBody(MessagingItem("\"message\":{\"is_unsupported\":true,\"mid\":\"m-2d\"}")));

        Assert.Empty(EventsOf(outcome));
        Assert.Equal("message-unsupported", Assert.Single(IgnoredOf(outcome)).Reason);
    }

    [Fact]
    public void AttachmentOnlyMessageIsNonTriggeringIgnoredFragment()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-2e", "instagram",
            MessagingBody(MessagingItem("\"message\":{\"mid\":\"m-2e\",\"attachments\":[{\"type\":\"image\",\"payload\":{\"url\":\"https://cdn.example/x\"}}]}")));

        Assert.Empty(EventsOf(outcome));
        Assert.Equal("message-attachment-only", Assert.Single(IgnoredOf(outcome)).Reason);
    }

    [Fact]
    public void InlineReplyCarriesReplyToMidCorrelation()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-2r", "instagram",
            MessagingBody(MessagingItem("\"message\":{\"mid\":\"m-2r\",\"text\":\"yes\",\"reply_to\":{\"mid\":\"mid-opening-1\"}}")));

        var message = Assert.IsType<InstagramMessageReceived>(Assert.Single(EventsOf(outcome)));
        Assert.Equal("m-2r", message.ProviderMessageId);
        // The official reply_to.mid is the message the user was replying to — never the sender's own mid.
        Assert.Equal("mid-opening-1", message.RepliedToProviderMessageId);
        Assert.Null(message.QuickReplyPayload);
    }

    [Fact]
    public void PlainMessageHasNoReplyToCorrelation()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-2s", "instagram",
            MessagingBody(MessagingItem("\"message\":{\"mid\":\"m-2s\",\"text\":\"first contact\"}")));

        var message = Assert.IsType<InstagramMessageReceived>(Assert.Single(EventsOf(outcome)));
        Assert.Null(message.RepliedToProviderMessageId);
    }

    [Fact]
    public void QuickReplyTapBecomesEventWithBoundedPayload()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-2f", "instagram",
            MessagingBody(MessagingItem("\"message\":{\"mid\":\"m-2f\",\"text\":\"I want more info\",\"quick_reply\":{\"payload\":\"MORE_INFO\"}}")));

        var message = Assert.IsType<InstagramMessageReceived>(Assert.Single(EventsOf(outcome)));
        Assert.Equal("MORE_INFO", message.QuickReplyPayload);
        Assert.Equal("I want more info", message.Text);
    }

    [Fact]
    public void MessageWithoutSenderIdIsObservableIgnoredFragment()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-2g", "instagram",
            """{"entry":[{"id":"17841408147298714","time":1502905976963,"messaging":[{"recipient":{"id":"17841408147298714"},"timestamp":1502905976377,"message":{"mid":"m-2g","text":"anon"}}]}]}""");

        Assert.Empty(EventsOf(outcome));
        Assert.Equal("message-sender-missing", Assert.Single(IgnoredOf(outcome)).Reason);
    }

    [Fact]
    public void OversizedMessageTextIsNonTriggeringFragmentNotTruncated()
    {
        var hugeText = new string('x', WebhookNormalizationPolicy.MaxInboundTextLength + 1);
        var outcome = MetaPayloadNormalizer.Normalize("evt-2h", "instagram",
            MessagingBody(MessagingItem("\"message\":{\"mid\":\"m-2h\",\"text\":\"" + hugeText + "\"}")));

        Assert.Empty(EventsOf(outcome));
        Assert.Equal("message-oversized", Assert.Single(IgnoredOf(outcome)).Reason);
    }

    // ------------------------------------------------------------------ postback / read

    [Fact]
    public void PostbackBecomesPostbackReceivedWithMidTitlePayload()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-3", "instagram",
            MessagingBody(MessagingItem("\"postback\":{\"mid\":\"m-pb-1\",\"title\":\"Get Started\",\"payload\":\"GET_STARTED\"}")));

        var postback = Assert.IsType<InstagramPostbackReceived>(Assert.Single(EventsOf(outcome)));
        Assert.Equal("evt-3:e0:m0", postback.EventId);
        Assert.Equal(AccountId, postback.ProviderAccountId);
        Assert.Equal("user-42", postback.SenderId);
        Assert.Equal("m-pb-1", postback.ProviderMessageId);
        Assert.Equal("Get Started", postback.Title);
        Assert.Equal("GET_STARTED", postback.Payload);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1502905976377), postback.OccurredAtUtc);
        Assert.Empty(IgnoredOf(outcome));
    }

    [Fact]
    public void OversizedPostbackIsUnrecognizedNeverTruncated()
    {
        var hugePayload = new string('p', WebhookNormalizationPolicy.MaxPostbackPayloadLength + 1);
        var outcome = MetaPayloadNormalizer.Normalize("evt-3b", "instagram",
            MessagingBody(MessagingItem("\"postback\":{\"mid\":\"m-pb-2\",\"title\":\"t\",\"payload\":\"" + hugePayload + "\"}")));

        Assert.Empty(EventsOf(outcome));
        Assert.Equal("postback-oversized", Assert.Single(UnrecognizedOf(outcome)).Kind);
    }

    [Fact]
    public void ReadBecomesMessageReadWithMidAndIgnoresAnyWatermark()
    {
        // The current official shape is read:{mid}; a legacy watermark property may
        // coexist in provider samples but carries no semantics here and is never read.
        var outcome = MetaPayloadNormalizer.Normalize("evt-4", "instagram",
            MessagingBody(MessagingItem("\"read\":{\"mid\":\"m-seen-1\",\"watermark\":1502905976377}")));

        var read = Assert.IsType<InstagramMessageRead>(Assert.Single(EventsOf(outcome)));
        Assert.Equal("evt-4:e0:m0", read.EventId);
        Assert.Equal(AccountId, read.ProviderAccountId);
        Assert.Equal("user-42", read.SenderId);
        Assert.Equal("m-seen-1", read.ProviderMessageId);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1502905976377), read.OccurredAtUtc);
        Assert.Empty(IgnoredOf(outcome));
    }

    // ------------------------------------------------------------------ comments

    [Fact]
    public void CommentCarriesMediaUsernameAndProviderTimestamp()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-5", "instagram",
            "{\"object\":\"instagram\",\"entry\":[{\"id\":\"" + AccountId + "\",\"time\":1502905976963,\"changes\":[{\"field\":\"comments\",\"value\":{\"id\":\"comment-9\",\"from\":{\"id\":\"commenter-42\",\"username\":\"curious_customer\"},\"text\":\"nice shot\",\"media\":{\"id\":\"media-77\",\"media_product_type\":\"FEED\"}}}]}]}");

        var comment = Assert.IsType<InstagramCommentCreated>(Assert.Single(EventsOf(outcome)));
        Assert.Equal("evt-5:e0:c0", comment.EventId);
        Assert.Equal(AccountId, comment.ProviderAccountId);
        Assert.Equal("comment-9", comment.CommentId);
        Assert.Equal("commenter-42", comment.FromId);
        Assert.Equal("curious_customer", comment.CommenterUsername);
        Assert.Equal("nice shot", comment.Text);
        Assert.Equal("media-77", comment.MediaId);
        Assert.Null(comment.OriginalMediaId);
        // Comment payloads carry no fragment timestamp in the current official shape:
        // entry.time (milliseconds) is the provider time.
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1502905976963), comment.CreatedAtUtc);
        Assert.Empty(IgnoredOf(outcome));
    }

    [Fact]
    public void CommentWithoutFromKeepsNullFromIdAndUsername()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-5b", "instagram",
            "{\"entry\":[{\"id\":\"" + AccountId + "\",\"time\":1502905976963,\"changes\":[{\"field\":\"comments\",\"value\":{\"id\":\"comment-10\",\"text\":\"anon\"}}]}]}");

        var comment = Assert.IsType<InstagramCommentCreated>(Assert.Single(EventsOf(outcome)));
        Assert.Null(comment.FromId);
        Assert.Null(comment.CommenterUsername);
        Assert.Null(comment.MediaId);
        Assert.Equal("anon", comment.Text);
    }

    [Fact]
    public void LiveFieldCommentIsMarkedLive()
    {
        // M13-009: the live_comments field identifies Live comments; the Private Reply
        // policy must never apply the 7-day rule to them.
        var outcome = MetaPayloadNormalizer.Normalize("evt-5d", "instagram",
            "{\"entry\":[{\"id\":\"" + AccountId + "\",\"time\":1502905976963,\"changes\":[{\"field\":\"live_comments\",\"value\":{\"id\":\"comment-live-1\",\"text\":\"watching\"}}]}]}");

        var comment = Assert.IsType<InstagramCommentCreated>(Assert.Single(EventsOf(outcome)));
        Assert.True(comment.IsLiveComment);
    }

    [Fact]
    public void CommentsFieldWithLiveProductTypeIsMarkedLive()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-5e", "instagram",
            "{\"entry\":[{\"id\":\"" + AccountId + "\",\"time\":1502905976963,\"changes\":[{\"field\":\"comments\",\"value\":{\"id\":\"comment-live-2\",\"text\":\"hi\",\"media\":{\"id\":\"media-live\",\"media_product_type\":\"LIVE\"}}}]}]}");

        var comment = Assert.IsType<InstagramCommentCreated>(Assert.Single(EventsOf(outcome)));
        Assert.True(comment.IsLiveComment);
    }

    [Fact]
    public void FeedCommentIsNotMarkedLive()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-5f", "instagram",
            "{\"entry\":[{\"id\":\"" + AccountId + "\",\"time\":1502905976963,\"changes\":[{\"field\":\"comments\",\"value\":{\"id\":\"comment-11\",\"text\":\"hello\",\"media\":{\"id\":\"media-1\",\"media_product_type\":\"REELS\"}}}]}]}");

        var comment = Assert.IsType<InstagramCommentCreated>(Assert.Single(EventsOf(outcome)));
        Assert.False(comment.IsLiveComment);
    }

    [Fact]
    public void AdCommentPreservesOriginalMediaIdSeparatelyFromMediaId()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-5c", "instagram",
            "{\"entry\":[{\"id\":\"" + AccountId + "\",\"time\":1502905976963,\"changes\":[{\"field\":\"comments\",\"value\":{\"id\":\"comment-ad-1\",\"text\":\"ad comment\",\"media\":{\"id\":\"ad-media-1\",\"original_media_id\":\"orig-media-9\",\"media_product_type\":\"FEED\"}}}]}]}");

        var comment = Assert.IsType<InstagramCommentCreated>(Assert.Single(EventsOf(outcome)));
        Assert.Equal("ad-media-1", comment.MediaId);
        Assert.Equal("orig-media-9", comment.OriginalMediaId);
    }

    [Fact]
    public void LiveCommentsFieldNormalizesLikeComments()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-5d", "instagram",
            "{\"entry\":[{\"id\":\"" + AccountId + "\",\"time\":1502905976963,\"changes\":[{\"field\":\"live_comments\",\"value\":{\"id\":\"comment-live-1\",\"text\":\"live!\"}}]}]}");

        var comment = Assert.IsType<InstagramCommentCreated>(Assert.Single(EventsOf(outcome)));
        Assert.Equal("comment-live-1", comment.CommentId);
    }

    [Fact]
    public void CommentWithoutIdIsObservableIgnoredFragment()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-5e", "instagram",
            """{"entry":[{"id":"17841408147298714","time":1502905976963,"changes":[{"field":"comments","value":{"text":"no id"}}]}]}""");

        Assert.Empty(EventsOf(outcome));
        Assert.Equal("comment-id-missing", Assert.Single(IgnoredOf(outcome)).Reason);
    }

    // ------------------------------------------------------------------ timestamps

    [Fact]
    public void MissingFragmentTimestampFallsBackToEntryTime()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-6", "instagram",
            MessagingBody(MessagingItem("\"message\":{\"mid\":\"m-6\",\"text\":\"no ts\"}").Replace("\"timestamp\":1502905976377,", "")));

        var message = Assert.IsType<InstagramMessageReceived>(Assert.Single(EventsOf(outcome)));
        // entry.time is the documented fallback (notification-send time, milliseconds).
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1502905976963), message.SentAtUtc);
    }

    [Fact]
    public void SecondsScaleTimestampIsNotMisreadAsMilliseconds()
    {
        // 1771900000 as "seconds" is a realistic older fixture; as milliseconds it would be
        // 1970-01-21. The window check rejects it and entry.time supplies the real time.
        var outcome = MetaPayloadNormalizer.Normalize("evt-6b", "instagram",
            """{"entry":[{"id":"17841408147298714","time":1502905976963,"messaging":[{"sender":{"id":"u1"},"timestamp":1771900000,"message":{"mid":"m-6b","text":"hi"}}]}]}""");

        var message = Assert.IsType<InstagramMessageReceived>(Assert.Single(EventsOf(outcome)));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1502905976963), message.SentAtUtc);
    }

    [Fact]
    public void InvalidTimestampTypeIsHandledWithoutThrowing()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-6c", "instagram",
            MessagingBody(MessagingItem("\"message\":{\"mid\":\"m-6c\",\"text\":\"str ts\"}").Replace("\"timestamp\":1502905976377", "\"timestamp\":\"1502905976377\"")));

        var message = Assert.IsType<InstagramMessageReceived>(Assert.Single(EventsOf(outcome)));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1502905976963), message.SentAtUtc);
    }

    [Fact]
    public void MissingAllProviderTimesYieldsIgnoredFragmentNeverUtcNow()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-6d", "instagram",
            """{"entry":[{"id":"17841408147298714","messaging":[{"sender":{"id":"u1"},"message":{"mid":"m-6d","text":"no times"}}]}]}""");

        Assert.Empty(EventsOf(outcome));
        Assert.Equal("message-timestamp-missing", Assert.Single(IgnoredOf(outcome)).Reason);
    }

    [Fact]
    public void CommentWithoutProviderTimeYieldsIgnoredFragment()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-6e", "instagram",
            """{"entry":[{"id":"17841408147298714","changes":[{"field":"comments","value":{"id":"c-1","text":"x"}}]}]}""");

        Assert.Empty(EventsOf(outcome));
        Assert.Equal("comment-timestamp-missing", Assert.Single(IgnoredOf(outcome)).Reason);
    }

    // ------------------------------------------------------------------ unknown / malformed

    [Fact]
    public void UnknownMessagingShapeIsUnrecognized()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-7", "instagram",
            MessagingBody(MessagingItem("\"somethingNew\":{\"future\":true}")));

        Assert.Empty(EventsOf(outcome));
        Assert.Equal("messaging-unknown-shape", Assert.Single(UnrecognizedOf(outcome)).Kind);
    }

    [Fact]
    public void MessageEditIsObservableIgnoredFragment()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-7b", "instagram",
            MessagingBody(MessagingItem("\"message_edit\":{\"mid\":\"m-1\",\"text\":\"edited\",\"num_edit\":2}")));

        Assert.Empty(EventsOf(outcome));
        Assert.Equal("message-edit", Assert.Single(IgnoredOf(outcome)).Reason);
    }

    [Fact]
    public void ReactionIsObservableIgnoredFragment()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-7c", "instagram",
            MessagingBody(MessagingItem("\"reaction\":{\"mid\":\"m-1\",\"action\":\"react\",\"reaction\":\"love\"}")));

        Assert.Empty(EventsOf(outcome));
        Assert.Equal("message-reaction", Assert.Single(IgnoredOf(outcome)).Reason);
    }

    [Fact]
    public void MessagingReferralIsObservableIgnoredFragment()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-7d", "instagram",
            MessagingBody(MessagingItem("\"referral\":{\"ref\":\"x\",\"source\":\"ig.me\",\"type\":\"OPEN_THREAD\"}")));

        Assert.Empty(EventsOf(outcome));
        Assert.Equal("messaging-referral", Assert.Single(IgnoredOf(outcome)).Reason);
    }

    [Fact]
    public void UnknownChangeFieldIsUnrecognized()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-8", "instagram",
            """{"entry":[{"id":"x","changes":[{"field":"story_insights","value":{"metric":1}}]}]}""");

        Assert.Empty(EventsOf(outcome));
        Assert.Equal("field:story_insights", Assert.Single(UnrecognizedOf(outcome)).Kind);
    }

    [Fact]
    public void MalformedJsonYieldsSingleFragmentWithoutThrowing()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-9", "instagram", "{\"entry\":[ broken");

        Assert.Empty(EventsOf(outcome));
        Assert.Equal("malformed-json", Assert.Single(UnrecognizedOf(outcome)).Kind);
    }

    [Fact]
    public void MalformedSiblingDoesNotSuppressValidSiblings()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-10", "instagram",
            MessagingBody(MessagingItem("\"message\":{\"mid\":\"m-ok-1\",\"text\":\"valid\"}")
                + "," + MessagingItem("\"postback\":{\"mid\":\"m-pb-x\"}") // incomplete: no title/payload
                + "," + MessagingItem("\"read\":{\"mid\":\"m-seen-2\"}")));

        var events = EventsOf(outcome);
        Assert.Equal(2, events.Count);
        Assert.Contains(events, e => e is InstagramMessageReceived { ProviderMessageId: "m-ok-1" });
        Assert.Contains(events, e => e is InstagramMessageRead { ProviderMessageId: "m-seen-2" });
        Assert.Equal("postback-incomplete", Assert.Single(UnrecognizedOf(outcome)).Kind);
        Assert.Empty(IgnoredOf(outcome));
    }

    [Fact]
    public void FutureUnknownPropertiesAreIgnoredSafely()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-11", "instagram",
            """{"object":"instagram","future_root":true,"entry":[{"id":"17841408147298714","time":1502905976963,"future_entry":"x","messaging":[{"sender":{"id":"u1"},"timestamp":1502905976377,"future_messaging":1,"message":{"mid":"m-11","text":"hi","future_message":true}}]}]}""");

        var message = Assert.IsType<InstagramMessageReceived>(Assert.Single(EventsOf(outcome)));
        Assert.Equal("hi", message.Text);
        Assert.Empty(UnrecognizedOf(outcome));
        Assert.Empty(IgnoredOf(outcome));
    }

    // ------------------------------------------------------------------ identity / fan-out

    [Fact]
    public void FragmentIdentityIsDeterministicAcrossRedeliveryAndDistinctPerSibling()
    {
        var body = MessagingBody(MessagingItem("\"message\":{\"mid\":\"m-1\",\"text\":\"one\"}")
            + "," + MessagingItem("\"message\":{\"mid\":\"m-2\",\"text\":\"two\"}"));

        var first = MetaPayloadNormalizer.Normalize("evt-12", "instagram", body);
        var second = MetaPayloadNormalizer.Normalize("evt-12", "instagram", body);

        var firstIds = EventsOf(first).Select(e => e.EventId).OrderBy(id => id).ToArray();
        var secondIds = EventsOf(second).Select(e => e.EventId).OrderBy(id => id).ToArray();
        Assert.Equal(firstIds, secondIds);
        Assert.Equal(["evt-12:e0:m0", "evt-12:e0:m1"], firstIds);
    }

    [Fact]
    public void MultiEntryPayloadGroupsEachEntryByItsOwnProviderAccount()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-13", "instagram",
            """{"object":"instagram","entry":[{"id":"account-A","time":1502905976963,"messaging":[{"sender":{"id":"u1"},"timestamp":1502905976377,"message":{"mid":"m-a","text":"for A"}}]},{"id":"account-B","time":1502905976963,"changes":[{"field":"comments","value":{"id":"c-b","text":"for B"}}]}]}""");

        Assert.Equal(2, outcome.Entries.Count);
        Assert.Equal("account-A", outcome.Entries[0].ProviderAccountId);
        Assert.Equal("account-B", outcome.Entries[1].ProviderAccountId);
        var events = EventsOf(outcome);
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal("evt-13", e.EventId.Split(':')[0]));
        Assert.Equal("account-A", Assert.IsType<InstagramMessageReceived>(events[0]).ProviderAccountId);
        Assert.Equal("account-B", Assert.IsType<InstagramCommentCreated>(events[1]).ProviderAccountId);
    }

    [Fact]
    public void EntryWithoutIdStillNormalizesWithNullProviderAccountId()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-14", "instagram",
            """{"entry":[{"time":1502905976963,"messaging":[{"sender":{"id":"u1"},"timestamp":1502905976377,"message":{"mid":"m-14","text":"hi"}}]}]}""");

        var message = Assert.IsType<InstagramMessageReceived>(Assert.Single(EventsOf(outcome)));
        Assert.Null(message.ProviderAccountId);
    }

    [Fact]
    public void MentionChangeStillNormalizesForRetainedFbPath()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-15", "instagram",
            """{"entry":[{"id":"17841408147298714","time":1502905976963,"changes":[{"field":"mentions","value":{"comment_id":"c-77"}}]}]}""");

        var mention = Assert.IsType<InstagramMentionCreated>(Assert.Single(EventsOf(outcome)));
        Assert.Equal("c-77", mention.CommentId);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1502905976963), mention.CreatedAtUtc);
    }

    [Fact]
    public void NonObjectEntryYieldsNothingWithoutThrowing()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-16", "instagram", "[1,2,3]");
        Assert.True(outcome.IsEmpty);

        var noEntry = MetaPayloadNormalizer.Normalize("evt-17", "instagram", "{\"object\":\"instagram\"}");
        Assert.True(noEntry.IsEmpty);
    }
}
