using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Conversations.Application.Conversations;
using Qasedak.Modules.Conversations.Domain.Conversations;
using Xunit;

namespace Qasedak.Modules.Conversations.UnitTests;

/// <summary>
/// Domain + import use-case semantics for bounded provider-history projection
/// (M13-013 §51-59): imported messages never inflate unread, never fabricate text,
/// dedupe by provider mid, preserve first-write (no downgrade), and a later real-time
/// webhook observation accounts unread exactly once.
/// </summary>
public sealed class ImportProviderHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly ChannelAccountId Account = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));

    [Fact]
    public void ImportedMessagesNeverAffectUnreadOrReopenArchivedThreads()
    {
        var conversation = Conversation.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "instagram", "participant-1", Now, Account);
        conversation.Archive(Now);

        conversation.AppendImportedMessage(Guid.CreateVersion7(), MessageDirection.Inbound, "mid-history-1", "participant-1", "hello from history", Now.AddMinutes(-10), MessageContentKind.Text);

        Assert.Equal(0, conversation.UnreadCount);
        Assert.Equal(ConversationStatus.Archived, conversation.Status);
        Assert.Equal(Now.AddMinutes(-10), conversation.LastMessageAtUtc);
    }

    [Fact]
    public void DuplicateProviderIdIsRejectedByTheSameRuleCode()
    {
        var conversation = Conversation.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "instagram", "participant-1", Now, Account);
        conversation.AppendImportedMessage(Guid.CreateVersion7(), MessageDirection.Inbound, "mid-1", "participant-1", "first", Now, MessageContentKind.Text);

        var exception = Assert.Throws<ConversationsDomainException>(() =>
            conversation.AppendImportedMessage(Guid.CreateVersion7(), MessageDirection.Inbound, "mid-1", "participant-1", "second", Now.AddMinutes(1), MessageContentKind.Text));

        Assert.Equal("message.duplicateProviderId", exception.RuleCode);
    }

    [Fact]
    public void HistoryImportRequiresProviderMessageIdNeverFabricated()
    {
        var conversation = Conversation.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "instagram", "participant-1", Now, Account);

        var exception = Assert.Throws<ConversationsDomainException>(() =>
            conversation.AppendImportedMessage(Guid.CreateVersion7(), MessageDirection.Inbound, " ", "participant-1", "x", Now, MessageContentKind.Text));

        Assert.Equal("message.providerIdRequired", exception.RuleCode);
    }

    [Fact]
    public void UnsupportedAndShareContentKindsAreTruthfulNeverFabricatedText()
    {
        var conversation = Conversation.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "instagram", "participant-1", Now, Account);

        var unsupported = conversation.AppendImportedMessage(
            Guid.CreateVersion7(), MessageDirection.Inbound, "mid-u", "participant-1", null, Now, MessageContentKind.Unsupported);
        var share = conversation.AppendImportedMessage(
            Guid.CreateVersion7(), MessageDirection.Inbound, "mid-s", "participant-1", "https://www.instagram.com/p/x/", Now, MessageContentKind.Share);

        Assert.Equal(MessageContentKind.Unsupported, unsupported.ContentKind);
        Assert.Equal(string.Empty, unsupported.Body);
        Assert.Equal(MessageContentKind.Share, share.ContentKind);
        Assert.Equal(MessageImportSource.ProviderHistory, share.ImportSource);
    }

    [Fact]
    public void WebhookObservationMarksHistoryRowAndIncrementsUnreadExactlyOnce()
    {
        var conversation = Conversation.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "instagram", "participant-1", Now, Account);
        conversation.AppendImportedMessage(Guid.CreateVersion7(), MessageDirection.Inbound, "mid-x", "participant-1", "old", Now.AddMinutes(-60), MessageContentKind.Text);

        conversation.MarkObservedByWebhook("mid-x", Now);

        Assert.Equal(1, conversation.UnreadCount);
        var message = conversation.Messages.Single(m => m.ProviderMessageId == "mid-x");
        Assert.True(message.WebhookObserved);

        // A second webhook redelivery must NOT increment again.
        Assert.Throws<ConversationsDomainException>(() => conversation.MarkObservedByWebhook("mid-x", Now.AddMinutes(1)));
        Assert.Equal(1, conversation.UnreadCount);
    }

    [Fact]
    public async Task ImportUseCaseRefusesMissingProviderIdentity()
    {
        var repository = new InMemoryConversationRepository();
        var useCase = new ImportProviderHistoryUseCase(repository, new FixedClock(Now));

        var exception = await Assert.ThrowsAsync<ConversationsDomainException>(() =>
            useCase.ExecuteAsync(new ProviderHistoryMessageImport(
                Guid.CreateVersion7(), "instagram", Account, "participant-1", " ",
                MessageDirection.Inbound, "participant-1", "x", Now, MessageContentKind.Text)));

        Assert.Equal("message.providerIdRequired", exception.RuleCode);
    }

    [Fact]
    public async Task ImportUseCaseIsIdempotentAndDoesNotDowngrade()
    {
        var repository = new InMemoryConversationRepository();
        var useCase = new ImportProviderHistoryUseCase(repository, new FixedClock(Now));
        var workspaceId = Guid.CreateVersion7();

        var first = await useCase.ExecuteAsync(new ProviderHistoryMessageImport(
            workspaceId, "instagram", Account, "participant-1", "mid-1",
            MessageDirection.Inbound, "participant-1", "richer body", Now, MessageContentKind.Text));
        var second = await useCase.ExecuteAsync(new ProviderHistoryMessageImport(
            workspaceId, "instagram", Account, "participant-1", "mid-1",
            MessageDirection.Inbound, "participant-1", null, Now, MessageContentKind.NoText));

        Assert.True(first);
        Assert.False(second);
        var conversation = Assert.Single(repository.Store);
        var message = Assert.Single(conversation.Messages);
        Assert.Equal("richer body", message.Body);
        Assert.Equal(MessageContentKind.Text, message.ContentKind);
        Assert.Equal(0, conversation.UnreadCount);
    }

    [Fact]
    public async Task ImportCreatesExactAccountThreadPerParticipant()
    {
        var repository = new InMemoryConversationRepository();
        var useCase = new ImportProviderHistoryUseCase(repository, new FixedClock(Now));
        var workspaceId = Guid.CreateVersion7();
        var otherAccount = new ChannelAccountId(Guid.Parse("22222222-2222-2222-2222-222222222222"));

        await useCase.ExecuteAsync(new ProviderHistoryMessageImport(
            workspaceId, "instagram", Account, "participant-1", "mid-1", MessageDirection.Inbound, "participant-1", "a", Now, MessageContentKind.Text));
        await useCase.ExecuteAsync(new ProviderHistoryMessageImport(
            workspaceId, "instagram", otherAccount, "participant-1", "mid-1", MessageDirection.Inbound, "participant-1", "a", Now, MessageContentKind.Text));

        Assert.Equal(2, repository.Store.Count);
    }

    private sealed class InMemoryConversationRepository : IConversationRepository
    {
        public List<Conversation> Store { get; } = [];

        public Task<Conversation?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Store.FirstOrDefault(c => c.Id == id));

        public Task<Conversation?> FindByParticipantAsync(Guid workspaceId, string channel, ChannelAccountId? channelAccountId, string participantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Store.FirstOrDefault(c =>
                c.WorkspaceId == workspaceId && c.Channel == channel && c.ChannelAccountId == channelAccountId && c.ParticipantId == participantId));

        public Task AddAsync(Conversation conversation, CancellationToken cancellationToken = default)
        {
            Store.Add(conversation);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
