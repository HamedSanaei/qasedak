using Qasedak.BuildingBlocks.Application;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Domain.Accounts;

namespace Qasedak.Modules.Instagram.Application.HistorySync;

/// <summary>
/// One bounded stage of a conversation-history sync operation for ONE exact connected
/// account (M13-013 Phase B). Invariants:
/// - the operation must exist and be claimable (Queued → Running); terminal
///   operations replay as a no-op (at-least-once job delivery);
/// - exact-account preflight BEFORE any provider call: account exists, not
///   disconnected, protected token available; otherwise zero provider traffic;
/// - bounded traversal: conversation pages ≤ <see cref="ConversationHistoryPolicy.MaxConversationPagesPerOperation"/>,
///   conversations ≤ MaxConversationsPerOperation; each conversation reads bounded
///   message ids but detail is fetched for AT MOST the newest 20 (the provider's
///   documented limit); bounded concurrency (≤ 4 detail calls);
/// - direction derives ONLY from exact provider identities (from.id == professional
///   ProviderAccountId ⇒ Outbound; else from.id == participant AND to includes the
///   professional account ⇒ Inbound; ambiguous ⇒ conversation skipped);
/// - the external participant is the exact counterpart identity; zero/multiple
///   external participants ⇒ conversation skipped boundedly;
/// - HISTORY NEVER EMITS <c>InstagramMessageReceived</c>: imports cross through the
///   channel-neutral gateway only — zero AutomationRuns, zero outbound calls;
/// - provider cursors are bounded components only; continuation jobs resume from the
///   operation checkpoint; a fresh sync always starts from the newest page;
/// - the "deleted" error for older-than-20 details is classified
///   <see cref="ConversationHistoryFailures.HistoryUnavailableOutsideRecentWindow"/>,
///   NEVER persisted as deletion, NEVER retried, NEVER counted as account-unhealthy.
/// </summary>
public sealed partial class ConversationHistorySyncUseCase(
    IConnectedAccountRepository accounts,
    IProtectedTokenStore tokens,
    IProviderSyncOperationStore operations,
    IInstagramConversationHistoryClient client,
    IConversationHistoryImportGateway gateway,
    IClock clock)
{
    /// <summary>Stage outcome: how much work remains and whether the stage succeeded.
    /// <see cref="NextStage"/> is the deterministic continuation stage number when
    /// <see cref="HasMoreWork"/> is true.</summary>
    public sealed record StageResult(
        SyncOperationStatus Status,
        bool HasMoreWork,
        string? FailureCode,
        bool Transient,
        ProviderSyncCounters Counters,
        int NextStage = 0);

    public async Task<StageResult> ExecuteAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        var operation = await operations.FindByIdAsync(operationId, cancellationToken);
        if (operation is null)
        {
            return Terminal(SyncOperationStatus.Failed, "convsync.operationMissing", false);
        }

        if (operation.Status is SyncOperationStatus.CompletedWithinProviderLimits or SyncOperationStatus.Failed)
        {
            // At-least-once redelivery of a settled operation — idempotent replay.
            return Terminal(operation.Status, null, false);
        }

        var account = await accounts.FindByIdAsync(operation.ConnectedAccountId, cancellationToken);
        if (account is null || account.IsDisconnected)
        {
            await operations.FailAsync(operationId, "account.notAvailable", transient: false, new ProviderSyncCounters(), clock.UtcNow, cancellationToken);
            return Terminal(SyncOperationStatus.Failed, "account.notAvailable", false);
        }

        var accessToken = await tokens.GetAsync(account.Id, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            await operations.FailAsync(operationId, AccountFailures.TokenMissing, transient: false, new ProviderSyncCounters(), clock.UtcNow, cancellationToken);
            return Terminal(SyncOperationStatus.Failed, AccountFailures.TokenMissing, false);
        }

        if (!await operations.TryStartAsync(operationId, clock.UtcNow, cancellationToken))
        {
            // Another worker holds the stage — report success so M13-004 settles this
            // delivery; the owning worker settles the operation.
            return new StageResult(SyncOperationStatus.Running, HasMoreWork: false, null, false, new ProviderSyncCounters(), NextStage: 0);
        }

        var counters = new ProviderSyncCounters(
            operation.ConversationsObserved,
            operation.MessageIdsObserved,
            operation.DetailsFetched,
            operation.MessagesImported,
            operation.Duplicates,
            operation.Unsupported,
            operation.HistoryWindowLimited,
            operation.RateLimited);
        var stage = operation.Stage;
        var cursor = operation.NextProviderCursor;
        var conversationsProcessed = 0;
        var pages = 0;

        try
        {
            while (pages < ConversationHistoryPolicy.MaxConversationPagesPerOperation &&
                   conversationsProcessed < ConversationHistoryPolicy.MaxConversationsPerOperation)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var listResult = await client.ListConversationsPageAsync(
                    accessToken, account.ProviderUserId, ConversationHistoryPolicy.PageSize, cursor, cancellationToken);

                if (listResult is ConversationListResult.Failed failed)
                {
                    return await FailAsync(operation, stage, counters, failed.FailureCode, failed.Transient, cancellationToken);
                }

                var page = ((ConversationListResult.Ok)listResult).Page;
                pages++;

                foreach (var conversation in page.Conversations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (conversationsProcessed >= ConversationHistoryPolicy.MaxConversationsPerOperation)
                    {
                        break;
                    }

                    counters.ConversationsObserved++;
                    await SyncConversationAsync(account, accessToken, conversation.ConversationId, counters, cancellationToken);
                    conversationsProcessed++;
                }

                if (!page.HasMore || page.NextAfterCursor is null)
                {
                    cursor = null;
                    break;
                }

                if (page.NextAfterCursor.Length > ConversationHistoryPolicy.MaxProviderCursorLength)
                {
                    return await FailAsync(operation, stage, counters, ConversationHistoryFailures.CursorOversized, transient: false, cancellationToken);
                }

                if (cursor == page.NextAfterCursor)
                {
                    return await FailAsync(operation, stage, counters, ConversationHistoryFailures.CursorLoop, transient: false, cancellationToken);
                }

                cursor = page.NextAfterCursor;
            }

            if (cursor is not null && conversationsProcessed >= ConversationHistoryPolicy.MaxConversationsPerOperation)
            {
                // Budget exhausted mid-traversal: checkpoint and continue in a later job.
                var nextStage = stage + 1;
                await operations.CheckpointAsync(operationId, nextStage, cursor, counters, clock.UtcNow, cancellationToken);
                return new StageResult(SyncOperationStatus.Running, HasMoreWork: true, null, false, counters, NextStage: nextStage);
            }

            await operations.CompleteAsync(operationId, counters, clock.UtcNow, cancellationToken);
            return new StageResult(SyncOperationStatus.CompletedWithinProviderLimits, HasMoreWork: false, null, false, counters);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancellation is NOT a provider failure: the stage stays claimable.
            return new StageResult(SyncOperationStatus.Running, HasMoreWork: true, "cancelled", false, counters, NextStage: 0);
        }
    }

    private async Task SyncConversationAsync(
        ConnectedAccount account, string accessToken, string conversationId,
        ProviderSyncCounters counters, CancellationToken cancellationToken)
    {
        var messagesResult = await client.GetConversationMessagesPageAsync(accessToken, conversationId, cancellationToken);
        if (messagesResult is ConversationMessagesResult.Failed)
        {
            return; // bounded per-conversation skip; the provider error already counted
        }

        var rows = ((ConversationMessagesResult.Ok)messagesResult).Messages;
        counters.MessageIdsObserved += Math.Min(rows.Count, ConversationHistoryPolicy.MaxMessageIdsPerConversation);
        counters.Unsupported += rows.Count(r => r.IsUnsupported);

        // Detail is only retrievable for the newest 20 (provider contract). The
        // provider lists newest-first; take the newest bounded window and NEVER call
        // older ids merely to receive errors (§89).
        var detailCandidates = rows
            .Where(r => !r.IsUnsupported)
            .Take(ConversationHistoryPolicy.MaxMessageDetailCallsPerConversation)
            .ToList();

        if (rows.Count > detailCandidates.Count)
        {
            counters.HistoryWindowLimited += rows.Count - detailCandidates.Count;
        }

        using var concurrency = new SemaphoreSlim(ConversationHistoryPolicy.MaxConcurrentMessageDetailCalls);
        var tasks = detailCandidates.Select(async row =>
        {
            await concurrency.WaitAsync(cancellationToken);
            try
            {
                var result = await client.GetMessageDetailAsync(accessToken, row.MessageId, cancellationToken);
                if (result is MessageDetailResult.Ok ok)
                {
                    return ok.Detail;
                }

                // Older-than-window "deleted" errors land here: bounded classification,
                // never a deletion signal, never retried (§75/§76).
                counters.IncrementHistoryWindowLimited();
                return null;
            }
            finally
            {
                concurrency.Release();
            }
        }).ToList();

        foreach (var detail in await Task.WhenAll(tasks))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (detail is null)
            {
                continue;
            }

            counters.DetailsFetched++;
            var mapped = MapDirectionAndParticipant(account, detail);
            if (mapped is null)
            {
                continue; // ambiguous/unsupported conversation — bounded skip
            }

            var imported = await gateway.ImportAsync(mapped, cancellationToken);
            if (imported)
            {
                counters.MessagesImported++;
            }
            else
            {
                counters.Duplicates++;
            }
        }

    }

    private static HistoryMessageImport? MapDirectionAndParticipant(ConnectedAccount account, ProviderMessageDetailRow detail)
    {
        // Exact provider identity rules (§45/§46): the professional account id is the
        // ProviderAccountId; a 1:1 conversation names EXACTLY ONE external identity
        // across from/to. Direction is NEVER inferred from list order.
        var externalIds = detail.ToIds
            .Concat(detail.FromId is null ? [] : [detail.FromId])
            .Where(id => id != account.ProviderUserId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (externalIds.Count != 1)
        {
            // Zero or multiple external participants (group/unsupported shapes) are
            // ambiguous — bounded skip, never fabricated direction/participant.
            return null;
        }

        var participant = externalIds[0];
        if (detail.FromId == account.ProviderUserId)
        {
            // Outbound: the professional sent to the single external recipient.
            return new HistoryMessageImport(
                account.WorkspaceId,
                account.Id,
                participant,
                detail.MessageId,
                HistoryMessageDirection.Outbound,
                account.ProviderUserId,
                detail.Body,
                detail.CreatedAtUtc ?? DateTimeOffset.MinValue,
                Classify(detail));
        }

        if (detail.FromId == participant && detail.ToIds.Contains(account.ProviderUserId))
        {
            // Inbound: the single external participant sent and the professional is a recipient.
            return new HistoryMessageImport(
                account.WorkspaceId,
                account.Id,
                participant,
                detail.MessageId,
                HistoryMessageDirection.Inbound,
                detail.FromId,
                detail.Body,
                detail.CreatedAtUtc ?? DateTimeOffset.MinValue,
                Classify(detail));
        }

        return null;
    }

    private static HistoryContentKind Classify(ProviderMessageDetailRow detail)
    {
        if (detail.IsUnsupported)
        {
            return HistoryContentKind.Unsupported;
        }

        if (detail.ShareUrl is not null)
        {
            return HistoryContentKind.Share;
        }

        return string.IsNullOrWhiteSpace(detail.Body) ? HistoryContentKind.NoText : HistoryContentKind.Text;
    }

    private async Task<StageResult> FailAsync(
        ProviderSyncOperation operation, int stage, ProviderSyncCounters counters, string failureCode, bool transient, CancellationToken cancellationToken)
    {
        await operations.FailAsync(operation.OperationId, failureCode, transient, counters, clock.UtcNow, cancellationToken);
        return new StageResult(
            transient ? SyncOperationStatus.RateLimitedRetrying : SyncOperationStatus.Failed,
            HasMoreWork: false,
            failureCode,
            transient,
            counters);
    }

    private static StageResult Terminal(SyncOperationStatus status, string? failureCode, bool transient) =>
        new(status, HasMoreWork: false, failureCode, transient, new ProviderSyncCounters());
}
