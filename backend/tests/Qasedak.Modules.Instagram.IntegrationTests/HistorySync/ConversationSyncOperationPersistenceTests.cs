using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Instagram.Application.HistorySync;
using Qasedak.Modules.Instagram.Infrastructure.HistorySync;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Modules.Instagram.IntegrationTests.HistorySync;

/// <summary>
/// M13-013 §66/§71/§104 real-PostgreSQL operation store semantics: one active
/// operation per account+kind (DB-enforced coalescing, including the concurrent
/// race), guarded stage transitions, checkpoint continuation, restart of a stale
/// Running stage and truthful terminal states.
/// </summary>
[Collection(PostgresTestEnvironment.Name)]
public sealed class ConversationSyncOperationPersistenceTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 6, 0, 0, TimeSpan.Zero);

    private InstagramDbContext NewContext() =>
        new(new DbContextOptionsBuilder<InstagramDbContext>()
            .UseNpgsql(fixture.Context.Database.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", InstagramDbContext.Schema))
            .Options);

    private static async Task<Guid> SeedAccountAsync(InstagramDbContext context)
    {
        var account = Qasedak.Modules.Instagram.Domain.Accounts.ConnectedAccount.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "1784140000" + Guid.NewGuid().ToString("N")[..8],
            Qasedak.Modules.Instagram.Domain.Accounts.ConnectionPath.InstagramLogin,
            ["instagram_business_basic"], DateTimeOffset.UtcNow.AddDays(30), Now);
        await context.Accounts.AddAsync(account);
        await context.SaveChangesAsync();
        return account.Id;
    }

    [Fact]
    public async Task SecondActiveOperationForSameAccountAndKindCoalesces()
    {
        await using var context = NewContext();
        var accountId = await SeedAccountAsync(context);
        var store = new EfProviderSyncOperationStore(context);
        var workspaceId = Guid.CreateVersion7();

        var first = await store.CreateOrGetActiveAsync(Guid.CreateVersion7(), accountId, workspaceId, SyncOperationKind.Initial, Now);
        var second = await store.CreateOrGetActiveAsync(Guid.CreateVersion7(), accountId, workspaceId, SyncOperationKind.Initial, Now.AddSeconds(1));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.OperationId, second.OperationId);
        Assert.Single(await context.ProviderSyncOperations.Where(o => o.ConnectedAccountId == accountId).ToListAsync());
    }

    [Fact]
    public async Task ManualAndInitialKindsAreIndependentAndCompletedFreesTheSlot()
    {
        await using var context = NewContext();
        var accountId = await SeedAccountAsync(context);
        var store = new EfProviderSyncOperationStore(context);
        var workspaceId = Guid.CreateVersion7();

        var initial = await store.CreateOrGetActiveAsync(Guid.CreateVersion7(), accountId, workspaceId, SyncOperationKind.Initial, Now);
        var manual = await store.CreateOrGetActiveAsync(Guid.CreateVersion7(), accountId, workspaceId, SyncOperationKind.Manual, Now);
        Assert.NotEqual(initial!.OperationId, manual!.OperationId);
        Assert.Equal(2, await context.ProviderSyncOperations.CountAsync(o => o.ConnectedAccountId == accountId));

        await store.CompleteAsync(initial.OperationId, new ProviderSyncCounters(), Now.AddMinutes(5));
        var afterComplete = await store.CreateOrGetActiveAsync(Guid.CreateVersion7(), accountId, workspaceId, SyncOperationKind.Initial, Now.AddMinutes(6));
        Assert.NotNull(afterComplete);
        Assert.NotEqual(initial.OperationId, afterComplete.OperationId);
    }

    [Fact]
    public async Task StaleRunningStageIsReclaimableAfterRestart()
    {
        await using var context = NewContext();
        var accountId = await SeedAccountAsync(context);
        var store = new EfProviderSyncOperationStore(context);
        var operation = await store.CreateOrGetActiveAsync(Guid.CreateVersion7(), accountId, Guid.NewGuid(), SyncOperationKind.Initial, Now);
        Assert.NotNull(operation);

        Assert.True(await store.TryStartAsync(operation.OperationId, Now.AddSeconds(1)));
        // A second worker (post-restart redelivery) reclaims the stale Running stage.
        Assert.True(await store.TryStartAsync(operation.OperationId, Now.AddSeconds(2)));

        var reloaded = await store.FindByIdAsync(operation.OperationId);
        Assert.Equal(SyncOperationStatus.Running, reloaded!.Status);
        Assert.Equal(Now.AddSeconds(2), reloaded.StartedAtUtc);
    }

    [Fact]
    public async Task CheckpointAndCompletionPersistCountersTruthfully()
    {
        await using var context = NewContext();
        var accountId = await SeedAccountAsync(context);
        var store = new EfProviderSyncOperationStore(context);
        var operation = await store.CreateOrGetActiveAsync(Guid.CreateVersion7(), accountId, Guid.NewGuid(), SyncOperationKind.Initial, Now);
        Assert.NotNull(operation);
        await store.TryStartAsync(operation.OperationId, Now);

        var counters = new ProviderSyncCounters(
            conversationsObserved: 7, messageIdsObserved: 40, detailsFetched: 20, messagesImported: 18,
            duplicates: 1, unsupported: 2, historyWindowLimited: 10, rateLimited: 0);
        await store.CheckpointAsync(operation.OperationId, stage: 1, "cursor-abc", counters, Now.AddMinutes(1));

        var checkpointed = await store.FindByIdAsync(operation.OperationId);
        Assert.Equal(1, checkpointed!.Stage);
        Assert.Equal("cursor-abc", checkpointed.NextProviderCursor);
        Assert.Equal(7, checkpointed.ConversationsObserved);
        Assert.Equal(20, checkpointed.DetailsFetched);
        Assert.Equal(SyncOperationStatus.Running, checkpointed.Status);

        await store.CompleteAsync(operation.OperationId, counters, Now.AddMinutes(2));
        var completed = await store.FindByIdAsync(operation.OperationId);
        Assert.Equal(SyncOperationStatus.CompletedWithinProviderLimits, completed!.Status);
        Assert.Null(completed.NextProviderCursor);
        Assert.NotNull(completed.CompletedAtUtc);
    }

    [Fact]
    public async Task RateLimitedFailureIsTruthfulAndRetryableNotAccountUnhealthy()
    {
        await using var context = NewContext();
        var accountId = await SeedAccountAsync(context);
        var store = new EfProviderSyncOperationStore(context);
        var operation = await store.CreateOrGetActiveAsync(Guid.CreateVersion7(), accountId, Guid.NewGuid(), SyncOperationKind.Manual, Now);
        Assert.NotNull(operation);

        await store.FailAsync(operation.OperationId, ConversationHistoryFailures.RateLimited, transient: true, new ProviderSyncCounters(), Now.AddMinutes(1));

        var failed = await store.FindByIdAsync(operation.OperationId);
        Assert.Equal(SyncOperationStatus.RateLimitedRetrying, failed!.Status);
        Assert.Equal(ConversationHistoryFailures.RateLimited, failed.FailureCategory);
    }

    [Fact]
    public async Task ConcurrentCreatesForSameAccountAndKindYieldExactlyOneActiveOperation()
    {
        await using var context = NewContext();
        var accountId = await SeedAccountAsync(context);
        var workspaceId = Guid.CreateVersion7();
        var results = new List<ProviderSyncOperation?>();

        var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            await using var own = NewContext();
            var store = new EfProviderSyncOperationStore(own);
            return await store.CreateOrGetActiveAsync(Guid.CreateVersion7(), accountId, workspaceId, SyncOperationKind.Initial, Now);
        }));
        results.AddRange(await Task.WhenAll(tasks));

        var distinct = results.Where(r => r is not null).Select(r => r!.OperationId).Distinct().ToList();
        Assert.Single(distinct);
        Assert.Equal(1, await context.ProviderSyncOperations.CountAsync(o => o.ConnectedAccountId == accountId));
    }
}
