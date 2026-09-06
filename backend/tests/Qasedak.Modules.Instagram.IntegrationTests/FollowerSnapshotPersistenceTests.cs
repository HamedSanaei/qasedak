using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Instagram.Application.FollowerSnapshots;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Modules.Instagram.IntegrationTests;

/// <summary>
/// M13-007 follower snapshot persistence against REAL PostgreSQL: migration survival
/// of pre-existing accounts, account/day uniqueness (incl. concurrent writers),
/// provenance precedence (Observed &gt; Derived &gt; Backfilled) under serialized and
/// concurrent writes, same-quality deterministic policies, account isolation, bounded
/// history reads and secret-free rows. No database mocking, no live Meta calls.
/// Every test uses its own fresh account IDs because the collection database is
/// shared with the other integration tests (repository convention).
/// </summary>
[Collection(PostgresTestEnvironment.Name)]
public sealed class FollowerSnapshotPersistenceTests(PostgreSqlFixture fixture)
{
    private static readonly DateOnly Day = new(2026, 9, 6);

    private static readonly DateTimeOffset ObservedAt = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private InstagramDbContext NewContext() =>
        new(new DbContextOptionsBuilder<InstagramDbContext>()
            .UseNpgsql(fixture.Context.Database.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", InstagramDbContext.Schema))
            .Options);

    private static FollowerSnapshotRecord Record(
        Guid accountId, DateOnly day, long count, FollowerSnapshotProvenance provenance, DateTimeOffset? observedAt = null) =>
        new(accountId, day, count, provenance, observedAt ?? ObservedAt);

    [Fact]
    public async Task MigrationKeepsExistingAccountRowsAndSnapshotTableWorks()
    {
        // A pre-M13-007 (M13-006) account row must survive the additive migration and
        // be usable for snapshot persistence.
        var accountId = Guid.CreateVersion7();
        var workspaceId = Guid.CreateVersion7();
        await using (var setup = NewContext())
        {
            await setup.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO instagram.connected_accounts
                ("Id", "WorkspaceId", "ProviderUserId", "Path", "Scopes", "Health", "TokenExpiresAtUtc", "ConnectedAtUtc")
                VALUES ({0}, {1}, {2}, 1, 'instagram_business_basic', 1, {3}, {4})
                """,
                accountId, workspaceId, "ig-legacy-" + Guid.NewGuid().ToString("N"), ObservedAt.AddDays(30), ObservedAt.AddDays(-30));
        }

        var store = new EfFollowerSnapshotStore(NewContext());
        var persisted = await store.UpsertAsync(Record(accountId, Day, 5_000, FollowerSnapshotProvenance.Observed));

        Assert.True(persisted);
        var latest = await store.GetLatestAsync(accountId);
        Assert.NotNull(latest);
        Assert.Equal(5_000, latest.FollowerCount);
        Assert.Equal(FollowerSnapshotProvenance.Observed, latest.Provenance);
    }

    [Fact]
    public async Task UniqueIndexRejectsDuplicateAccountDayRows()
    {
        var accountId = Guid.CreateVersion7();
        await using var context = NewContext();
        await context.FollowerSnapshots.AddAsync(new FollowerSnapshotRow(
            Guid.CreateVersion7(), accountId, Day, 10_000, FollowerSnapshotProvenance.Observed, ObservedAt, ObservedAt, ObservedAt));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // A second raw row for the same account/day violates the unique index.
        await context.FollowerSnapshots.AddAsync(new FollowerSnapshotRow(
            Guid.CreateVersion7(), accountId, Day, 9_999, FollowerSnapshotProvenance.Backfilled, ObservedAt, ObservedAt, ObservedAt));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task ConcurrentSameDayWritesProduceExactlyOneLogicalSnapshot()
    {
        var accountId = Guid.CreateVersion7();
        var storeA = new EfFollowerSnapshotStore(NewContext());
        var storeB = new EfFollowerSnapshotStore(NewContext());

        // Two concurrent daily jobs for the same account/day: PostgreSQL serializes
        // them on the conflict; exactly one logical snapshot survives.
        await Task.WhenAll(
            storeA.UpsertAsync(Record(accountId, Day, 10_000, FollowerSnapshotProvenance.Observed, ObservedAt)),
            storeB.UpsertAsync(Record(accountId, Day, 10_000, FollowerSnapshotProvenance.Observed, ObservedAt.AddMinutes(1))));

        await using var verification = NewContext();
        var rows = await verification.FollowerSnapshots.Where(s => s.ConnectedAccountId == accountId && s.SnapshotDateUtc == Day).ToListAsync();
        Assert.Single(rows);
    }

    [Fact]
    public async Task ObservedOverwritesBackfilledButNeverViceVersa()
    {
        var accountId = Guid.CreateVersion7();
        var store = new EfFollowerSnapshotStore(NewContext());

        // Backfilled first, then a direct observation: observed wins.
        Assert.True(await store.UpsertAsync(Record(accountId, Day, 9_950, FollowerSnapshotProvenance.Backfilled)));
        Assert.True(await store.UpsertAsync(Record(accountId, Day, 10_000, FollowerSnapshotProvenance.Observed)));

        await using var context = NewContext();
        var row = await context.FollowerSnapshots.SingleAsync(s => s.ConnectedAccountId == accountId && s.SnapshotDateUtc == Day);
        Assert.Equal(10_000, row.FollowerCount);
        Assert.Equal(FollowerSnapshotProvenance.Observed, row.Provenance);

        // Regression (§32): incoming backfill for an observed day must NOT overwrite it,
        // regardless of write order.
        var refused = await store.UpsertAsync(Record(accountId, Day, 9_950, FollowerSnapshotProvenance.Backfilled));
        Assert.False(refused);
        var after = await context.FollowerSnapshots.AsNoTracking()
            .SingleAsync(s => s.ConnectedAccountId == accountId && s.SnapshotDateUtc == Day);
        Assert.Equal(10_000, after.FollowerCount);
        Assert.Equal(FollowerSnapshotProvenance.Observed, after.Provenance);
    }

    [Fact]
    public async Task DerivedAndBackfilledCannotOverwriteObserved()
    {
        var accountId = Guid.CreateVersion7();
        var store = new EfFollowerSnapshotStore(NewContext());
        await store.UpsertAsync(Record(accountId, Day, 10_000, FollowerSnapshotProvenance.Observed));

        Assert.False(await store.UpsertAsync(Record(accountId, Day, 9_900, FollowerSnapshotProvenance.Derived)));
        Assert.False(await store.UpsertAsync(Record(accountId, Day, 9_800, FollowerSnapshotProvenance.Backfilled)));

        await using var context = NewContext();
        var row = await context.FollowerSnapshots.AsNoTracking()
            .SingleAsync(s => s.ConnectedAccountId == accountId && s.SnapshotDateUtc == Day);
        Assert.Equal(10_000, row.FollowerCount);
        Assert.Equal(FollowerSnapshotProvenance.Observed, row.Provenance);
    }

    [Fact]
    public async Task ConcurrentLowerAndHigherQualityWritesEndWithHigherQuality()
    {
        var accountId = Guid.CreateVersion7();
        var observedStore = new EfFollowerSnapshotStore(NewContext());
        var backfilledStore = new EfFollowerSnapshotStore(NewContext());

        await Task.WhenAll(
            observedStore.UpsertAsync(Record(accountId, Day, 10_000, FollowerSnapshotProvenance.Observed, ObservedAt)),
            backfilledStore.UpsertAsync(Record(accountId, Day, 9_500, FollowerSnapshotProvenance.Backfilled, ObservedAt.AddHours(2))));

        await using var context = NewContext();
        var row = await context.FollowerSnapshots.AsNoTracking()
            .SingleAsync(s => s.ConnectedAccountId == accountId && s.SnapshotDateUtc == Day);
        Assert.Equal(10_000, row.FollowerCount);
        Assert.Equal(FollowerSnapshotProvenance.Observed, row.Provenance);
    }

    [Fact]
    public async Task ObservedSameQualityKeepsFreshestObservationRegardlessOfWriteOrder()
    {
        var accountId = Guid.CreateVersion7();
        var store = new EfFollowerSnapshotStore(NewContext());
        var earlier = ObservedAt;
        var later = ObservedAt.AddMinutes(30);

        // Later write first, then an older observation: the fresher value survives.
        await store.UpsertAsync(Record(accountId, Day, 10_050, FollowerSnapshotProvenance.Observed, later));
        Assert.False(await store.UpsertAsync(Record(accountId, Day, 10_000, FollowerSnapshotProvenance.Observed, earlier)));

        await using var context = NewContext();
        var row = await context.FollowerSnapshots.AsNoTracking()
            .SingleAsync(s => s.ConnectedAccountId == accountId && s.SnapshotDateUtc == Day);
        Assert.Equal(10_050, row.FollowerCount);
        Assert.Equal(later, row.ObservedAtUtc);
    }

    [Fact]
    public async Task SameQualityDerivedAndBackfilledKeepFirstWrite()
    {
        var accountId = Guid.CreateVersion7();
        var store = new EfFollowerSnapshotStore(NewContext());

        Assert.True(await store.UpsertAsync(Record(accountId, Day, 9_900, FollowerSnapshotProvenance.Derived, ObservedAt)));
        Assert.False(await store.UpsertAsync(Record(accountId, Day, 9_950, FollowerSnapshotProvenance.Derived, ObservedAt.AddMinutes(1))));
        Assert.False(await store.UpsertAsync(Record(accountId, Day, 9_800, FollowerSnapshotProvenance.Backfilled, ObservedAt.AddMinutes(2))));

        await using var context = NewContext();
        var row = await context.FollowerSnapshots.AsNoTracking()
            .SingleAsync(s => s.ConnectedAccountId == accountId && s.SnapshotDateUtc == Day);
        Assert.Equal(9_900, row.FollowerCount);
        Assert.Equal(FollowerSnapshotProvenance.Derived, row.Provenance);
    }

    [Fact]
    public async Task SameDayAcrossAccountsRemainsIndependent()
    {
        var accountA = Guid.CreateVersion7();
        var accountB = Guid.CreateVersion7();
        var store = new EfFollowerSnapshotStore(NewContext());

        await store.UpsertAsync(Record(accountA, Day, 10_000, FollowerSnapshotProvenance.Observed));
        await store.UpsertAsync(Record(accountB, Day, 20_000, FollowerSnapshotProvenance.Observed));

        await using var context = NewContext();
        var rows = await context.FollowerSnapshots.AsNoTracking()
            .Where(s => s.ConnectedAccountId == accountA || s.ConnectedAccountId == accountB).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(10_000, rows.Single(r => r.ConnectedAccountId == accountA).FollowerCount);
        Assert.Equal(20_000, rows.Single(r => r.ConnectedAccountId == accountB).FollowerCount);
    }

    [Fact]
    public async Task HistoryIsNewestFirstBoundedAndLatestWorks()
    {
        var accountId = Guid.CreateVersion7();
        var store = new EfFollowerSnapshotStore(NewContext());
        for (var i = 0; i < 5; i++)
        {
            var day = Day.AddDays(-i);
            await store.UpsertAsync(Record(accountId, day, 10_000L - i, FollowerSnapshotProvenance.Observed, ObservedAt.AddDays(-i)));
        }

        var history = await store.ListHistoryAsync(accountId, 3);
        Assert.Equal(3, history.Count);
        Assert.Equal(Day, history[0].SnapshotDateUtc);
        Assert.Equal(Day.AddDays(-1), history[1].SnapshotDateUtc);
        Assert.Equal(Day.AddDays(-2), history[2].SnapshotDateUtc);

        var latest = await store.GetLatestAsync(accountId);
        Assert.NotNull(latest);
        Assert.Equal(Day, latest.SnapshotDateUtc);
        Assert.Equal(10_000, latest.FollowerCount);

        // Isolation: another account has no history.
        var other = Guid.CreateVersion7();
        Assert.Empty(await store.ListHistoryAsync(other, 10));
        Assert.Null(await store.GetLatestAsync(other));
    }

    [Fact]
    public async Task SnapshotRowStoresNoTokenOrProviderBlob()
    {
        var accountId = Guid.CreateVersion7();
        var store = new EfFollowerSnapshotStore(NewContext());
        await store.UpsertAsync(Record(accountId, Day, 10_000, FollowerSnapshotProvenance.Observed));

        await using var context = NewContext();
        var columns = await context.Database.SqlQueryRaw<string>(
            "SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_schema = 'instagram' AND table_name = 'follower_snapshots'")
            .ToListAsync();
        Assert.Equal(
            ["ConnectedAccountId", "CreatedAtUtc", "FollowerCount", "Id", "ObservedAtUtc", "Provenance", "SnapshotDateUtc", "UpdatedAtUtc"],
            columns.OrderBy(c => c).ToArray());

        var row = await context.FollowerSnapshots.AsNoTracking()
            .SingleAsync(s => s.ConnectedAccountId == accountId && s.SnapshotDateUtc == Day);
        Assert.Equal(10_000, row.FollowerCount);
        Assert.Equal(FollowerSnapshotProvenance.Observed, row.Provenance);
        Assert.Equal(ObservedAt, row.ObservedAtUtc);
    }
}
