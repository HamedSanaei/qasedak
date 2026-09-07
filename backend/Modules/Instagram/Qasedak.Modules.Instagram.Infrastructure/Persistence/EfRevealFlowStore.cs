using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Instagram.Application.RevealFlow;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;

namespace Qasedak.Modules.Instagram.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL-backed durable reveal-flow store (M13-011). All state transitions are
/// atomic compare-and-swap UPDATE statements — exactly one candidate ever obtains a
/// provider-mutation authority, and a lost race replays the durable state with ZERO
/// provider calls. Uniqueness is enforced by the database:
/// - one flow row per logical origin (ConnectedAccountId + ProviderCommentId);
/// - globally unique correlation-token hashes (the raw token never persists);
/// - the Revealing CAS is the single-reveal authority.
/// Every store step is one short statement — no DB transaction ever spans a provider
/// HTTP call (the coordinator keeps the sequence strictly ordered).
/// </summary>
public sealed class EfRevealFlowStore(InstagramDbContext context) : IRevealFlowStore
{
    public async Task<RevealFlowSnapshot> CreateOrGetAsync(
        Guid flowId,
        Guid workspaceId,
        Guid connectedAccountId,
        string providerCommentId,
        string? participantIGSId,
        bool isLiveComment,
        DateTimeOffset notificationOccurredAtUtc,
        FollowGateMode followGateMode,
        RevealFlowContent content,
        Guid? automationId,
        int? automationVersionNumber,
        string? triggerEventId,
        int? actionIndex,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var row = new RevealFlowRow
        {
            Id = flowId,
            WorkspaceId = workspaceId,
            ConnectedAccountId = connectedAccountId,
            ProviderCommentId = providerCommentId,
            ParticipantIGSID = participantIGSId,
            IsLiveComment = isLiveComment,
            NotificationOccurredAtUtc = notificationOccurredAtUtc,
            AutomationId = automationId,
            AutomationVersionNumber = automationVersionNumber,
            TriggerEventId = triggerEventId,
            ActionIndex = actionIndex,
            OpeningPrivateReplyText = content.OpeningPrivateReplyText,
            GatePromptText = content.GatePromptText,
            PostbackButtonTitle = content.PostbackButtonTitle,
            FollowUrl = content.FollowUrl,
            FollowButtonTitle = content.FollowButtonTitle,
            RevealText = content.RevealText,
            FollowGateMode = followGateMode,
            State = RevealFlowState.Starting,
            GatePromptStatus = RevealGatePromptStatus.NotAttempted,
            RevealStatus = RevealSendStatus.NotAttempted,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        context.RevealFlows.Add(row);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return ToSnapshot(row);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            context.Entry(row).State = EntityState.Detached;
            var existing = await context.RevealFlows.AsNoTracking()
                .SingleAsync(r => r.Id == flowId, cancellationToken);
            return ToSnapshot(existing);
        }
    }

