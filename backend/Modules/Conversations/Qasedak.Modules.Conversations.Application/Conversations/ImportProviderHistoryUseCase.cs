using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Conversations.Domain.Conversations;

namespace Qasedak.Modules.Conversations.Application.Conversations;

/// <summary>Channel-neutral provider-history import command (M13-013 §50/§51). Import
/// semantics are explicitly NOT a webhook: no unread inflation, no archived-thread
/// reopen, no automation fan-out.</summary>
public sealed record ProviderHistoryMessageImport(
    Guid WorkspaceId,
    string Channel,
    ChannelAccountId ChannelAccountId,
    string ParticipantId,
    string ProviderMessageId,
    MessageDirection Direction,
    string SenderId,
    string? Body,
    DateTimeOffset OccurredAtUtc,
    MessageContentKind ContentKind);

/// <summary>
/// Imports ONE provider-history message idempotently (M13-013 Phase B). Thread
/// creation converges under concurrency (the exact-thread unique index is retried,
/// never surfaced as a 500); duplicate provider ids are no-ops; the provider message
/// identity is REQUIRED (rows without one are refused — never fabricated); unread is
/// never touched; existing richer rows are never downgraded (first-write preserved).
/// History import never emits automation events — that invariant lives upstream: this
/// use case only owns projection.
/// </summary>
public sealed class ImportProviderHistoryUseCase(
    IConversationRepository conversations,
    IClock clock)
{
    public async Task<bool> ExecuteAsync(ProviderHistoryMessageImport import, CancellationToken cancellationToken = default)
    {
        if (import.WorkspaceId == Guid.Empty)
        {
            throw new ConversationsDomainException("conversation.workspaceRequired", "A conversation requires a workspace.");
        }

        if (import.ChannelAccountId is not { IsResolved: true })
        {
            throw new ConversationsDomainException("conversation.accountRequired", "History import requires the exact channel account.");
        }

        if (string.IsNullOrWhiteSpace(import.ProviderMessageId))
        {
            throw new ConversationsDomainException("message.providerIdRequired", "History import requires the provider message identity.");
        }

        // Bounded retry on the exact-thread unique index: history and webhook can
        // discover the first message of the same thread concurrently (§64).
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var conversation = await conversations.FindByParticipantAsync(
                import.WorkspaceId, import.Channel, import.ChannelAccountId, import.ParticipantId, cancellationToken);
            var created = false;
            if (conversation is null)
            {
                conversation = Conversation.Create(
                    Guid.CreateVersion7(),
                    import.WorkspaceId,
                    import.Channel,
                    import.ParticipantId,
                    clock.UtcNow,
                    import.ChannelAccountId);
                await conversations.AddAsync(conversation, cancellationToken);
                created = true;
            }

            try
            {
                conversation.AppendImportedMessage(
                    Guid.CreateVersion7(),
                    import.Direction,
                    import.ProviderMessageId,
                    import.SenderId,
                    import.Body,
                    import.OccurredAtUtc == default ? clock.UtcNow : import.OccurredAtUtc,
                    import.ContentKind);
            }
            catch (ConversationsDomainException exception) when (exception.RuleCode == "message.duplicateProviderId" && !created)
            {
                // Idempotent repeat import: same conversation, same provider message —
                // no duplicate row, no unread change, no downgrade.
                return false;
            }

            try
            {
                await conversations.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (UniqueConstraintViolationException) when (!created)
            {
                // The message itself collided (same MID in this conversation): a
                // concurrent import won — idempotent no-op.
                return false;
            }
            catch (UniqueConstraintViolationException) when (created && attempt < MaxThreadCreationRetries)
            {
                // Thread creation raced with another projection: reload and re-append.
                continue;
            }
        }
    }

    private const int MaxThreadCreationRetries = 3;
}
