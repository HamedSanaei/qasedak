using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Instagram.Application.FollowerSnapshots;

namespace Qasedak.Modules.Instagram.Infrastructure.Persistence;

/// <summary>
/// Instagram-owned follower-snapshot store over <c>instagram.follower_snapshots</c>.
/// Writes go through a PostgreSQL-native conditional upsert that enforces BOTH
/// account/day uniqueness AND provenance precedence atomically — a lower-quality
/// incoming value can never overwrite a higher-quality row, regardless of write
/// order or concurrency (no read-before-write code path).
///
/// Quality order: Observed(2) &gt; Derived(1) &gt; Backfilled(0). Same-quality policy:
/// Observed-vs-Observed keeps the fresher observation (greater ObservedAtUtc);
/// Derived/Backfilled keep the existing row (deterministic first-write).
/// </summary>
public sealed class EfFollowerSnapshotStore(InstagramDbContext context) : IFollowerSnapshotStore
{
    public async Task<bool> UpsertAsync(FollowerSnapshotRecord snapshot, CancellationToken cancellationToken = default)
    {
        var id = Guid.CreateVersion7();
        var createdAt = snapshot.ObservedAtUtc;
        var rows = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO instagram.follower_snapshots
                ("Id", "ConnectedAccountId", "SnapshotDateUtc", "FollowerCount", "Provenance", "ObservedAtUtc", "CreatedAtUtc", "UpdatedAtUtc")
            VALUES ({id}, {snapshot.AccountId}, {snapshot.SnapshotDateUtc}, {snapshot.FollowerCount}, {(int)snapshot.Provenance}, {snapshot.ObservedAtUtc}, {createdAt}, {createdAt})
            ON CONFLICT ("ConnectedAccountId", "SnapshotDateUtc") DO UPDATE SET
                "FollowerCount" = EXCLUDED."FollowerCount",
                "Provenance" = EXCLUDED."Provenance",
                "ObservedAtUtc" = EXCLUDED."ObservedAtUtc",
                "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc"
            WHERE ("follower_snapshots"."Provenance" < EXCLUDED."Provenance")
               OR ("follower_snapshots"."Provenance" = EXCLUDED."Provenance"
                   AND EXCLUDED."Provenance" = {(int)FollowerSnapshotProvenance.Observed}
                   AND EXCLUDED."ObservedAtUtc" > "follower_snapshots"."ObservedAtUtc")
            """,
            cancellationToken);

        return rows > 0;
    }

    public async Task<FollowerSnapshotRecord?> GetLatestAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var row = await context.FollowerSnapshots
            .AsNoTracking()
            .Where(s => s.ConnectedAccountId == accountId)
            .OrderByDescending(s => s.SnapshotDateUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<FollowerSnapshotRecord>> ListHistoryAsync(
        Guid accountId, int limit, CancellationToken cancellationToken = default)
    {
        var rows = await context.FollowerSnapshots
            .AsNoTracking()
            .Where(s => s.ConnectedAccountId == accountId)
            .OrderByDescending(s => s.SnapshotDateUtc)
            .Take(Math.Clamp(limit, 1, 366))
            .ToListAsync(cancellationToken);

        return rows.Select(Map).ToList();
    }

    public Task<bool> ExistsForDayAsync(Guid accountId, DateOnly snapshotDateUtc, CancellationToken cancellationToken = default) =>
        context.FollowerSnapshots
            .AsNoTracking()
            .AnyAsync(s => s.ConnectedAccountId == accountId && s.SnapshotDateUtc == snapshotDateUtc, cancellationToken);

    private static FollowerSnapshotRecord Map(FollowerSnapshotRow row) =>
        new(row.ConnectedAccountId, row.SnapshotDateUtc, row.FollowerCount, row.Provenance, row.ObservedAtUtc);
}
