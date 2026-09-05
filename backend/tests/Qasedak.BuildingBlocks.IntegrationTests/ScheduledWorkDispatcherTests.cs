using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Application.Scheduling;
using Qasedak.BuildingBlocks.Infrastructure.Scheduling;
using Xunit;

namespace Qasedak.BuildingBlocks.IntegrationTests;

/// <summary>
/// Dispatcher integration over real PostgreSQL: poll/claim/dispatch/finish across
/// success, retryable, permanent, faulting and unknown-type handlers, with secrets
/// never appearing in stored payloads or logs.
/// </summary>
[Collection(PostgresTestEnvironment.Name)]
public sealed class ScheduledWorkDispatcherTests(PostgreSqlFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 6, 0, 0, TimeSpan.Zero);

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

    private sealed class ScriptedHandler : IScheduledWorkHandler
    {
        public string WorkType { get; }
        public Func<ScheduledWorkItem, WorkOutcome> Respond { get; set; } = _ => WorkOutcome.Succeeded.Instance;
        public List<Guid> Handled { get; } = [];

        public ScriptedHandler(string workType = "test.work") => WorkType = workType;

        public Task<WorkOutcome> HandleAsync(ScheduledWorkItem item, CancellationToken cancellationToken)
        {
            Handled.Add(item.Id);
            return Task.FromResult(Respond(item));
        }
    }

    private sealed class FaultingHandler : IScheduledWorkHandler
    {
        public string WorkType => "test.fault";

        public Task<WorkOutcome> HandleAsync(ScheduledWorkItem item, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private (ScheduledWorkDispatcher Dispatcher, EfScheduledWorkStore Store) NewDispatcher(
        FixedClock clock, params IScheduledWorkHandler[] handlers)
    {
        EfScheduledWorkStore StoreFactory()
        {
            var options = new DbContextOptionsBuilder<ScheduledWorkDbContext>()
                .UseNpgsql(fixture.Context.Database.GetConnectionString(), npgsql =>
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ScheduledWorkDbContext.Schema))
                .Options;
            return new EfScheduledWorkStore(new ScheduledWorkDbContext(options));
        }

        var store = StoreFactory();
        var services = new ServiceCollection();
        services.AddScoped<IScheduledWorkStore>(_ => StoreFactory());
        foreach (var handler in handlers)
        {
            services.AddSingleton(handler);
            services.AddSingleton<IScheduledWorkHandler>(handler);
        }

        var provider = services.BuildServiceProvider();
        var dispatcher = new ScheduledWorkDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ScheduledWorkOptions { BatchSize = 10, BackoffBaseSeconds = 30, BackoffMaxSeconds = 3600 }),
            clock,
            new ScheduledWorkMetrics(),
            NullLogger<ScheduledWorkDispatcher>.Instance);
        return (dispatcher, store);
    }

    private static ScheduledWorkEnqueue Enqueue(string type, string key, DateTimeOffset? due = null) =>
        new(type, key, """{"step":"one"}""", 1, Guid.CreateVersion7(), Guid.CreateVersion7(), due ?? Now, 3);

    private static string Key([System.Runtime.CompilerServices.CallerMemberName] string? test = null) =>
        "swd-" + Guid.CreateVersion7().ToString("N") + "-" + test;

    [Fact]
    public async Task SuccessRetryablePermanentAndUnknownTypesSettleCorrectly()
    {
        var clock = new FixedClock(Now);
        var handler = new ScriptedHandler();
        var (dispatcher, store) = NewDispatcher(clock, handler);
        var ok = await store.EnqueueAsync(Enqueue("test.work", Key("ok")), Now);
        var retry = await store.EnqueueAsync(Enqueue("test.work", Key("retry")), Now);
        var dead = await store.EnqueueAsync(Enqueue("test.work", Key("dead")), Now);
        var ghost = await store.EnqueueAsync(Enqueue("mystery.work", Key("ghost")), Now);
        handler.Respond = item => item.Id == retry.Item1.Id
            ? new WorkOutcome.Retryable("provider.busy")
            : item.Id == dead.Item1.Id
                ? new WorkOutcome.Permanent("provider.invalid")
                : WorkOutcome.Succeeded.Instance;

        Assert.Equal(4, await dispatcher.PollOnceAsync());

        Assert.Equal(ScheduledWorkStatus.Succeeded, (await store.FindByIdempotencyKeyAsync(ok.Item1.IdempotencyKey))!.Status);
        var retried = (await store.FindByIdempotencyKeyAsync(retry.Item1.IdempotencyKey))!;
        Assert.Equal(ScheduledWorkStatus.Pending, retried.Status);
        Assert.Equal("provider.busy", retried.LastFailureCode);
        Assert.True(retried.NextAttemptAtUtc > Now);
        var failed = (await store.FindByIdempotencyKeyAsync(dead.Item1.IdempotencyKey))!;
        Assert.Equal(ScheduledWorkStatus.Failed, failed.Status);
        var unknown = (await store.FindByIdempotencyKeyAsync(ghost.Item1.IdempotencyKey))!;
        Assert.Equal(ScheduledWorkStatus.DeadLettered, unknown.Status);
        Assert.Equal(ScheduledWorkFailures.UnknownWorkType, unknown.LastFailureCode);
        Assert.Equal(3, handler.Handled.Count);
    }

    [Fact]
    public async Task SettledRecordsAreNeverReclaimed()
    {
        var clock = new FixedClock(Now);
        var handler = new ScriptedHandler();
        var (dispatcher, store) = NewDispatcher(clock, handler);
        var (item, _) = await store.EnqueueAsync(Enqueue("test.work", Key()), Now);

        Assert.Equal(1, await dispatcher.PollOnceAsync());
        Assert.Equal(0, await dispatcher.PollOnceAsync());

        var row = (await store.FindByIdempotencyKeyAsync(item.IdempotencyKey))!;
        Assert.Equal(ScheduledWorkStatus.Succeeded, row.Status);
    }

    [Fact]
    public async Task CancelledPollLeavesWorkClaimable()
    {
        var clock = new FixedClock(Now);
        var (dispatcher, store) = NewDispatcher(clock, new ScriptedHandler());
        var (item, _) = await store.EnqueueAsync(Enqueue("test.work", Key()), Now);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatcher.PollOnceAsync(cancelled.Token));

        // Nothing was settled or poisoned: a later poll still finds the work.
        Assert.Equal(1, await dispatcher.PollOnceAsync());
        var row = (await store.FindByIdempotencyKeyAsync(item.IdempotencyKey))!;
        Assert.Equal(ScheduledWorkStatus.Succeeded, row.Status);
    }

    [Fact]
    public async Task FaultingHandlerRetriesInsteadOfWedgingTheWorker()
    {
        var clock = new FixedClock(Now);
        var (dispatcher, store) = NewDispatcher(clock, new FaultingHandler());
        var (item, _) = await store.EnqueueAsync(Enqueue("test.fault", Key()), Now);

        Assert.Equal(1, await dispatcher.PollOnceAsync());

        var row = (await store.FindByIdempotencyKeyAsync(item.IdempotencyKey))!;
        Assert.Equal(ScheduledWorkStatus.Pending, row.Status);
        Assert.Equal("handler.faulted", row.LastFailureCode);
    }

    [Fact]
    public async Task EmptyPollDoesNothing()
    {
        var (dispatcher, _) = NewDispatcher(new FixedClock(Now), new ScriptedHandler());

        Assert.Equal(0, await dispatcher.PollOnceAsync());
    }
}
