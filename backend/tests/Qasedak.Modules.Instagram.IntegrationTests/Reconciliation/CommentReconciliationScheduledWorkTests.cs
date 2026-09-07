using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Application.Scheduling;
using Qasedak.BuildingBlocks.Infrastructure.Scheduling;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.Reconciliation;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Qasedak.Modules.Instagram.Infrastructure.Reconciliation;
using Xunit;

namespace Qasedak.Modules.Instagram.IntegrationTests.Reconciliation;

/// <summary>
/// M13-013 §87 job mechanics over real PostgreSQL: one active occurrence per account,
/// idempotent duplicate ensure, restart-safe chaining (no overlapping sweeps), the
/// DB-only existing-account bootstrap (zero provider I/O by construction), secret-free
/// payloads and permanent-outcome chain stops. Provider sweeps are scripted; the
/// scheduled-work store and account persistence are real.
/// </summary>
[Collection(PostgresTestEnvironment.Name)]
public sealed class CommentReconciliationScheduledWorkTests(PostgreSqlFixture fixture) : IAsyncLifetime
{
    /// <summary>The Instagram fixture only migrates the Instagram schema; the
    /// scheduled-work schema (M13-004, <c>platform</c>) is migrated into the SAME
    /// physical test DB once per process before any truncation. Only this class
    /// writes scheduled work in the shared DB, so full per-test truncation keeps
    /// chained-occurrence assertions exact.</summary>
    public async Task InitializeAsync()
    {
        if (Interlocked.Exchange(ref _workSchemaReady, 1) == 0)
        {
            var options = new DbContextOptionsBuilder<ScheduledWorkDbContext>()
                .UseNpgsql(fixture.Context.Database.GetConnectionString(), npgsql =>
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ScheduledWorkDbContext.Schema))
                .Options;
            await new ScheduledWorkDbContext(options).Database.MigrateAsync();
        }

        await fixture.Context.Database.ExecuteSqlRawAsync(
            $"DELETE FROM {ScheduledWorkDbContext.Schema}.scheduled_jobs");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly DateTimeOffset Now = new(2026, 9, 7, 6, 0, 0, TimeSpan.Zero);

    private static int _workSchemaReady;