    public async Task<RevealFlowSnapshot?> GetByIdAsync(Guid flowId, CancellationToken cancellationToken = default)
    {
        var row = await context.RevealFlows.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == flowId, cancellationToken);
        return row is null ? null : ToSnapshot(row);
    }

    public async Task<RevealFlowSnapshot?> GetByOpeningMessageIdAsync(
        Guid connectedAccountId,
        string openingProviderMessageId,
        string participantIGSId,
        CancellationToken cancellationToken = default)
    {
        // Exact reply_to.mid correlation: at most one flow can own one opening message id
        // (one flow per comment, one opening per flow), and the sender must match.
        var row = await context.RevealFlows.AsNoTracking()
            .SingleOrDefaultAsync(r => r.ConnectedAccountId == connectedAccountId
                && r.OpeningPrivateReplyMessageId == openingProviderMessageId
                && r.ParticipantIGSID == participantIGSId, cancellationToken);
        return row is null ? null : ToSnapshot(row);
    }

    public async Task<IReadOnlyList<RevealFlowSnapshot>> GetAwaitingResponseAsync(
        Guid connectedAccountId,
        string participantIGSId,
        CancellationToken cancellationToken = default)
    {
        var rows = await context.RevealFlows.AsNoTracking()
            .Where(r => r.ConnectedAccountId == connectedAccountId
                && r.ParticipantIGSID == participantIGSId
                && r.State == RevealFlowState.AwaitingUserResponse)
            .OrderBy(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return rows.Select(ToSnapshot).ToList();
    }

    public async Task<RevealFlowSnapshot?> GetByCorrelationTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        var row = await context.RevealFlows.AsNoTracking()
            .SingleOrDefaultAsync(r => r.CorrelationTokenHash == tokenHash, cancellationToken);
        return row is null ? null : ToSnapshot(row);
    }

    public async Task<bool> TryMarkOpeningAttemptedAsync(
        Guid flowId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var updated = await context.RevealFlows
            .Where(r => r.Id == flowId && r.State == RevealFlowState.Starting)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.State, RevealFlowState.OpeningAttempted)
                .SetProperty(r => r.UpdatedAtUtc, nowUtc), cancellationToken);
        return updated == 1;
    }

    public async Task<bool> TryOpenAsync(
        Guid flowId,
        string openingProviderMessageId,
        string openingProviderRecipientId,
        string participantIGSId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var updated = await context.RevealFlows
            .Where(r => r.Id == flowId && r.State == RevealFlowState.OpeningAttempted)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.State, RevealFlowState.AwaitingUserResponse)
                .SetProperty(r => r.OpeningPrivateReplyMessageId, openingProviderMessageId)
                .SetProperty(r => r.OpeningPrivateReplyRecipientId, openingProviderRecipientId)
                .SetProperty(r => r.ParticipantIGSID, participantIGSId)
                .SetProperty(r => r.UpdatedAtUtc, nowUtc), cancellationToken);
        return updated == 1;
    }

    public async Task<bool> TryAdvanceUserResponseAsync(
        Guid flowId,
        DateTimeOffset lastUserMessageAtUtc,
        CancellationToken cancellationToken = default)
    {
        // Monotonic consent/window anchor: never regresses (GREATEST semantics), and the
        // state predicate makes concurrent deliveries race-safe. Qualifying inbound
        // messages refresh the anchor in every pre-reveal continuation state — a new user
        // message re-opens the 24h window even while a gate prompt is in flight/delivered
        // (never a second prompt, but the anchor must not falsely expire the reveal).
        var updated = await context.RevealFlows
            .Where(r => r.Id == flowId
                && (r.State == RevealFlowState.AwaitingUserResponse
                    || r.State == RevealFlowState.PreparingGatePrompt
                    || r.State == RevealFlowState.AwaitingPostback))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.LastUserMessageAtUtc,
                    value => EF.Functions.Greatest(value.LastUserMessageAtUtc, (DateTimeOffset?)lastUserMessageAtUtc))
                .SetProperty(row => row.UpdatedAtUtc, lastUserMessageAtUtc), cancellationToken);
        return updated == 1;
    }

    public async Task<bool> TryStartGatePromptAsync(
        Guid flowId,
        string correlationTokenHash,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        // Durable Attempting marker + correlation hash — persisted BEFORE any provider call.
        try
        {
            var updated = await context.RevealFlows
                .Where(r => r.Id == flowId && r.State == RevealFlowState.AwaitingUserResponse)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(r => r.State, RevealFlowState.PreparingGatePrompt)
                    .SetProperty(r => r.GatePromptStatus, RevealGatePromptStatus.Attempting)
                    .SetProperty(r => r.GatePromptAttemptedAtUtc, nowUtc)
                    .SetProperty(r => r.CorrelationTokenHash, correlationTokenHash)
                    .SetProperty(r => r.CorrelationTokenPurpose, RevealCorrelation.TokenPurpose)
                    .SetProperty(r => r.UpdatedAtUtc, nowUtc), cancellationToken);
            return updated == 1;
        }
        catch (Exception exception) when (IsUniqueViolation(exception))
        {
            // Token-hash collision (astronomically unlikely): fail closed, zero provider
            // calls — the caller replays the durable state. ExecuteUpdateAsync surfaces
            // the raw Npgsql PostgresException (not wrapped in DbUpdateException).
            return false;
        }
    }

    public async Task<bool> RecordGatePromptSuccessAsync(
        Guid flowId,
        string providerMessageId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var updated = await context.RevealFlows
            .Where(r => r.Id == flowId && r.State == RevealFlowState.PreparingGatePrompt
                && r.GatePromptStatus == RevealGatePromptStatus.Attempting)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.State, RevealFlowState.AwaitingPostback)
                .SetProperty(r => r.GatePromptStatus, RevealGatePromptStatus.Succeeded)
                .SetProperty(r => r.GatePromptProviderMessageId, providerMessageId)
                .SetProperty(r => r.UpdatedAtUtc, nowUtc), cancellationToken);
        return updated == 1;
    }

    public async Task<bool> RecordGatePromptTerminalAsync(
        Guid flowId,
        string failureCode,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var updated = await context.RevealFlows
            .Where(r => r.Id == flowId && r.State == RevealFlowState.PreparingGatePrompt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.State, RevealFlowState.TerminalFailed)
                .SetProperty(r => r.GatePromptStatus, RevealGatePromptStatus.TerminalFailed)
                .SetProperty(r => r.GatePromptFailureCode, failureCode)
                .SetProperty(r => r.FailureCode, failureCode)
                .SetProperty(r => r.UpdatedAtUtc, nowUtc), cancellationToken);
        return updated == 1;
    }

    public async Task<bool> RecordGatePromptUncertainAsync(
        Guid flowId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var updated = await context.RevealFlows
            .Where(r => r.Id == flowId && r.State == RevealFlowState.PreparingGatePrompt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.State, RevealFlowState.Uncertain)
                .SetProperty(r => r.GatePromptStatus, RevealGatePromptStatus.Uncertain)
                .SetProperty(r => r.GatePromptFailureCode, RevealFlowFailures.GatePromptUncertain)
                .SetProperty(r => r.FailureCode, RevealFlowFailures.GatePromptUncertain)
                .SetProperty(r => r.UpdatedAtUtc, nowUtc), cancellationToken);
        return updated == 1;
    }

    public async Task<bool> TryMarkRevealingAsync(
        Guid flowId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        // Single-reveal authority: only AwaitingPostback (normal) or PreparingGatePrompt
        // (ambiguous gate-prompt rescue — the tap proves delivery) may enter Revealing.
        var updated = await context.RevealFlows
            .Where(r => r.Id == flowId && (r.State == RevealFlowState.AwaitingPostback
                || r.State == RevealFlowState.PreparingGatePrompt))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.State, RevealFlowState.Revealing)
                .SetProperty(r => r.RevealStatus, RevealSendStatus.Attempting)
                .SetProperty(r => r.RevealAttemptedAtUtc, nowUtc)
                .SetProperty(r => r.UpdatedAtUtc, nowUtc), cancellationToken);
        return updated == 1;
    }

    public async Task<bool> RecordRevealSuccessAsync(
        Guid flowId,
        string providerMessageId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var updated = await context.RevealFlows
            .Where(r => r.Id == flowId && r.State == RevealFlowState.Revealing
                && r.RevealStatus == RevealSendStatus.Attempting)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.State, RevealFlowState.Revealed)
                .SetProperty(r => r.RevealStatus, RevealSendStatus.Succeeded)
                .SetProperty(r => r.RevealProviderMessageId, providerMessageId)
                .SetProperty(r => r.RevealCompletedAtUtc, nowUtc)
                .SetProperty(r => r.UpdatedAtUtc, nowUtc), cancellationToken);
        return updated == 1;
    }

    public async Task<bool> RecordRevealTerminalAsync(
        Guid flowId,
        string failureCode,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var updated = await context.RevealFlows
            .Where(r => r.Id == flowId && r.State == RevealFlowState.Revealing)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.State, RevealFlowState.TerminalFailed)
                .SetProperty(r => r.RevealStatus, RevealSendStatus.TerminalFailed)
                .SetProperty(r => r.RevealFailureCode, failureCode)
                .SetProperty(r => r.FailureCode, failureCode)
                .SetProperty(r => r.UpdatedAtUtc, nowUtc), cancellationToken);
        return updated == 1;
    }

    public async Task<bool> RecordRevealUncertainAsync(
        Guid flowId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var updated = await context.RevealFlows
            .Where(r => r.Id == flowId && r.State == RevealFlowState.Revealing)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.State, RevealFlowState.Uncertain)
                .SetProperty(r => r.RevealStatus, RevealSendStatus.Uncertain)
                .SetProperty(r => r.RevealFailureCode, RevealFlowFailures.RevealUncertain)
                .SetProperty(r => r.FailureCode, RevealFlowFailures.RevealUncertain)
                .SetProperty(r => r.UpdatedAtUtc, nowUtc), cancellationToken);
        return updated == 1;
    }

    public async Task<bool> RecordFollowCheckAsync(
        Guid flowId,
        FollowState state,
        FollowStateUnavailableReason? unavailableReason,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var updated = await context.RevealFlows
            .Where(r => r.Id == flowId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.LastFollowState, state)
                .SetProperty(r => r.LastFollowUnavailableReason, unavailableReason)
                .SetProperty(r => r.LastFollowCheckAtUtc, nowUtc)
                .SetProperty(r => r.UpdatedAtUtc, nowUtc), cancellationToken);
        return updated == 1;
    }

    public async Task<bool> TryTerminalAsync(
        Guid flowId,
        RevealFlowState terminalState,
        string failureCode,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var updated = await context.RevealFlows
            .Where(r => r.Id == flowId
                && r.State != RevealFlowState.Revealed
                && r.State != RevealFlowState.Expired
                && r.State != RevealFlowState.TerminalFailed
                && r.State != RevealFlowState.Uncertain)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.State, terminalState)
                .SetProperty(r => r.FailureCode, failureCode)
                .SetProperty(r => r.UpdatedAtUtc, nowUtc), cancellationToken);
        return updated == 1;
    }

    /// <summary>Npgsql reports unique-index races as SQLSTATE 23505 ("duplicate key").</summary>
    private static bool IsUniqueViolation(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("23505", StringComparison.Ordinal)
                || current.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static RevealFlowSnapshot ToSnapshot(RevealFlowRow row) => new(
        row.Id,
        row.WorkspaceId,
        row.ConnectedAccountId,
        row.ProviderCommentId,
        row.ParticipantIGSID,
        row.State,
        new RevealFlowContent(
            row.OpeningPrivateReplyText,
            row.GatePromptText,
            row.PostbackButtonTitle,
            row.FollowUrl,
            row.FollowButtonTitle,
            row.RevealText),
        row.FollowGateMode,
        row.OpeningPrivateReplyMessageId,
        row.OpeningPrivateReplyRecipientId,
        row.LastUserMessageAtUtc,
        row.GatePromptStatus,
        row.GatePromptProviderMessageId,
        row.GatePromptAttemptedAtUtc,
        row.CorrelationTokenHash,
        row.RevealStatus,
        row.RevealProviderMessageId,
        row.RevealAttemptedAtUtc,
        row.RevealCompletedAtUtc,
        row.LastFollowState,
        row.LastFollowUnavailableReason,
        row.FailureCode,
        row.CreatedAtUtc,
        row.UpdatedAtUtc);
}
