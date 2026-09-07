using System.Diagnostics.Metrics;

namespace Qasedak.Modules.Instagram.Application.HistorySync;

/// <summary>
/// Instagram-owned durable sync-operation persistence (M13-013 §66/§72/§73). Holds
/// operation lifecycle, counters and the bounded provider cursor checkpoint for
/// continuation jobs. NEVER holds tokens, raw provider bodies or message text.
/// Conversations and Automations own their own persistence — Instagram never writes
/// their schemas.
/// </summary>
public interface IProviderSyncOperationStore
{
    /// <summary>Creates the operation row, or returns the EXISTING active operation when
    /// one is already Queued/Running for the same exact account+kind (DB-enforced
    /// coalescing — one active operation per account/kind). Returns null when the
    /// creation raced and lost (caller reloads).</summary>
    Task<ProviderSyncOperation?> CreateOrGetActiveAsync(
        Guid operationId,
        Guid connectedAccountId,
        Guid workspaceId,
        SyncOperationKind kind,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<ProviderSyncOperation?> FindByIdAsync(Guid operationId, CancellationToken cancellationToken = default);

    /// <summary>Atomically transitions Queued → Running (false when not claimable).</summary>
    Task<bool> TryStartAsync(Guid operationId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Checkpoint: cursor + counters for continuation stages.</summary>
    Task CheckpointAsync(
        Guid operationId, int stage, string? nextCursor, ProviderSyncCounters counters, DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(Guid operationId, ProviderSyncCounters counters, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task FailAsync(Guid operationId, string failureCategory, bool transient, ProviderSyncCounters counters, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderSyncOperation>> ListRecentAsync(Guid connectedAccountId, int limit, CancellationToken cancellationToken = default);
}

/// <summary>Mutable low-cardinality counters accumulated by one operation (a single
/// stage accumulates them sequentially; persistence snapshots values).</summary>
public sealed class ProviderSyncCounters
{
    public int ConversationsObserved { get; set; }

    public int MessageIdsObserved { get; set; }

    public int DetailsFetched { get; set; }

    public int MessagesImported { get; set; }

    public int Duplicates { get; set; }

    public int Unsupported { get; set; }

    public int HistoryWindowLimited { get; set; }

    public int RateLimited { get; set; }

    /// <summary>Thread-safe increment used from bounded parallel detail calls.</summary>
    public void IncrementHistoryWindowLimited()
    {
        lock (_gate)
        {
            HistoryWindowLimited++;
        }
    }

    private readonly object _gate = new();

    public ProviderSyncCounters()
    {
    }

    public ProviderSyncCounters(
        int conversationsObserved, int messageIdsObserved, int detailsFetched, int messagesImported,
        int duplicates, int unsupported, int historyWindowLimited, int rateLimited)
    {
        ConversationsObserved = conversationsObserved;
        MessageIdsObserved = messageIdsObserved;
        DetailsFetched = detailsFetched;
        MessagesImported = messagesImported;
        Duplicates = duplicates;
        Unsupported = unsupported;
        HistoryWindowLimited = historyWindowLimited;
        RateLimited = rateLimited;
    }
}

/// <summary>Low-cardinality sync observability (M13-013 §83). Labels never contain
/// identifiers, text or token material.</summary>
public sealed class ConversationSyncMetrics
{
    private static readonly Meter Meter = new("Qasedak.Instagram.ConversationHistorySync", "1.0.0");

    private readonly Counter<long> _operations = Meter.CreateCounter<long>("qasedak.instagram.conversation_history_sync.operations", "operations");

    private readonly Counter<long> _messages = Meter.CreateCounter<long>("qasedak.instagram.conversation_history_sync.messages", "messages");

    public void OperationCompleted(string kind, string outcome, int conversations, int details) =>
        _operations.Add(1,
            new KeyValuePair<string, object?>("kind", kind),
            new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("conversations", conversations),
            new KeyValuePair<string, object?>("details", details));

    public void Messages(int imported, int duplicates, int unsupported, int windowLimited)
    {
        if (imported > 0)
        {
            _messages.Add(imported, new KeyValuePair<string, object?>("kind", "imported"));
        }

        if (duplicates > 0)
        {
            _messages.Add(duplicates, new KeyValuePair<string, object?>("kind", "duplicates"));
        }

        if (unsupported > 0)
        {
            _messages.Add(unsupported, new KeyValuePair<string, object?>("kind", "unsupported"));
        }

        if (windowLimited > 0)
        {
            _messages.Add(windowLimited, new KeyValuePair<string, object?>("kind", "windowLimited"));
        }
    }
}
