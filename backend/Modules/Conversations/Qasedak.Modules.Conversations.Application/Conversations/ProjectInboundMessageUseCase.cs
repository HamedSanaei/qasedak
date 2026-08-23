using Qasedak.BuildingBlocks.Application;
using Qasedak.Modules.Conversations.Domain.Conversations;

namespace Qasedak.Modules.Conversations.Application.Conversations;

/// <summary>Neutral inbound-message projection command (channel-agnostic on purpose).</summary>
public sealed record InboundMessageProjection(
    Guid WorkspaceId,
    string Channel,
    string ParticipantId,
    string? ProviderMessageId,
    string SenderId,
    string Text,
    DateTimeOffset OccurredAtUtc);

public readonly record struct InboundProjectionResult(Guid ConversationId, bool Duplicate, bool Created)
{
    public static InboundProjectionResult Appended(Guid conversationId, bool created) =>
        new(conversationId, Duplicate: false, Created: created);

    public static InboundProjectionResult DuplicateDelivery(Guid conversationId) =>
        new(conversationId, Duplicate: true, Created: false);
}

/// <summary>
/// Projects one inbound message into conversation state idempotently: the thread is
/// created on first sight (one conversation per workspace/channel/participant) and the
/// aggregate-level provider-message uniqueness makes duplicate deliveries a no-op rather
/// than an error — Meta retries and concurrent webhook workers stay safe. The caller
/// supplies the owning workspace; how workspaces bind to provider accounts is decided
/// outside this module (composition root).
/// </summary>
public sealed class ProjectInboundMessageUseCase(
    IConversationRepository conversations,
    IClock clock)
{
    public async Task<InboundProjectionResult> ExecuteAsync(InboundMessageProjection projection, CancellationToken cancellationToken = default)
    {
        if (projection.WorkspaceId == Guid.Empty)
        {
            throw new ConversationsDomainException("conversation.workspaceRequired", "A conversation requires a workspace.");
        }

        var conversation = await conversations.FindByParticipantAsync(
            projection.WorkspaceId, projection.Channel, projection.ParticipantId, cancellationToken);
        var created = false;

        if (conversation is null)
        {
            conversation = Conversation.Create(
                Guid.CreateVersion7(),
                projection.WorkspaceId,
                projection.Channel,
                projection.ParticipantId,
                clock.UtcNow);
            await conversations.AddAsync(conversation, cancellationToken);
            created = true;
        }

        try
        {
            conversation.AppendMessage(
                Guid.CreateVersion7(),
                MessageDirection.Inbound,
                projection.ProviderMessageId,
                projection.SenderId,
                string.IsNullOrWhiteSpace(projection.Text) ? "(no text)" : projection.Text,
                projection.OccurredAtUtc == default ? clock.UtcNow : projection.OccurredAtUtc);
        }
        catch (ConversationsDomainException exception) when (
            exception.RuleCode == "message.duplicateProviderId" && !created)
        {
            // Idempotent redelivery: state already contains this message.
            return InboundProjectionResult.DuplicateDelivery(conversation.Id);
        }
        catch (ConversationsDomainException exception) when (exception.RuleCode == "message.tooLong")
        {
            // Oversized inbound content is stored truncated rather than dropped: losing a
            // customer message is worse than clipping display text.
            conversation.AppendMessage(
                Guid.CreateVersion7(),
                MessageDirection.Inbound,
                projection.ProviderMessageId,
                projection.SenderId,
                projection.Text[..Conversation.MaxBodyLength],
                projection.OccurredAtUtc == default ? clock.UtcNow : projection.OccurredAtUtc);
        }

        await conversations.SaveChangesAsync(cancellationToken);
        return InboundProjectionResult.Appended(conversation.Id, created);
    }
}
