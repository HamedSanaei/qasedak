using Qasedak.Modules.Instagram.Application.HistorySync;

namespace Qasedak.Modules.Instagram.Infrastructure.HistorySync;

/// <summary>Instagram-owned durable sync-operation row (M13-013 §66). Never holds
/// tokens, raw provider bodies or message text; the cursor is a bounded opaque
/// provider cursor component only.</summary>
public sealed class ProviderSyncOperationRow
{
    public Guid OperationId { get; set; }

    public Guid ConnectedAccountId { get; set; }

    public Guid WorkspaceId { get; set; }

    public SyncOperationKind Kind { get; set; }

    public SyncOperationStatus Status { get; set; }

    /// <summary>Continuation stage (0 = first job); incremented per checkpoint.</summary>
    public int Stage { get; set; }

    public int ConversationsObserved { get; set; }

    public int MessageIdsObserved { get; set; }

    public int DetailsFetched { get; set; }

    public int MessagesImported { get; set; }

    public int Duplicates { get; set; }

    public int Unsupported { get; set; }

    public int HistoryWindowLimited { get; set; }

    public int RateLimited { get; set; }

    public string? FailureCategory { get; set; }

    public string? NextProviderCursor { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ProviderSyncOperation ToContract() => new(
        OperationId,
        ConnectedAccountId,
        WorkspaceId,
        Kind,
        Status,
        Stage,
        ConversationsObserved,
        MessageIdsObserved,
        DetailsFetched,
        MessagesImported,
        Duplicates,
        Unsupported,
        HistoryWindowLimited,
        RateLimited,
        FailureCategory,
        NextProviderCursor,
        CreatedAtUtc,
        StartedAtUtc,
        CompletedAtUtc,
        UpdatedAtUtc);
}
