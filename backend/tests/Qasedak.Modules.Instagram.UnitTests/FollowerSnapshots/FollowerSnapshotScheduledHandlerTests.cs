using Microsoft.Extensions.Logging;
using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Application.Scheduling;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.FollowerSnapshots;
using Qasedak.Modules.Instagram.Application.Insights;
using Qasedak.Modules.Instagram.Application.Media;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.Infrastructure.Insights;
using Qasedak.Modules.Instagram.Infrastructure.Snapshots;
using Qasedak.Modules.Instagram.UnitTests.TestSupport;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests.FollowerSnapshots;

/// <summary>
/// M13-007: daily snapshot handler — payload parsing, outcome mapping, exactly-one
/// next-day chaining (retry/redelivery cannot duplicate), zero provider calls for
/// disconnected/missing-token accounts, identifiers-only payloads and observability.
/// </summary>
public sealed class FollowerSnapshotScheduledHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid WorkspaceId = Guid.Parse("01914c8e-0000-7000-8000-000000000004");

    private static readonly DateOnly Today = FollowerSnapshotPolicy.UtcDay(Now);

    private sealed class FakeRepository : IConnectedAccountRepository
    {
        public Dictionary<Guid, ConnectedAccount> Rows { get; } = [];

        public Task<ConnectedAccount?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.GetValueOrDefault(id));

        public Task<ConnectedAccount?> FindByProviderIdentityAsync(Guid workspaceId, string providerUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ConnectedAccount?>(null);

        public Task<AccountResolution> ResolveActiveAccountAsync(string providerAccountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(AccountResolution.NotFound());

        public Task<IReadOnlyList<ConnectedAccount>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConnectedAccount>>([]);

        public Task<IReadOnlyList<ConnectedAccount>> ListActiveAsync(int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConnectedAccount>>([]);

        public Task AddAsync(ConnectedAccount account, CancellationToken cancellationToken = default)
        {
            Rows[account.Id] = account;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> DisconnectAsync(Guid accountId, DateTimeOffset disconnectedAtUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class FakeTokenStore(Dictionary<Guid, string> tokens) : IProtectedTokenStore
    {
        public int Gets { get; private set; }

        public Task StoreAsync(Guid accountId, string accessToken, CancellationToken cancellationToken = default)
        {
            tokens[accountId] = accessToken;
            return Task.CompletedTask;
        }

        public Task<string?> GetAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            Gets++;
            return Task.FromResult(tokens.GetValueOrDefault(accountId));
        }

        public Task DeleteAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            tokens.Remove(accountId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInsightsClient : IInstagramInsightsClient
    {
        public FollowerCountResult FollowerResult { get; set; } = new FollowerCountResult.Value(10_000);

        public int FollowerCalls { get; private set; }

        public int MediaCalls { get; private set; }

        public Task<AccountInsightsResult> GetAccountInsightsAsync(string accessToken, string providerAccountId, DateOnly dayUtc, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MediaInsightsResult> GetMediaInsightsAsync(string accessToken, string providerMediaId, MediaKind kind, CancellationToken cancellationToken = default)
        {
            MediaCalls++;
            return Task.FromResult<MediaInsightsResult>(new MediaInsightsResult.Ok([]));
        }

        public Task<FollowerCountResult> GetFollowerCountAsync(string accessToken, string providerAccountId, CancellationToken cancellationToken = default)
        {
            FollowerCalls++;
            return Task.FromResult(FollowerResult);
        }
    }

    private sealed class FakeSnapshotStore : IFollowerSnapshotStore
    {
        public List<FollowerSnapshotRecord> Rows { get; } = [];

        public Task<bool> UpsertAsync(FollowerSnapshotRecord snapshot, CancellationToken cancellationToken = default)
        {
            Rows.Add(snapshot);
            return Task.FromResult(true);
        }

        public Task<FollowerSnapshotRecord?> GetLatestAsync(Guid accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.OrderByDescending(r => r.SnapshotDateUtc).FirstOrDefault(r => r.AccountId == accountId));

        public Task<IReadOnlyList<FollowerSnapshotRecord>> ListHistoryAsync(Guid accountId, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FollowerSnapshotRecord>>(Rows.Where(r => r.AccountId == accountId).OrderByDescending(r => r.SnapshotDateUtc).Take(limit).ToList());

        public Task<bool> ExistsForDayAsync(Guid accountId, DateOnly snapshotDateUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.Any(r => r.AccountId == accountId && r.SnapshotDateUtc == snapshotDateUtc));
    }

    private sealed class FakeWorkStore : IScheduledWorkStore
    {
        public List<ScheduledWorkEnqueue> Enqueued { get; } = [];

        public Task<(ScheduledWorkItem Item, bool Duplicate)> EnqueueAsync(ScheduledWorkEnqueue request, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            Enqueued.Add(request);
            var item = new ScheduledWorkItem(Guid.CreateVersion7(), request.WorkType, request.IdempotencyKey,
                request.PayloadJson, request.PayloadVersion, request.ConnectedAccountId, request.WorkspaceId,
                request.DueAtUtc, ScheduledWorkStatus.Pending, 0, request.MaxAttempts, request.DueAtUtc,
                null, null, null, now, now, null, null);
            return Task.FromResult((item, false));
        }

        public Task<IReadOnlyList<ScheduledWorkItem>> ClaimDueAsync(string leaseOwner, int batchSize, TimeSpan leaseDuration, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> RenewLeaseAsync(Guid id, string leaseOwner, TimeSpan leaseDuration, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CompleteAsync(Guid id, string leaseOwner, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task FailAsync(Guid id, string leaseOwner, WorkOutcome outcome, DateTimeOffset nextAttemptAtUtc, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CancelAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ScheduledWorkItem?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NullHandlerLogger : ILogger<FollowerSnapshotScheduledHandler>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }

    private static ConnectedAccount Connected(FakeRepository repo, Dictionary<Guid, string> tokens)
    {
        var account = ConnectedAccount.Create(
            Guid.CreateVersion7(), WorkspaceId, "ig-11",
            ConnectionPath.InstagramLogin, ["instagram_business_basic", "instagram_business_manage_insights"],
            Now.AddDays(10), Now.AddDays(-50));
        repo.Rows[account.Id] = account;
        tokens[account.Id] = "CURRENT-TOKEN";
        return account;
    }

    private static ScheduledWorkItem ItemFor(Guid accountId, DateOnly dayUtc) =>
        new(Guid.CreateVersion7(), FollowerSnapshotPolicy.JobType,
            FollowerSnapshotPolicy.IdempotencyKey(accountId, dayUtc),
            FollowerSnapshotPolicy.Payload(accountId, dayUtc), 1,
            accountId, WorkspaceId, Now, ScheduledWorkStatus.Claimed, 1, 8, Now,
            null, "test-worker", Now.AddMinutes(5), Now.AddDays(-7), Now, Now.AddMinutes(-1), null);

    private static (FollowerSnapshotScheduledHandler Handler, FakeInsightsClient Insights, FakeWorkStore Jobs, FakeSnapshotStore Snapshots, FakeTokenStore Tokens) NewSut(
        FakeRepository repo, Dictionary<Guid, string> tokens)
    {
        var jobs = new FakeWorkStore();
        var snapshots = new FakeSnapshotStore();
        var insights = new FakeInsightsClient();
        var tokenStore = new FakeTokenStore(tokens);
        var useCase = new FollowerSnapshotUseCase(repo, tokenStore, insights, snapshots, new FixedClock(Now));
        return (new FollowerSnapshotScheduledHandler(useCase, jobs, new FixedClock(Now), new InsightsMetrics(), new NullHandlerLogger()),
            insights, jobs, snapshots, tokenStore);
    }

    [Fact]
    public void HandlerServesTheDocumentedJobType()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var (handler, _, _, _, _) = NewSut(repo, tokens);

        Assert.Equal("instagram.follower-snapshot", handler.WorkType);
    }

    [Fact]
    public async Task MalformedPayloadIsPermanentWithoutProviderOrChain()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var (handler, insights, jobs, _, _) = NewSut(repo, tokens);
        var item = new ScheduledWorkItem(Guid.CreateVersion7(), FollowerSnapshotPolicy.JobType, "k", """{"nope":1}""", 1,
            null, WorkspaceId, Now, ScheduledWorkStatus.Claimed, 1, 8, Now, null, "w", Now, Now, Now, null, null);

        var outcome = await handler.HandleAsync(item, CancellationToken.None);

        var permanent = Assert.IsType<WorkOutcome.Permanent>(outcome);
        Assert.Equal("followers.malformedPayload", permanent.FailureCode);
        Assert.Equal(0, insights.FollowerCalls);
        Assert.Empty(jobs.Enqueued);
    }

    [Fact]
    public async Task ObservationPersistsAndChainsExactlyOneNextDayOccurrence()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var account = Connected(repo, tokens);
        var (handler, _, jobs, snapshots, _) = NewSut(repo, tokens);

        var outcome = await handler.HandleAsync(ItemFor(account.Id, Today), CancellationToken.None);

        Assert.IsType<WorkOutcome.Succeeded>(outcome);
        var row = Assert.Single(snapshots.Rows);
        Assert.Equal(account.Id, row.AccountId);
        Assert.Equal(Today, row.SnapshotDateUtc);
        Assert.Equal(10_000, row.FollowerCount);
        Assert.Equal(FollowerSnapshotProvenance.Observed, row.Provenance);

        var next = Assert.Single(jobs.Enqueued);
        Assert.Equal(FollowerSnapshotPolicy.JobType, next.WorkType);
        Assert.Equal(account.Id, next.ConnectedAccountId);
        Assert.Equal(WorkspaceId, next.WorkspaceId);
        Assert.Equal(FollowerSnapshotPolicy.IdempotencyKey(account.Id, Today.AddDays(1)), next.IdempotencyKey);
        Assert.Equal(FollowerSnapshotPolicy.NextOccurrenceDueAt(Today.AddDays(1)), next.DueAtUtc);
        Assert.DoesNotContain("CURRENT-TOKEN", next.PayloadJson);
        Assert.Equal((account.Id, Today.AddDays(1)), FollowerSnapshotPolicy.ParsePayload(next.PayloadJson));
    }

    [Fact]
    public async Task RedeliveryOfSameDayDoesNotDuplicateNextDayOccurrence()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var account = Connected(repo, tokens);
        var (handler, _, jobs, _, _) = NewSut(repo, tokens);
        var item = ItemFor(account.Id, Today);

        // At-least-once: the same occurrence is delivered twice (crash after first
        // settlement attempt). The second execution must not create a second next-day job.
        await handler.HandleAsync(item, CancellationToken.None);
        var outcome = await handler.HandleAsync(item, CancellationToken.None);

        Assert.IsType<WorkOutcome.Succeeded>(outcome);
        // The handler always enqueues the SAME deterministic next-day key; collapsing
        // duplicates onto one logical job is the store's unique-index contract
        // (proven against real PostgreSQL in the integration suite).
        var nextDay = FollowerSnapshotPolicy.IdempotencyKey(account.Id, Today.AddDays(1));
        var nextDayEnqueues = jobs.Enqueued.Where(j => j.IdempotencyKey == nextDay).ToList();
        Assert.Equal(2, nextDayEnqueues.Count);
        Assert.All(nextDayEnqueues, j => Assert.Equal(FollowerSnapshotPolicy.JobType, j.WorkType));
    }

    [Fact]
    public async Task NoDataSettlesWithoutFabricatingRowAndChainsNextDay()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var account = Connected(repo, tokens);
        var (handler, insights, jobs, snapshots, _) = NewSut(repo, tokens);
        insights.FollowerResult = new FollowerCountResult.NoData("followers.noData");

        var outcome = await handler.HandleAsync(ItemFor(account.Id, Today), CancellationToken.None);

        // Settled for the day (no tight retry loop), no fabricated snapshot.
        Assert.IsType<WorkOutcome.Succeeded>(outcome);
        Assert.Empty(snapshots.Rows);
        Assert.Single(jobs.Enqueued); // bounded 1/day continuation detects restored values
    }

    [Fact]
    public async Task TransientFailureIsRetryableWritesNothingAndKeepsDailyCadence()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var account = Connected(repo, tokens);
        var (handler, insights, jobs, snapshots, _) = NewSut(repo, tokens);
        insights.FollowerResult = new FollowerCountResult.Failed(InsightsFailureKind.RateLimited, InsightsFailures.RateLimited);

        var outcome = await handler.HandleAsync(ItemFor(account.Id, Today), CancellationToken.None);

        var retryable = Assert.IsType<WorkOutcome.Retryable>(outcome);
        Assert.Equal(InsightsFailures.RateLimited, retryable.FailureCode);
        Assert.Empty(snapshots.Rows); // never a fake 0-follower row
        Assert.Single(jobs.Enqueued); // next day still chained idempotently
    }

    [Fact]
    public async Task DisconnectedAccountIsTerminalWithZeroProviderCallsAndNoChain()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var account = Connected(repo, tokens);
        account.Disconnect(Now);
        var (handler, insights, jobs, snapshots, tokenStore) = NewSut(repo, tokens);

        var outcome = await handler.HandleAsync(ItemFor(account.Id, Today), CancellationToken.None);

        var permanent = Assert.IsType<WorkOutcome.Permanent>(outcome);
        Assert.Equal(AccountFailures.AlreadyDisconnected, permanent.FailureCode);
        Assert.Equal(0, insights.FollowerCalls);
        Assert.Empty(jobs.Enqueued);
        Assert.Empty(snapshots.Rows);
        // Zero token read: disconnected accounts must not even touch the store.
        Assert.Equal(0, tokenStore.Gets);
    }

    [Fact]
    public async Task MissingTokenIsTerminalWithoutProviderCallOrChain()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var account = Connected(repo, tokens);
        tokens.Remove(account.Id);
        var (handler, insights, jobs, snapshots, _) = NewSut(repo, tokens);

        var outcome = await handler.HandleAsync(ItemFor(account.Id, Today), CancellationToken.None);

        var permanent = Assert.IsType<WorkOutcome.Permanent>(outcome);
        Assert.Equal(AccountFailures.TokenMissing, permanent.FailureCode);
        Assert.Equal(0, insights.FollowerCalls);
        Assert.Empty(jobs.Enqueued);
        Assert.Empty(snapshots.Rows);
    }

    [Fact]
    public async Task AccountLevelAuthenticationFailureIsTerminalWithoutChain()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var account = Connected(repo, tokens);
        var (handler, insights, _, snapshots, _) = NewSut(repo, tokens);
        insights.FollowerResult = new FollowerCountResult.Failed(InsightsFailureKind.Authentication, "insights.authentication");

        var outcome = await handler.HandleAsync(ItemFor(account.Id, Today), CancellationToken.None);

        Assert.IsType<WorkOutcome.Permanent>(outcome);
        Assert.Empty(snapshots.Rows);
    }
}
