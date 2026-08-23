using Qasedak.BuildingBlocks.Application;
using Qasedak.Modules.Conversations.Application.Conversations;
using Qasedak.Modules.Conversations.Domain.Conversations;
using Xunit;

namespace Qasedak.Modules.Conversations.UnitTests;

/// <summary>
/// Reply flow: compliance gates (open thread, 24h window) run before delivery; only an
/// accepted channel send is appended to the aggregate.
/// </summary>
public sealed class SendReplyTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private static Conversation OpenThread(DateTimeOffset? lastInboundUtc = null)
    {
        var conversation = Conversation.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "instagram", "customer-9", Now.AddHours(-30));
        conversation.AppendMessage(
            Guid.CreateVersion7(), MessageDirection.Inbound, "mid-9", "customer-9", "hi",
            lastInboundUtc ?? Now.AddMinutes(-10));
        return conversation;
    }

    private sealed class FakeRepository : IConversationRepository
    {
        public List<Conversation> Threads { get; } = [];

        public bool SaveCalled { get; private set; }

        public Task<Conversation?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Threads.FirstOrDefault(t => t.Id == id));

        public Task<Conversation?> FindByParticipantAsync(Guid workspaceId, string channel, string participantId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddAsync(Conversation conversation, CancellationToken cancellationToken = default)
        {
            Threads.Add(conversation);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeGateway : IConversationChannelGateway
    {
        public ChannelDeliveryRequest? LastRequest { get; private set; }

        public ChannelDeliveryResult NextResult { get; set; } = ChannelDeliveryResult.Delivered();

        public int Calls { get; private set; }

        public Task<ChannelDeliveryResult> DeliverAsync(ChannelDeliveryRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(NextResult);
        }
    }

    private static SendReplyCommand Command(Guid workspaceId, Guid conversationId, string text) =>
        new(workspaceId, conversationId, text, Now);

    [Fact]
    public async Task AcceptedDeliveryAppendsOutboundMessageAndPersists()
    {
        var repository = new FakeRepository();
        var gateway = new FakeGateway();
        var thread = OpenThread();
        await repository.AddAsync(thread);
        var useCase = new SendReplyUseCase(repository, gateway);

        var result = await useCase.ExecuteAsync(Command(thread.WorkspaceId, thread.Id, "here to help"), default);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.MessageId);
        Assert.True(repository.SaveCalled);
        var outbound = Assert.Single(thread.Messages, m => m.Direction == MessageDirection.Outbound);
        Assert.Equal("here to help", outbound.Body);
        Assert.Null(outbound.ProviderMessageId);
        Assert.Equal("workspace", outbound.SenderId);
    }

    [Fact]
    public async Task GatewayRejectionReturnsFailureWithoutAppending()
    {
        var repository = new FakeRepository();
        var gateway = new FakeGateway { NextResult = ChannelDeliveryResult.Rejected("instagram.windowExpired") };
        var thread = OpenThread();
        await repository.AddAsync(thread);
        var useCase = new SendReplyUseCase(repository, gateway);

        var result = await useCase.ExecuteAsync(Command(thread.WorkspaceId, thread.Id, "hello"), default);

        Assert.False(result.Succeeded);
        Assert.Equal("instagram.windowExpired", result.FailureCode);
        Assert.False(repository.SaveCalled);
        Assert.Single(thread.Messages); // Only the inbound seed remains.
    }

    [Fact]
    public async Task UnknownOrForeignThreadFailsWithNotFound()
    {
        var repository = new FakeRepository();
        var gateway = new FakeGateway();
        var thread = OpenThread();
        await repository.AddAsync(thread);
        var useCase = new SendReplyUseCase(repository, gateway);

        var missing = await useCase.ExecuteAsync(Command(Guid.CreateVersion7(), Guid.CreateVersion7(), "x"), default);
        var foreign = await useCase.ExecuteAsync(Command(Guid.CreateVersion7(), thread.Id, "x"), default);

        Assert.Equal(ReplyFailures.NotFound, missing.FailureCode);
        Assert.Equal(ReplyFailures.NotFound, foreign.FailureCode);
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task ArchivedThreadIsRejectedBeforeDelivery()
    {
        var repository = new FakeRepository();
        var gateway = new FakeGateway();
        var thread = OpenThread();
        thread.Archive(Now.AddMinutes(-5));
        await repository.AddAsync(thread);
        var useCase = new SendReplyUseCase(repository, gateway);

        var result = await useCase.ExecuteAsync(Command(thread.WorkspaceId, thread.Id, "x"), default);

        Assert.Equal(ReplyFailures.ArchivedThread, result.FailureCode);
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task RecipientOutsideTwentyFourHourWindowIsRejectedBeforeDelivery()
    {
        var repository = new FakeRepository();
        var gateway = new FakeGateway();
        var thread = OpenThread(lastInboundUtc: Now.AddHours(-25));
        await repository.AddAsync(thread);
        var useCase = new SendReplyUseCase(repository, gateway);

        var result = await useCase.ExecuteAsync(Command(thread.WorkspaceId, thread.Id, "x"), default);

        Assert.Equal(ReplyFailures.MessagingWindowClosed, result.FailureCode);
        Assert.Equal(0, gateway.Calls);
        // Boundary: just inside the window passes the gate.
        var fresh = OpenThread(lastInboundUtc: Now.AddHours(-24).AddSeconds(1));
        await repository.AddAsync(fresh);
        var inside = await useCase.ExecuteAsync(Command(fresh.WorkspaceId, fresh.Id, "still allowed"), default);
        Assert.True(inside.Succeeded);
    }

    [Fact]
    public async Task EmptyAndOversizedTextAreRejectedBeforeDelivery()
    {
        var repository = new FakeRepository();
        var gateway = new FakeGateway();
        var thread = OpenThread();
        await repository.AddAsync(thread);
        var useCase = new SendReplyUseCase(repository, gateway);

        var empty = await useCase.ExecuteAsync(Command(thread.WorkspaceId, thread.Id, "   "), default);
        var tooLong = await useCase.ExecuteAsync(
            Command(thread.WorkspaceId, thread.Id, new string('x', Conversation.MaxBodyLength + 1)), default);

        Assert.Equal(ReplyFailures.EmptyText, empty.FailureCode);
        Assert.Equal(ReplyFailures.TooLongText, tooLong.FailureCode);
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task GatewayReceivesThreadChannelAndParticipant()
    {
        var repository = new FakeRepository();
        var gateway = new FakeGateway();
        var thread = OpenThread();
        await repository.AddAsync(thread);
        var useCase = new SendReplyUseCase(repository, gateway);

        await useCase.ExecuteAsync(Command(thread.WorkspaceId, thread.Id, "hello"), default);

        Assert.NotNull(gateway.LastRequest);
        Assert.Equal("instagram", gateway.LastRequest!.Channel);
        Assert.Equal("customer-9", gateway.LastRequest.ParticipantId);
        Assert.Equal("hello", gateway.LastRequest.Text);
    }
}