    private async Task<EfScheduledWorkStore> NewWorkStoreAsync()
    {
        var options = new DbContextOptionsBuilder<ScheduledWorkDbContext>()
            .UseNpgsql(fixture.Context.Database.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ScheduledWorkDbContext.Schema))
            .Options;
        return new EfScheduledWorkStore(new ScheduledWorkDbContext(options));
    }

    private async Task<Guid> SeedAccountAsync()
    {
        await using var context = NewInstagramContext();
        var account = ConnectedAccount.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "1784140000" + Guid.NewGuid().ToString("N")[..8],
            ConnectionPath.InstagramLogin, ["instagram_business_basic"],
            DateTimeOffset.UtcNow.AddDays(30), Now);
        await context.Accounts.AddAsync(account);
        await context.SaveChangesAsync();
        return account.Id;
    }

    private InstagramDbContext NewInstagramContext() =>
        new(new DbContextOptionsBuilder<InstagramDbContext>()
            .UseNpgsql(fixture.Context.Database.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", InstagramDbContext.Schema))
            .Options);

    [Fact]
    public async Task BootstrapEnqueuesOneOccurrencePerAccountAndIsIdempotentAcrossRestarts()
    {
        var accountId = await SeedAccountAsync();
        var work = await NewWorkStoreAsync();
        var bootstrap = new CommentReconciliationScheduleBootstrap(NewScopeFactory(work), new FixedClock(Now), NullLogger<CommentReconciliationScheduleBootstrap>.Instance);

        await bootstrap.StartAsync(default);
        await bootstrap.StartAsync(default);

        var due = await work.ClaimDueAsync("test", 100, TimeSpan.FromMinutes(5), Now.AddMinutes(1), default);
        var forAccount = due.Where(j => j.WorkType == CommentReconciliationPolicy.JobType && j.ConnectedAccountId == accountId).ToList();
        Assert.Single(forAccount);
        Assert.Equal(accountId, CommentReconciliationPolicy.ParseAccountId(forAccount[0].PayloadJson));
        // Identifier-only payload: no token, no text, no URL.
        Assert.DoesNotContain("token", forAccount[0].PayloadJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", forAccount[0].PayloadJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandlerChainsNextOccurrenceWithAFreshKeyAndRedeliveryNeverOverlaps()
    {
        var accountId = await SeedAccountAsync();
        var work = await NewWorkStoreAsync();
        var handler = new CommentReconciliationScheduledHandler(
            new StubSweep(new CommentSweepOutcome(true, 1, 1, 1, 1, 0, 0, 0, 0, false, null)),
            work, new FixedClock(Now), new CommentReconciliationMetrics(),
            NullLogger<CommentReconciliationScheduledHandler>.Instance);

        await work.EnqueueAsync(new ScheduledWorkEnqueue(
            CommentReconciliationPolicy.JobType,
            CommentReconciliationPolicy.IdempotencyKey(accountId, Now),
            CommentReconciliationPolicy.Payload(accountId),
            PayloadVersion: 1, ConnectedAccountId: accountId, WorkspaceId: Guid.NewGuid(), DueAtUtc: Now, MaxAttempts: 3), Now);

        // Worker mechanics: claim, handle, settle the occurrence.
        var claimed = await work.ClaimDueAsync("test", 100, TimeSpan.FromMinutes(5), Now, default);
        var job = Assert.Single(claimed, j => j.WorkType == CommentReconciliationPolicy.JobType);
        Assert.IsType<WorkOutcome.Succeeded>(await handler.HandleAsync(job, default));
        await work.CompleteAsync(job.Id, "test", Now.AddMinutes(1), default);

        var chainedDue = Now.Add(CommentReconciliationPolicy.Cadence);
        var chained = await work.ClaimDueAsync("test", 100, TimeSpan.FromMinutes(5), chainedDue, default);
        var chainedJob = Assert.Single(chained, j => j.WorkType == CommentReconciliationPolicy.JobType);
        Assert.NotEqual(job.Id, chainedJob.Id);
        Assert.Equal(CommentReconciliationPolicy.IdempotencyKey(accountId, chainedDue), chainedJob.IdempotencyKey);

        // At-least-once redelivery of the settled job: replay succeeds and chains onto
        // the SAME next key — no second next-occurrence is ever created (a new pending
        // row due now would surface in the claim below; none does).
        Assert.IsType<WorkOutcome.Succeeded>(await handler.HandleAsync(job, default));
        var again = await work.ClaimDueAsync("test", 100, TimeSpan.FromMinutes(5), chainedDue.AddMinutes(1), default);
        Assert.DoesNotContain(again, j => j.WorkType == CommentReconciliationPolicy.JobType);
    }

    [Fact]
    public async Task RateLimitedSweepKeepsTheChainAliveAndReportsRetryable()
    {
        var accountId = await SeedAccountAsync();
        var work = await NewWorkStoreAsync();
        var handler = new CommentReconciliationScheduledHandler(
            new StubSweep(new CommentSweepOutcome(true, 0, 1, 0, 0, 0, 0, 0, 0, true, CommentHistoryFailures.RateLimited)),
            work, new FixedClock(Now), new CommentReconciliationMetrics(),
            NullLogger<CommentReconciliationScheduledHandler>.Instance);

        await work.EnqueueAsync(new ScheduledWorkEnqueue(
            CommentReconciliationPolicy.JobType, "k-" + Guid.NewGuid(), CommentReconciliationPolicy.Payload(accountId),
            1, accountId, Guid.NewGuid(), Now, 3), Now);

        var job = Assert.Single(await work.ClaimDueAsync("test", 100, TimeSpan.FromMinutes(5), Now, default));
        Assert.IsType<WorkOutcome.Retryable>(await handler.HandleAsync(job, default));

        // The rate-limited occurrence still chains the next sweep occurrence (bounded
        // backoff cadence), keyed deterministically for the next due window.
        var chainedKey = CommentReconciliationPolicy.IdempotencyKey(accountId, Now.Add(CommentReconciliationPolicy.Cadence));
        var chained = await work.FindByIdempotencyKeyAsync(chainedKey, default);
        Assert.NotNull(chained);
        Assert.Equal(accountId, chained.ConnectedAccountId);
    }

    [Fact]
    public async Task PermanentSweepFailureStopsTheChainWithoutATightLoop()
    {
        var accountId = await SeedAccountAsync();
        var work = await NewWorkStoreAsync();
        var handler = new CommentReconciliationScheduledHandler(
            new StubSweep(new CommentSweepOutcome(false, 0, 0, 0, 0, 0, 0, 0, 0, false, AccountFailures.TokenMissing)),
            work, new FixedClock(Now), new CommentReconciliationMetrics(),
            NullLogger<CommentReconciliationScheduledHandler>.Instance);

        await work.EnqueueAsync(new ScheduledWorkEnqueue(
            CommentReconciliationPolicy.JobType, "k-" + Guid.NewGuid(), CommentReconciliationPolicy.Payload(accountId),
            1, accountId, Guid.NewGuid(), Now, 3), Now);

        var job = Assert.Single(await work.ClaimDueAsync("test", 100, TimeSpan.FromMinutes(5), Now, default));
        Assert.IsType<WorkOutcome.Permanent>(await handler.HandleAsync(job, default));

        // Permanent outcomes never chain: the next-window key must not exist; the
        // DB-only bootstrap re-establishes the chain on the next host start.
        var chainedKey = CommentReconciliationPolicy.IdempotencyKey(accountId, Now.Add(CommentReconciliationPolicy.Cadence));
        Assert.Null(await work.FindByIdempotencyKeyAsync(chainedKey, default));
    }

    private IServiceScopeFactory NewScopeFactory(EfScheduledWorkStore work)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(new FixedClock(Now));
        services.AddSingleton<IScheduledWorkStore>(work);
        services.AddScoped<IConnectedAccountRepository>(_ => new EfConnectedAccountRepository(NewInstagramContext()));
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private sealed class StubSweep(CommentSweepOutcome outcome) : ICommentSweep
    {
        public int Calls { get; private set; }

        public Task<CommentSweepOutcome> ExecuteAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(outcome);
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
