using Microsoft.EntityFrameworkCore;
using Qasedak.BuildingBlocks.Application.Scheduling;
using Qasedak.BuildingBlocks.Infrastructure.Scheduling;
using Xunit;

namespace Qasedak.BuildingBlocks.IntegrationTests;

/// <summary>
/// Scheduled-work store semantics over real PostgreSQL: unique idempotent enqueue
/// (incl. races), atomic single-winner claims, lease expiry reclaim, retry/backoff
/// progression, terminal states, restart recovery and secret-material refusal.
/// </summary>
[Collection(PostgresTestEnvironment.Name)]
public sealed class ScheduledWorkStoreTests(PostgreSqlFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 6, 0, 0, TimeSpan.Zero);

    /// <summary>Each test starts from an empty jobs table: the collection database is
    /// shared and the fixed clock would otherwise let leftovers get claimed.</summary>
    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<ScheduledWorkDbContext>()
            .UseNpgsql(fixture.Context.Database.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ScheduledWorkDbContext.Schema))
            .Options;
        await using var context = new ScheduledWorkDbContext(options);
        await context.Jobs.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Key([System.Runtime.CompilerServices.CallerMemberName] string? test = null) =>
        "sw-" + Guid.CreateVersion7().ToString("N") + "-" + test;

    private static ScheduledWorkEnqueue Enqueue(string key, DateTimeOffset? due = null, int maxAttempts = 3) =>
        new("test.work", key, """{"attempt":"first"}""", 1, Guid.CreateVersion7(), Guid.CreateVersion7(),
            due ?? Now, maxAttempts);

    private static ScheduledWorkEnqueue EnqueuePayload(string key, string payload) =>
        new("test.work", key, payload, 1, Guid.CreateVersion7(), Guid.CreateVersion7(), Now, 3);

    private EfScheduledWorkStore NewStore()
    {
        var options = new DbContextOptionsBuilder<ScheduledWorkDbContext>()
            .UseNpgsql(fixture.Context.Database.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ScheduledWorkDbContext.Schema))
            .Options;
        return new EfScheduledWorkStore(new ScheduledWorkDbContext(options));
    }

    [Fact]
    public async Task DuplicateIdempotencyKeyReturnsTheSameLogicalJob()
    {
        var store = NewStore();
        var key = Key();

        var (first, duplicateFirst) = await store.EnqueueAsync(Enqueue(key), Now);
        var (second, duplicateSecond) = await store.EnqueueAsync(Enqueue(key), Now.AddMinutes(1));

        Assert.False(duplicateFirst);
        Assert.True(duplicateSecond);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(ScheduledWorkStatus.Pending, second.Status);
    }

    [Fact]
    public async Task ConcurrentEnqueuesCollapseOntoOneLogicalJob()
    {
        var store = NewStore();
        var key = Key();
        var barrier = new TaskCompletionSource();

        var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            await barrier.Task;
            // A separate store instance per racer mimics separate workers honestly.
            return await NewStore().EnqueueAsync(Enqueue(key), Now);
        })).ToArray();
        barrier.SetResult();
        var outcomes = await Task.WhenAll(tasks);

        Assert.Single(outcomes.Select(o => o.Item.Id).Distinct());
        Assert.Equal(3, outcomes.Count(o => o.Duplicate));
    }

    [Fact]
    public async Task TwoWorkersClaimDisjointSets()
    {
        var keyPrefix = Key();
        var first = NewStore();
        for (var i = 0; i < 4; i++)
        {
            await first.EnqueueAsync(Enqueue(keyPrefix + "-" + i), Now);
        }

        var barrier = new TaskCompletionSource();
        var firstWorker = Task.Run(async () =>
        {
            await barrier.Task;
            return await NewStore().ClaimDueAsync("worker-1", 10, TimeSpan.FromMinutes(5), Now);
        });
        var secondWorker = Task.Run(async () =>
        {
            await barrier.Task;
            return await NewStore().ClaimDueAsync("worker-2", 10, TimeSpan.FromMinutes(5), Now);
        });
        barrier.SetResult();
        var results = await Task.WhenAll(firstWorker, secondWorker);

        var all = results[0].Concat(results[1]).ToList();
        Assert.Equal(4, all.Count);
        Assert.Equal(4, all.Select(i => i.Id).Distinct().Count());
        Assert.All(all, item => Assert.Equal(ScheduledWorkStatus.Claimed, item.Status));
        Assert.All(all, item => Assert.Equal(1, item.Attempts));
    }

    [Fact]
    public async Task FutureDueRecordsAreNotClaimed()
    {
        var store = NewStore();
        await store.EnqueueAsync(Enqueue(Key(), Now.AddHours(1)), Now);

        var claimed = await store.ClaimDueAsync("worker-1", 10, TimeSpan.FromMinutes(5), Now);

        Assert.Empty(claimed);
    }

    [Fact]
    public async Task ExpiredLeaseIsReclaimedByAnotherWorker()
    {
        var store = NewStore();
        var (item, _) = await store.EnqueueAsync(Enqueue(Key()), Now);

        var crashed = await store.ClaimDueAsync("crashed-worker", 10, TimeSpan.FromSeconds(30), Now);
        Assert.Single(crashed);

        // Lease still held: nobody else may take it.
        Assert.Empty(await store.ClaimDueAsync("worker-2", 10, TimeSpan.FromMinutes(5), Now.AddSeconds(10)));

        // After expiry a healthy worker reclaims the same record with a new lease.
        var reclaimed = await store.ClaimDueAsync("worker-2", 10, TimeSpan.FromMinutes(5), Now.AddMinutes(2));
        var job = Assert.Single(reclaimed);
        Assert.Equal(item.Id, job.Id);
        Assert.Equal("worker-2", job.LeaseOwner);
        Assert.Equal(2, job.Attempts);
    }

    [Fact]
    public async Task RetryableFailureReschedulesWithBackoffUntilDeadLetter()
    {
        var store = NewStore();
        var (item, _) = await store.EnqueueAsync(Enqueue(Key(), maxAttempts: 2), Now);

        var first = Assert.Single(await store.ClaimDueAsync("w", 10, TimeSpan.FromMinutes(5), Now));
        await store.FailAsync(first.Id, "w", new WorkOutcome.Retryable("provider.transient"),
            ScheduledWorkBackoff.NextAttemptAt(Now, first.Attempts, 30, 3600), Now);
        var afterRetry = (await store.FindByIdempotencyKeyAsync(item.IdempotencyKey))!;
        Assert.Equal(ScheduledWorkStatus.Pending, afterRetry.Status);
        Assert.Equal("provider.transient", afterRetry.LastFailureCode);
        Assert.True(afterRetry.NextAttemptAtUtc > Now);

        // Second attempt exhausts the budget of 2: dead letter, never silently dropped.
        var second = Assert.Single(await store.ClaimDueAsync("w", 10, TimeSpan.FromMinutes(5), afterRetry.NextAttemptAtUtc));
        await store.FailAsync(second.Id, "w", new WorkOutcome.Retryable("provider.transient"),
            ScheduledWorkBackoff.NextAttemptAt(afterRetry.NextAttemptAtUtc, second.Attempts, 30, 3600), afterRetry.NextAttemptAtUtc);
        var terminal = (await store.FindByIdempotencyKeyAsync(item.IdempotencyKey))!;
        Assert.Equal(ScheduledWorkStatus.DeadLettered, terminal.Status);
        Assert.NotNull(terminal.FinishedAtUtc);
    }

    [Fact]
    public async Task PermanentFailureAndCompletionAreTerminal()
    {
        var store = NewStore();
        var (failed, _) = await store.EnqueueAsync(Enqueue(Key("failed")), Now);
        var (done, _) = await store.EnqueueAsync(Enqueue(Key("done")), Now);
        var (pending, _) = await store.EnqueueAsync(Enqueue(Key("pending")), Now);

        var claimed = await store.ClaimDueAsync("w", 10, TimeSpan.FromSeconds(30), Now);
        Assert.Equal(3, claimed.Count);
        await store.FailAsync(failed.Id, "w", new WorkOutcome.Permanent("provider.invalid"), Now, Now);
        var failedRow = (await store.FindByIdempotencyKeyAsync(failed.IdempotencyKey))!;
        Assert.Equal(ScheduledWorkStatus.Failed, failedRow.Status);
        Assert.Equal("provider.invalid", failedRow.LastFailureCode);
        Assert.NotNull(failedRow.FinishedAtUtc);

        var claimedDone = Assert.Single(claimed, i => i.Id == done.Id);
        Assert.Equal(done.Id, claimedDone.Id);
        await store.CompleteAsync(done.Id, "w", Now);
        var doneRow = (await store.FindByIdempotencyKeyAsync(done.IdempotencyKey))!;
        Assert.Equal(ScheduledWorkStatus.Succeeded, doneRow.Status);
        Assert.NotNull(doneRow.FinishedAtUtc);

        // A fresh store instance (restart recovery): the untouched record's lease
        // has expired, so it is claimable again.
        var restarted = NewStore();
        var recovered = Assert.Single(await restarted.ClaimDueAsync("w", 10, TimeSpan.FromMinutes(5), Now.AddMinutes(2)));
        Assert.Equal(pending.Id, recovered.Id);
    }

    [Fact]
    public async Task CancelledRecordsAreNeverClaimed()
    {
        var store = NewStore();
        var (item, _) = await store.EnqueueAsync(Enqueue(Key()), Now);
        await store.CancelAsync(item.Id, Now);

        Assert.Empty(await store.ClaimDueAsync("w", 10, TimeSpan.FromMinutes(5), Now));
        var row = (await store.FindByIdempotencyKeyAsync(item.IdempotencyKey))!;
        Assert.Equal(ScheduledWorkStatus.Cancelled, row.Status);
    }

    [Fact]
    public async Task LostLeaseOperationsAreRejected()
    {
        var store = NewStore();
        var (item, _) = await store.EnqueueAsync(Enqueue(Key()), Now);
        await store.ClaimDueAsync("owner-a", 10, TimeSpan.FromMinutes(5), Now);

        Assert.False(await store.RenewLeaseAsync(item.Id, "owner-b", TimeSpan.FromMinutes(5), Now));
        await Assert.ThrowsAsync<ScheduledWorkException>(() => store.CompleteAsync(item.Id, "owner-b", Now));
        await Assert.ThrowsAsync<ScheduledWorkException>(() =>
            store.FailAsync(item.Id, "owner-b", new WorkOutcome.Retryable("x"), Now, Now));
    }

    [Fact]
    public async Task SecretShapedPayloadsAreRejectedAtEnqueue()
    {
        var store = NewStore();

        await Assert.ThrowsAsync<ScheduledWorkException>(() => store.EnqueueAsync(
            EnqueuePayload(Key(), "{\"token\":\"EAACEdEose0secret\"}"), Now));
        await Assert.ThrowsAsync<ScheduledWorkException>(() => store.EnqueueAsync(
            EnqueuePayload(Key(), "{\"k\":\"client_secret=abc\"}"), Now));
        await Assert.ThrowsAsync<ScheduledWorkException>(() => store.EnqueueAsync(
            EnqueuePayload(Key(), "{\"k\":\"access_token=abc\"}"), Now));
    }
}
