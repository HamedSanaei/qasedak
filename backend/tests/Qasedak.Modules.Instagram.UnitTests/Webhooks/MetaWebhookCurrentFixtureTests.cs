using Qasedak.Modules.Instagram.Application.Webhooks;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests;

/// <summary>Small sanitized first-party-shaped fixtures pinned by M13-015.</summary>
public sealed class MetaWebhookCurrentFixtureTests
{
    [Fact]
    public void LiveCommentFixturePreservesLiveSemantics()
    {
        var outcome = Normalize("live-comment-current.json");
        var comment = Assert.IsType<InstagramCommentCreated>(Assert.Single(Events(outcome)));
        Assert.Equal("comment-live-1", comment.CommentId);
        Assert.Equal("live-media-1", comment.MediaId);
        Assert.True(comment.IsLiveComment);
    }

    [Fact]
    public void ReplyToFixturePreservesExactOpeningMid()
    {
        var message = Assert.IsType<InstagramMessageReceived>(Assert.Single(Events(Normalize("message-reply-to-current.json"))));
        Assert.Equal("mid-reply-1", message.ProviderMessageId);
        Assert.Equal("mid-opening-1", message.RepliedToProviderMessageId);
    }

    [Fact]
    public void QuickReplyAndPostbackFixturesPreserveBoundedPayloads()
    {
        var quick = Assert.IsType<InstagramMessageReceived>(Assert.Single(Events(Normalize("message-quick-reply-current.json"))));
        Assert.Equal("MORE_INFO", quick.QuickReplyPayload);
        var postback = Assert.IsType<InstagramPostbackReceived>(Assert.Single(Events(Normalize("postback-current.json"))));
        Assert.Equal("mid-postback-1", postback.ProviderMessageId);
        Assert.Equal("Reveal", postback.Title);
        Assert.Equal("rv1.fixture", postback.Payload);
    }

    [Fact]
    public void ReadFixtureIsReceiptOnlyNeverAUserMessage()
    {
        var read = Assert.IsType<InstagramMessageRead>(Assert.Single(Events(Normalize("read-current.json"))));
        Assert.Equal("mid-seen-1", read.ProviderMessageId);
    }

    [Theory]
    [InlineData("message-unsupported-current.json", "message-unsupported")]
    [InlineData("message-share-current.json", "message-attachment-only")]
    public void UnsupportedAndShareFixturesStayNonTriggering(string fixture, string reason)
    {
        var outcome = Normalize(fixture);
        Assert.Empty(Events(outcome));
        Assert.Equal(reason, Assert.Single(Ignored(outcome)).Reason);
    }

    private static NormalizationOutcome Normalize(string fixture) =>
        MetaPayloadNormalizer.Normalize("m13-015-fixture", "instagram", ReadFixture(fixture));

    private static IIntegrationEvent[] Events(NormalizationOutcome outcome) =>
        outcome.Entries.SelectMany(entry => entry.Events).ToArray();

    private static IgnoredWebhookFragment[] Ignored(NormalizationOutcome outcome) =>
        outcome.Entries.SelectMany(entry => entry.Ignored).ToArray();

    private static string ReadFixture(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "webhook", fileName);
        return File.ReadAllText(path);
    }
}
