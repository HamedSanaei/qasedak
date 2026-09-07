using System.Text.Json;

namespace Qasedak.Modules.Instagram.Application.HistorySync;

/// <summary>
/// One conservative history-sync policy (M13-013 §43): every traversal bound lives
/// here. Derived from the verified provider contract (2026-09-07): message DETAIL is
/// only retrievable for the 20 most recent messages of a conversation; conversations in
/// the Requests folder inactive for 30+ days are not returned; shares expose a URL
/// only. No unbounded traversal, no concurrency beyond <see cref="MaxConcurrentMessageDetailCalls"/>.
/// </summary>
public static class ConversationHistoryPolicy
{
    /// <summary>M13-004 work type for the durable per-account sync operation.</summary>
    public const string JobType = "instagram.conversation-history-sync";

    public const int PayloadVersion = 1;

    public const int DefaultMaxAttempts = 5;

    /// <summary>Hard per-operation conversation-list page ceiling.</summary>
    public const int MaxConversationPagesPerOperation = 5;

    /// <summary>Hard per-operation conversation ceiling.</summary>
    public const int MaxConversationsPerOperation = 40;

    /// <summary>Bounded message-id rows read per conversation (the provider may expose
    /// many; we never detail more than the newest 20 anyway).</summary>
    public const int MaxMessageIdsPerConversation = 100;

    /// <summary>
    /// Provider-supported detail window: at most the 20 most recent messages of a
    /// conversation get detail calls (§38/§89). Older ids are never called merely to
    /// receive errors; older ids observed are counted as history-window-limited.
    /// </summary>
    public const int MaxMessageDetailCallsPerConversation = 20;

    /// <summary>Hard per-operation message-detail ceiling.</summary>
    public const int MaxMessageDetailCallsPerOperation = 200;

    /// <summary>Maximum raw provider cursor component length accepted.</summary>
    public const int MaxProviderCursorLength = 512;

    /// <summary>Bounded message-detail concurrency (never Task.WhenAll of everything).</summary>
    public const int MaxConcurrentMessageDetailCalls = 4;

    /// <summary>Conversation-list page size (provider default 25; bounded 25).</summary>
    public const int PageSize = 25;

    /// <summary>Share URL length bound (defensive; never a provider detail contract).</summary>
    public const int MaxShareUrlLength = 2048;

    /// <summary>Message body length bound (mirrors the Conversations display cap).</summary>
    public const int MaxBodyLength = 1000;

    /// <summary>Job payload: local identifiers only; no token, no raw next URL, no text.</summary>
    public static string Payload(Guid operationId, Guid connectedAccountId) =>
        JsonSerializer.Serialize(new { operationId, accountId = connectedAccountId });

    public static (Guid OperationId, Guid AccountId)? ParsePayload(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("operationId", out var op) || op.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("accountId", out var account) || account.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(op.GetString(), out var operationId) ||
                !Guid.TryParse(account.GetString(), out var accountId))
            {
                return null;
            }

            return (operationId, accountId);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>One logical job per operation (first occurrence); restarts collapse.</summary>
    public static string OperationIdempotencyKey(Guid operationId) => $"convsync:op:{operationId:N}";

    /// <summary>Deterministic continuation key per checkpoint stage.</summary>
    public static string ContinuationIdempotencyKey(Guid operationId, int stage) => $"convsync:op:{operationId:N}:c{stage}";
}

/// <summary>Kinds of durable sync operations (M13-013 §66/§71).</summary>
public enum SyncOperationKind
{
    /// <summary>Bounded catch-up scheduled after connection (and bootstrapped for
    /// pre-existing accounts).</summary>
    Initial = 1,

    /// <summary>Exact-account authorized manual resync.</summary>
    Manual = 2,

    /// <summary>Bounded repeat/incremental sync (future cadence; same idempotent import).</summary>
    Incremental = 3,
}

/// <summary>Truthful operation statuses — never "FullySynced"/"CompleteHistory" (§77/§116).</summary>
public enum SyncOperationStatus
{
    Queued = 1,

    Running = 2,

    /// <summary>Traversal finished within the verified provider limits; coverage is
    /// bounded by the 20-detail window and Requests-30-day omission by contract.</summary>
    CompletedWithinProviderLimits = 3,

    /// <summary>Permanent failure (permission/auth loss, malformed state).</summary>
    Failed = 4,

    /// <summary>Rate limited/transient: the operation paused and will retry.</summary>
    RateLimitedRetrying = 5,
}

/// <summary>One durable sync operation row (Instagram-owned persistence).</summary>
public sealed record ProviderSyncOperation(
    Guid OperationId,
    Guid ConnectedAccountId,
    Guid WorkspaceId,
    SyncOperationKind Kind,
    SyncOperationStatus Status,
    int Stage,
    int ConversationsObserved,
    int MessageIdsObserved,
    int DetailsFetched,
    int MessagesImported,
    int Duplicates,
    int Unsupported,
    int HistoryWindowLimited,
    int RateLimited,
    string? FailureCategory,
    string? NextProviderCursor,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset UpdatedAtUtc);
