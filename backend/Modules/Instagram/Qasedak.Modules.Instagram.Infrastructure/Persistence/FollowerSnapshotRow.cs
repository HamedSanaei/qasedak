using Qasedak.Modules.Instagram.Application.FollowerSnapshots;

namespace Qasedak.Modules.Instagram.Infrastructure.Persistence;

/// <summary>
/// Persistence row for <c>instagram.follower_snapshots</c> (M13-007). One row per
/// ConnectedAccount/UTC day (unique index); provenance is a first-class enum column.
/// NEVER stores raw Graph responses, tokens or unrelated analytics blobs.
/// </summary>
public sealed class FollowerSnapshotRow
{
    public FollowerSnapshotRow(
        Guid id,
        Guid connectedAccountId,
        DateOnly snapshotDateUtc,
        long followerCount,
        FollowerSnapshotProvenance provenance,
        DateTimeOffset observedAtUtc,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        Id = id;
        ConnectedAccountId = connectedAccountId;
        SnapshotDateUtc = snapshotDateUtc;
        FollowerCount = followerCount;
        Provenance = provenance;
        ObservedAtUtc = observedAtUtc;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; private init; }

    /// <summary>References instagram.connected_accounts.Id by convention (no FK; history survives disconnect).</summary>
    public Guid ConnectedAccountId { get; private init; }

    /// <summary>The UTC calendar day this snapshot represents (never local time).</summary>
    public DateOnly SnapshotDateUtc { get; private init; }

    public long FollowerCount { get; private init; }

    public FollowerSnapshotProvenance Provenance { get; private init; }

    /// <summary>When the value was directly observed (or the provider series was read).</summary>
    public DateTimeOffset ObservedAtUtc { get; private init; }

    public DateTimeOffset CreatedAtUtc { get; private init; }

    public DateTimeOffset UpdatedAtUtc { get; private init; }
}
