using Qasedak.Modules.Instagram.Application.Webhooks;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests;

/// <summary>
/// Normalization fixtures: canonical Meta payload shapes map to explicit integration
/// events; unknown fields and malformed JSON surface as unrecognized fragments instead of
/// being dropped or thrown away.
/// </summary>
public sealed class MetaPayloadNormalizerTests
{

    [Fact]
    public void MessagingEntryBecomesMessageReceived()
    {
        var outcome = MetaPayloadNormalizer.Normalize(
            "evt-1",
            "instagram",
            """{"object":"instagram","entry":[{"id":"17841400000000000","time":1771800000,"messaging":[{"sender":{"id":"user-42"},"recipient":{"id":"17841400000000000"},"timestamp":1771800100,"message":{"mid":"m-1","text":"hello there"}}]}]}""");

        var message = Assert.IsType<InstagramMessageReceived>(Assert.Single(outcome.Events));
        Assert.Equal("evt-1", message.EventId);
        Assert.Equal("17841400000000000", message.ProviderAccountId);
        Assert.Equal("user-42", message.SenderId);
        Assert.Equal("hello there", message.Text);
        Assert.True(outcome.Unrecognized.Count == 0);
    }

    [Fact]
    public void EchoMessagesAreSkipped()
    {
        var outcome = MetaPayloadNormalizer.Normalize(
            "evt-2",
            "instagram",
            """{"entry":[{"id":"17841400000000000","messaging":[{"sender":{"id":"17841400000000000"},"message":{"is_echo":true,"mid":"m-2","text":"we said this"}}]}]}""");

        Assert.Empty(outcome.Events);
        Assert.Empty(outcome.Unrecognized);
    }

    [Fact]
    public void CommentChangeBecomesCommentCreated()
    {
        var outcome = MetaPayloadNormalizer.Normalize(
            "evt-3",
            "instagram",
            """{"object":"instagram","entry":[{"id":"17841400000000000","changes":[{"field":"comments","value":{"id":"comment-9","from":{"id":"commenter-42"},"text":"nice shot","created_time":1771900000}}]}]}""");

        var comment = Assert.IsType<InstagramCommentCreated>(Assert.Single(outcome.Events));
        Assert.Equal("comment-9", comment.CommentId);
        Assert.Equal("commenter-42", comment.FromId);
        Assert.Equal("nice shot", comment.Text);
        Assert.Equal(2026, comment.CreatedAtUtc.Year);
    }

    [Fact]
    public void CommentWithoutFromStillNormalizesWithNullSender()
    {
        var outcome = MetaPayloadNormalizer.Normalize(
            "evt-3b",
            "instagram",
            """{"object":"instagram","entry":[{"id":"17841400000000000","changes":[{"field":"comments","value":{"id":"comment-10","text":"anon"}}]}]}""");

        var comment = Assert.IsType<InstagramCommentCreated>(Assert.Single(outcome.Events));
        Assert.Null(comment.FromId);
        Assert.Equal("anon", comment.Text);
    }

    [Fact]
    public void MentionChangeBecomesMentionCreated()
    {
        var outcome = MetaPayloadNormalizer.Normalize(
            "evt-4",
            "instagram",
            """{"entry":[{"id":"17841400000000000","changes":[{"field":"mentions","value":{"comment_id":"c-77"}}]}]}""");

        var mention = Assert.IsType<InstagramMentionCreated>(Assert.Single(outcome.Events));
        Assert.Equal("c-77", mention.CommentId);
    }

    [Fact]
    public void UnknownFieldsSurfaceAsUnrecognizedFragments()
    {
        var outcome = MetaPayloadNormalizer.Normalize(
            "evt-5",
            "instagram",
            """{"entry":[{"id":"x","changes":[{"field":"story_insights","value":{"metric":1}}]},{"id":"y","changes":[{"field":"live_comments","value":{}}]}]}""");

        Assert.Empty(outcome.Events);
        Assert.Equal(2, outcome.Unrecognized.Count);
        Assert.Contains(outcome.Unrecognized, fragment => fragment.Kind == "field:story_insights");
    }

    [Fact]
    public void MalformedJsonYieldsSingleFragmentWithoutThrowing()
    {
        var outcome = MetaPayloadNormalizer.Normalize("evt-6", "instagram", "{\"entry\":[ broken");

        Assert.Empty(outcome.Events);
        Assert.Equal("malformed-json", Assert.Single(outcome.Unrecognized).Kind);
    }
}

