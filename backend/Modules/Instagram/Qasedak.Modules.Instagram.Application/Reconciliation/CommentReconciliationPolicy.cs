using System.Text.Json;

namespace Qasedak.Modules.Instagram.Application.Reconciliation;

/// <summary>
/// Central bounded comment-reconciliation policy (M13-013 Phase A). One source of
/// truth for sweep ceilings, cadence and the M13-004 job contract — no magic
/// constants scattered across adapters/handlers/tests. Conservative bounded values,
/// never provider maxima.
///
/// Verified provider contract (2026-09-07): comments edge returns at most 50 comments
/// per query in reverse-chronological order with cursor pagination; comments cannot be
/// filtered by timestamp; ad/boosted and post-broadcast Live comment surfaces are NOT
/// available through Instagram Login (recorded in the coverage matrix and provider
/// contract doc — reconciliation simply never targets those surfaces).
/// </summary>
public static class CommentReconciliationPolicy
{
    /// <summary>M13-004 work type for the per-account recurring sweep.</summary>
    public const string JobType = "instagram.comment-reconciliation";

    public const int PayloadVersion = 1;

    public const int DefaultMaxAttempts = 5;

    /// <summary>Recurring sweep cadence for each eligible active account.</summary>
    public static readonly TimeSpan Cadence = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Hard per-sweep ceiling of media objects scanned (specific ids + AnySource recent).
    /// </summary>
    public const int MaxMediaPerSweep = 30;

    /// <summary>Hard per-media page ceiling inside one sweep.</summary>
    public const int MaxCommentPagesPerMedia = 3;

    /// <summary>Hard per-media comment ceiling inside one sweep.</summary>
    public const int MaxCommentsPerMedia = 150;

    /// <summary>Hard total comment ceiling for one sweep.</summary>
    public const int MaxCommentsPerSweep = 500;

    /// <summary>Maximum raw provider cursor component length accepted for the next page.</summary>
    public const int MaxProviderCursorLength = 512;

    /// <summary>Provider page size for comment listing (verified max is 50; bounded 25).</summary>
    public const int PageSize = 25;

    /// <summary>
    /// Conservative reconciliation horizon: comments created more than this long ago
    /// are never dispatched by a sweep. 14 days covers the Private Reply 7-day window
    /// (M13-009 remains the expiry authority — this is a traversal bound, not a policy
    /// duplicate) plus margin; actions whose provider semantics allow older content
    /// are still evaluated through the existing execution boundaries when a younger
    /// comment arrives. Older pages are still bounded by the page caps above.
    /// </summary>
    public static readonly TimeSpan MaxCommentAge = TimeSpan.FromDays(14);

    /// <summary>Verified comments-edge fields (IG Comment reference, 2026-09-07).</summary>
    public const string Fields = "id,from{id,username},text,timestamp,media{id},parent_id,hidden";

    /// <summary>Job payload: identifiers only (ConnectedAccountId); never a token, text,
    /// comment body or provider cursor URL.</summary>
    public static string Payload(Guid connectedAccountId) =>
        JsonSerializer.Serialize(new { accountId = connectedAccountId });

    public static Guid? ParseAccountId(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("accountId", out var account) ||
                account.ValueKind != JsonValueKind.String || !Guid.TryParse(account.GetString(), out var accountId))
            {
                return null;
            }

            return accountId;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Occurrence-specific deterministic idempotency key. Each chained occurrence owns a
    /// unique UTC-window key; enqueueing the same key twice (restart races, duplicate
    /// ensure calls) collapses onto one logical job.
    /// </summary>
    public static string IdempotencyKey(Guid accountId, DateTimeOffset dueAtUtc)
    {
        var window = dueAtUtc.ToUnixTimeSeconds() / (long)Cadence.TotalSeconds;
        return $"comment-recon:{accountId:N}:{window}";
    }

    /// <summary>Due time of the next recurring occurrence after a settled sweep.</summary>
    public static DateTimeOffset NextOccurrenceDueAt(DateTimeOffset afterUtc) => afterUtc.Add(Cadence);
}

/// <summary>Low-cardinality sweep outcome counters (M13-013 §83). No identifiers,
/// no text, no token material in labels.</summary>
public sealed record CommentSweepOutcome(
    bool ProviderTrafficAttempted,
    int MediaScanned,
    int CommentPagesScanned,
    int CommentsObserved,
    int CommentsDispatched,
    int Duplicates,
    int SelfSkipped,
    int UnknownAuthorSkipped,
    int UnsupportedSurfaceSkipped,
    bool RateLimited,
    string? FailureCode);
