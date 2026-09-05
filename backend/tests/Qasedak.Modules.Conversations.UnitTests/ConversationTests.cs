using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Conversations.Domain.Conversations;
using Xunit;

namespace Qasedak.Modules.Conversations.UnitTests;

public sealed class ConversationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 9, 0, 0, TimeSpan.Zero);

    private static Conversation NewConversation() =>
        Conversation.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "instagram", "participant-1", Now);

    [Fact]
    public void CreateRequiresWorkspaceChannelAndParticipant()
    {
        Assert.Throws<ConversationsDomainException>(() =>
            Conversation.Create(Guid.CreateVersion7(), Guid.Empty, "instagram", "p", Now));
        Assert.Throws<ConversationsDomainException>(() =>
            Conversation.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "", "p", Now));
        Assert.Throws<ConversationsDomainException>(() =>
            Conversation.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "instagram", " ", Now));
        var ok = NewConversation();
        Assert.Equal(ConversationStatus.Open, ok.Status);
    }

    [Fact]
    public void CreateAcceptsExactAccountAndRejectsUnresolvedSentinel()
    {
        var account = new ChannelAccountId(Guid.CreateVersion7());
        var bound = Conversation.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "instagram", "p", Now, account);
        Assert.Equal(account, bound.ChannelAccountId);

        var legacy = NewConversation();
        Assert.Null(legacy.ChannelAccountId);

        Assert.Throws<ConversationsDomainException>(() =>
            Conversation.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "instagram", "p", Now, default(ChannelAccountId)));
    }

    [Fact]
    public void InboundMessagesIncreaseUnreadAndMoveLastActivity()
    {
        var conversation = NewConversation();

        conversation.AppendMessage(Guid.CreateVersion7(), MessageDirection.Inbound, "mid-1", "participant-1", "hello", Now.AddMinutes(1));
        conversation.AppendMessage(Guid.CreateVersion7(), MessageDirection.Inbound, "mid-2", "participant-1", "again", Now.AddMinutes(2));

        Assert.Equal(2, conversation.UnreadCount);
        Assert.Equal(Now.AddMinutes(2), conversation.LastMessageAtUtc);
        Assert.Equal(2, conversation.Messages.Count);
    }

    [Fact]
    public void DuplicateProviderMessageIdIsRejected()
    {
        var conversation = NewConversation();
        conversation.AppendMessage(Guid.CreateVersion7(), MessageDirection.Inbound, "mid-1", "participant-1", "hello", Now.AddMinutes(1));

        Assert.Throws<ConversationsDomainException>(() =>
            conversation.AppendMessage(Guid.CreateVersion7(), MessageDirection.Inbound, "mid-1", "participant-1", "dup", Now.AddMinutes(2)));
    }

    [Fact]
    public void OversizedBodiesAreRejectedByRuleCode()
    {
        var conversation = NewConversation();

        var exception = Assert.Throws<ConversationsDomainException>(() =>
            conversation.AppendMessage(Guid.CreateVersion7(), MessageDirection.Inbound, null, "p", new string('x', 1001), Now.AddMinutes(1)));

        Assert.Equal("message.tooLong", exception.RuleCode);
    }

    [Fact]
    public void MarkReadResetsUnreadAndSecondReadIsRejected()
    {
        var conversation = NewConversation();
        conversation.AppendMessage(Guid.CreateVersion7(), MessageDirection.Inbound, "mid-1", "participant-1", "hello", Now.AddMinutes(1));

        conversation.MarkRead(Now.AddMinutes(5));

        Assert.Equal(0, conversation.UnreadCount);
        Assert.Throws<ConversationsDomainException>(() => conversation.MarkRead(Now.AddMinutes(6)));
    }

    [Fact]
    public void ArchiveHidesThreadButInboundReopensIt()
    {
        var conversation = NewConversation();
        conversation.Archive(Now.AddMinutes(10));
        Assert.Equal(ConversationStatus.Archived, conversation.Status);
        Assert.Throws<ConversationsDomainException>(() => conversation.Archive(Now.AddMinutes(11)));

        conversation.AppendMessage(Guid.CreateVersion7(), MessageDirection.Inbound, "mid-9", "participant-1", "still here?", Now.AddMinutes(12));

        Assert.Equal(ConversationStatus.Open, conversation.Status);
    }

    [Fact]
    public void OutboundMessagesDoNotAffectUnreadCount()
    {
        var conversation = NewConversation();

        conversation.AppendMessage(Guid.CreateVersion7(), MessageDirection.Outbound, "mid-out", "our-account", "reply", Now.AddMinutes(3));

        Assert.Equal(0, conversation.UnreadCount);
        Assert.Equal(Now.AddMinutes(3), conversation.LastMessageAtUtc);
    }

    [Fact]
    public void FromStateRestoresAggregateWithMessages()
    {
        var id = Guid.CreateVersion7();
        var workspaceId = Guid.CreateVersion7();
        var messageStates = new List<MessageState>
        {
            new(Guid.CreateVersion7(), id, MessageDirection.Inbound, "mid-a", "participant-1", "hi", Now.AddMinutes(1)),
            new(Guid.CreateVersion7(), id, MessageDirection.Outbound, "mid-b", "our-account", "yo", Now.AddMinutes(2)),
        };

        var restored = Conversation.FromState(
            id, workspaceId, "instagram", new ChannelAccountId(Guid.CreateVersion7()), "participant-1",
            ConversationStatus.Open, Now, Now.AddMinutes(2), 1, messageStates);

        Assert.Equal(workspaceId, restored.WorkspaceId);
        Assert.NotNull(restored.ChannelAccountId);
        Assert.Equal(2, restored.Messages.Count);
        Assert.Equal(1, restored.UnreadCount);
        restored.AppendMessage(Guid.CreateVersion7(), MessageDirection.Inbound, "mid-c", "participant-1", "more", Now.AddMinutes(3));
        Assert.Equal(2, restored.UnreadCount);
    }
}
