namespace Qasedak.Modules.Instagram.Application.FollowerSnapshots;

/// <summary>
/// Provenance of one follower snapshot row (M13-007). Explicit first-class contract,
/// never a nullable text note. Ordering encodes quality: higher enum values are
/// higher quality and may replace lower-quality values for the same account/day;
/// lower-quality values NEVER overwrite higher-quality ones (enforced by the
/// PostgreSQL conditional upsert, not by read-before-write code).
/// </summary>
public enum FollowerSnapshotProvenance
{
    /// <summary>Derived from provider series data Qasedak did not directly observe (lowest quality).</summary>
    Backfilled = 0,

    /// <summary>Derived/estimated value (reserved; not written by M13-007).</summary>
    Derived = 1,

    /// <summary>
    /// Directly observed by Qasedak through a verified absolute provider value
    /// (<c>followers_count</c> on the IG Login user node, retrieved 2026-09-06).
    /// Highest quality; never overwritten by lower-quality data.
    /// </summary>
    Observed = 2,
}

/// <summary>One durable follower snapshot (application view; no provider blobs).</summary>
public sealed record FollowerSnapshotRecord(
    Guid AccountId,
    DateOnly SnapshotDateUtc,
    long FollowerCount,
    FollowerSnapshotProvenance Provenance,
    DateTimeOffset ObservedAtUtc);

/// <summary>
/// Instagram-owned durable follower-history store (M13-007). Persistence enforces one
/// snapshot per ConnectedAccount/UTC day and provenance precedence under concurrency
/// (PostgreSQL conditional upsert). No other module owns this data.
/// </summary>
public interface IFollowerSnapshotStore
{
    /// <summary>
    /// Inserts or conditionally replaces the snapshot for the account/day:
    /// incoming quality must be &gt;= existing quality; same-quality Observed values are
    /// replaced only by a fresher observation; same-quality Derived/Backfilled values
    /// keep the existing row (deterministic first-write). Returns true when the row
    /// was inserted or updated.
    /// </summary>
    Task<bool> UpsertAsync(FollowerSnapshotRecord snapshot, CancellationToken cancellationToken = default);

    /// <summary>Latest snapshot for the account (most recent UTC day), or null when none.</summary>
    Task<FollowerSnapshotRecord?> GetLatestAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>Most recent <paramref name="limit"/> snapshots for the account, newest first.</summary>
    Task<IReadOnlyList<FollowerSnapshotRecord>> ListHistoryAsync(
        Guid accountId, int limit, CancellationToken cancellationToken = default);

    /// <summary>Whether a snapshot row already exists for the account/day (bootstrap gating).</summary>
    Task<bool> ExistsForDayAsync(Guid accountId, DateOnly snapshotDateUtc, CancellationToken cancellationToken = default);
}
