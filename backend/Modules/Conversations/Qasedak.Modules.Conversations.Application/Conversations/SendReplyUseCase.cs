using Qasedak.Modules.Conversations.Domain.Conversations;

namespace Qasedak.Modules.Conversations.Application.Conversations;

/// <summary>Stable failure codes for the reply flow.</summary>
public static class ReplyFailures
{
    public const string NotFound = "conversation.notFound";

    public const string ArchivedThread = "reply.archivedThread";

    public const string MessagingWindowClosed = "reply.messagingWindowClosed";

    public const string EmptyText = "reply.emptyText";

    public const string TooLongText = "reply.tooLong";
}

public sealed record SendReplyCommand(
    Guid WorkspaceId,
    Guid ConversationId,
    string Text,
    DateTimeOffset SentAtUtc,
    string SenderId = "workspace");

public sealed record SendReplyResult(Guid? MessageId, string? FailureCode)
{
    public bool Succeeded => MessageId is not null;

    public static SendReplyResult Ok(Guid messageId) => new(messageId, null);

    public static SendReplyResult Fail(string failureCode) => new(null, failureCode);
}

/// <summary>
/// Sends a workspace-authored outbound reply through the thread's channel. Compliance is
/// enforced before any network call: the thread must be open and the recipient inside the
/// 24-hour customer service window (measured from the newest inbound message). Delivery
/// happens first — only an accepted send is appended to the aggregate, so local state never
/// claims a message that was not sent.
/// </summary>
public sealed class SendReplyUseCase(IConversationRepository repository, IConversationChannelGateway gateway)
{
    /// <summary>Meta's documented customer service window.</summary>
    public static readonly TimeSpan MessagingWindow = TimeSpan.FromHours(24);

    public async Task<SendReplyResult> ExecuteAsync(SendReplyCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Text))
        {
            return SendReplyResult.Fail(ReplyFailures.EmptyText);
        }

        if (command.Text.Length > Conversation.MaxBodyLength)
        {
            return SendReplyResult.Fail(ReplyFailures.TooLongText);
        }

        var conversation = await repository.FindByIdAsync(command.ConversationId, cancellationToken);
        if (conversation is null || conversation.WorkspaceId != command.WorkspaceId)
        {
            return SendReplyResult.Fail(ReplyFailures.NotFound);
        }

        if (conversation.Status == ConversationStatus.Archived)
        {
            return SendReplyResult.Fail(ReplyFailures.ArchivedThread);
        }

        var lastInboundUtc = conversation.Messages
            .Where(m => m.Direction == MessageDirection.Inbound)
            .Select(m => m.OccurredAtUtc)
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();
        if (command.SentAtUtc - lastInboundUtc > MessagingWindow)
        {
            return SendReplyResult.Fail(ReplyFailures.MessagingWindowClosed);
        }

        var delivery = await gateway.DeliverAsync(
            new ChannelDeliveryRequest(command.WorkspaceId, conversation.Channel, conversation.ParticipantId, command.Text),
            cancellationToken);
        if (!delivery.Accepted)
        {
            return SendReplyResult.Fail(delivery.FailureCode ?? "channel.rejected");
        }

        var message = conversation.AppendMessage(
            Guid.CreateVersion7(),
            MessageDirection.Outbound,
            providerMessageId: null,
            senderId: command.SenderId,
            body: command.Text,
            occurredAtUtc: command.SentAtUtc);
        await repository.SaveChangesAsync(cancellationToken);

        return SendReplyResult.Ok(message.Id);
    }
}
