using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.FollowerSnapshots;
using Qasedak.Modules.Instagram.Application.Insights;
using Qasedak.Modules.Instagram.Application.Media;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.Infrastructure.Insights;
using Qasedak.Modules.Instagram.UnitTests.TestSupport;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests.Insights;

/// <summary>
/// M13-007 overview use case: exact-account authorization (zero token reads and zero
/// provider calls for foreign accounts), truthful degradation (permission loss only
/// disables analytics), bounded concurrency with deterministic proof, cancellation,
/// partial per-media failures, unknown-kind safety, NoData-vs-zero semantics and
/// sum-safe media totals. No live Meta calls.
/// </summary>
public sealed class InstagramOverviewUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = FollowerSnapshotPolicy.UtcDay(Now);

    private static readonly Guid WorkspaceId = Guid.Parse("01914c8e-0000-7000-8000-000000000004");

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
        public List<Guid> Gets { get; } = [];

        public Task StoreAsync(Guid accountId, string accessToken, CancellationToken cancellationToken = default)
        {
            tokens[accountId] = accessToken;
            return Task.CompletedTask;
        }

        public Task<string?> GetAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            Gets.Add(accountId);
            return Task.FromResult(tokens.GetValueOrDefault(accountId));
        }

        public Task DeleteAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            tokens.Remove(accountId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMediaCatalogClient : IMediaCatalogClient
    {
        public int RecentCalls { get; private set; }

        public List<MediaCatalogItem> Items { get; set; } = [];

        public MediaCatalogResult? Override { get; set; }

        public Task<MediaCatalogResult> GetPageAsync(string accessToken, string providerAccountId, Guid accountId, int limit, string? afterCursor, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MediaCatalogResult> GetRecentAsync(string accessToken, string providerAccountId, Guid accountId, int maxItems, CancellationToken cancellationToken = default)
        {
            RecentCalls++;
            return Task.FromResult(Override ?? new MediaCatalogResult.Ok(new MediaCatalogPage(Items, null, false)));
        }
    }

    private sealed class FakeInsightsClient : IInstagramInsightsClient
    {
        public AccountInsightsResult AccountResult { get; set; } = new AccountInsightsResult.Ok([]);

        public int AccountCalls { get; private set; }

        public Func<MediaKind, MediaInsightsResult> MediaResult { get; set; } = _ => new MediaInsightsResult.Ok([]);

        public int MediaCalls { get; private set; }

        public List<MediaKind> RequestedKinds { get; } = [];

        public Task<AccountInsightsResult> GetAccountInsightsAsync(string accessToken, string providerAccountId, DateOnly dayUtc, CancellationToken cancellationToken = default)
        {
            AccountCalls++;
            return Task.FromResult(AccountResult);
        }

        public Task<MediaInsightsResult> GetMediaInsightsAsync(string accessToken, string providerMediaId, MediaKind kind, CancellationToken cancellationToken = default)
        {
            MediaCalls++;
            RequestedKinds.Add(kind);
            return Task.FromResult(MediaResult(kind));
        }

        public Task<FollowerCountResult> GetFollowerCountAsync(string accessToken, string providerAccountId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// Concurrency-probing insights client: each media call blocks on a per-call gate
    /// the test releases, so the use case's bounded fan-out can be proven deterministically
    /// without timing sleeps.
    /// </summary>
    private sealed class GatedInsightsClient : IInstagramInsightsClient
    {
        public int InFlight;
        public int Started;
        public int MaxObservedConcurrent;
        public List<TaskCompletionSource> Gates { get; } = [];

        public Task<AccountInsightsResult> GetAccountInsightsAsync(string accessToken, string providerAccountId, DateOnly dayUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountInsightsResult>(new AccountInsightsResult.Ok([]));

        public async Task<MediaInsightsResult> GetMediaInsightsAsync(string accessToken, string providerMediaId, MediaKind kind, CancellationToken cancellationToken = default)
        {
            var inFlight = Interlocked.Increment(ref InFlight);
            Interlocked.Increment(ref Started);
            UpdateMax(inFlight);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (Gates)
            {
                Gates.Add(gate);
            }

            try
            {
                await gate.Task.WaitAsync(cancellationToken);
                return new MediaInsightsResult.Ok([]);
            }
            finally
            {
                Interlocked.Decrement(ref InFlight);
            }
        }

        public Task<FollowerCountResult> GetFollowerCountAsync(string accessToken, string providerAccountId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private void UpdateMax(int current)
        {
            while (true)
            {
                var observed = Volatile.Read(ref MaxObservedConcurrent);
                if (current <= observed || Interlocked.CompareExchange(ref MaxObservedConcurrent, current, observed) == observed)
                {
                    return;
                }
            }
        }
    }

    private sealed class FakeSnapshotStore : IFollowerSnapshotStore
    {
        public List<FollowerSnapshotRecord> Rows { get; set; } = [];

        public Task<bool> UpsertAsync(FollowerSnapshotRecord snapshot, CancellationToken cancellationToken = default)
        {
            Rows.Add(snapshot);
            return Task.FromResult(true);
        }

        public Task<FollowerSnapshotRecord?> GetLatestAsync(Guid accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.Where(r => r.AccountId == accountId).OrderByDescending(r => r.SnapshotDateUtc).FirstOrDefault());

        public Task<IReadOnlyList<FollowerSnapshotRecord>> ListHistoryAsync(Guid accountId, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FollowerSnapshotRecord>>(
                Rows.Where(r => r.AccountId == accountId).OrderByDescending(r => r.SnapshotDateUtc).Take(limit).ToList());

        public Task<bool> ExistsForDayAsync(Guid accountId, DateOnly snapshotDateUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.Any(r => r.AccountId == accountId && r.SnapshotDateUtc == snapshotDateUtc));
    }

    private sealed class NullObservability : IInsightsObservability
    {
        public List<(InsightMetricKey Key, MediaKind Kind)> Drift { get; } = [];

        public void RecordContractDrift(InsightMetricKey metricKey, MediaKind mediaKind) => Drift.Add((metricKey, mediaKind));
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

    private static GetInstagramOverviewUseCase NewUseCase(
        FakeRepository repo,
        Dictionary<Guid, string> tokens,
        FakeMediaCatalogClient media,
        FakeInsightsClient insights,
        FakeSnapshotStore snapshots,
        int maxConcurrent = 4,
        NullObservability? observability = null) =>
        new(repo, new FakeTokenStore(tokens), media, insights, snapshots,
            new InsightsOptions { MaxConcurrentMediaInsights = maxConcurrent },
            observability ?? new NullObservability(), new FixedClock(Now));

    private static MediaCatalogItem Item(string id, string kind = "Image", int? likeCount = null, int? commentCount = null) =>
        new(id, null, Enum.Parse<MediaKind>(kind), null, null, null, null, null, false, false, likeCount, commentCount, null);

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new Xunit.Sdk.XunitException("Timed out waiting for condition.");
            }

            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task ForeignWorkspaceAccountIsRefusedWithZeroTokenReadsAndZeroProviderCalls()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var account = Connected(repo, tokens); // workspace A
        var media = new FakeMediaCatalogClient();
        var insights = new FakeInsightsClient();
        var useCase = NewUseCase(repo, tokens, media, insights, new FakeSnapshotStore());
        var tokenStore = new FakeTokenStore(tokens);
        var useCaseWithStore = new GetInstagramOverviewUseCase(
            repo, tokenStore, media, insights, new FakeSnapshotStore(),
            new InsightsOptions(), new NullObservability(), new FixedClock(Now));

        var result = await useCaseWithStore.ExecuteAsync(Guid.NewGuid(), account.Id, default);

        var refused = Assert.IsType<InstagramOverviewResult.Refused>(result);
        Assert.Equal(AccountFailures.NotFound, refused.FailureCode);
        Assert.Empty(tokenStore.Gets);
        Assert.Equal(0, media.RecentCalls);
        Assert.Equal(0, insights.AccountCalls);
        Assert.Equal(0, insights.MediaCalls);
    }

    [Fact]
    public async Task DisconnectedAccountIsRefusedWithoutProviderCalls()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var account = Connected(repo, tokens);
        account.Disconnect(Now);
        var media = new FakeMediaCatalogClient();
        var insights = new FakeInsightsClient();
        var useCase = NewUseCase(repo, tokens, media, insights, new FakeSnapshotStore());

        var result = await useCase.ExecuteAsync(WorkspaceId, account.Id, default);

        Assert.Equal(AccountFailures.AlreadyDisconnected, Assert.IsType<InstagramOverviewResult.Refused>(result).FailureCode);
        Assert.Equal(0, media.RecentCalls);
        Assert.Equal(0, insights.AccountCalls);
    }

    [Fact]
    public async Task SuccessfulOverviewProjectsFollowersHistoryMediaAndMetrics()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var account = Connected(repo, tokens);
        var media = new FakeMediaCatalogClient
        {
            Items = [Item("m1", "Image", likeCount: 3, commentCount: 1), Item("m2", "Reel", likeCount: 5, commentCount: 2)],
        };
        var insights = new FakeInsightsClient
        {
            AccountResult = new AccountInsightsResult.Ok(
            [
                MetricObservation.Available(InsightMetricKey.Reach, 100),
                MetricObservation.Unavailable(InsightMetricKey.AccountsEngaged, MetricAvailability.NoData),
            ]),
            MediaResult = kind => kind == MediaKind.Reel
                ? new MediaInsightsResult.Ok([MetricObservation.Available(InsightMetricKey.Views, 9)])
                : new MediaInsightsResult.Ok([MetricObservation.Available(InsightMetricKey.Likes, 1)]),
        };
        var snapshots = new FakeSnapshotStore
        {
            Rows =
            [
                new FollowerSnapshotRecord(account.Id, Today, 10_000, FollowerSnapshotProvenance.Observed, Now),
                new FollowerSnapshotRecord(account.Id, Today.AddDays(-1), 9_900, FollowerSnapshotProvenance.Observed, Now.AddDays(-1)),
            ],
        };

        var result = Assert.IsType<InstagramOverviewResult.Ok>(
            await NewUseCase(repo, tokens, media, insights, snapshots).ExecuteAsync(WorkspaceId, account.Id, default));

        var overview = result.Overview;
        Assert.Equal(account.Id, overview.AccountId);
        Assert.Equal(InsightsAvailability.Available, overview.AnalyticsAvailability);
        // Current follower: today's snapshot → Available with provenance.
        Assert.Equal(CurrentFollowerState.Available, overview.CurrentFollowers.State);
        Assert.Equal(10_000, overview.CurrentFollowers.Value);
        Assert.Equal(FollowerSnapshotProvenance.Observed, overview.CurrentFollowers.Provenance);
        Assert.Equal(Now, overview.CurrentFollowers.ObservedAtUtc);
        Assert.Equal(2, overview.FollowerHistory.Count);
        // Media totals: sum-safe counters, complete because every item reported values.
        Assert.Equal(2, overview.Media.Count);
        Assert.Equal(8, overview.Media.LikeTotal);
        Assert.Equal(3, overview.Media.CommentTotal);
        Assert.True(overview.Media.LikeTotalComplete);
        var items = overview.Media.Items!;
        var m1 = items.Single(i => i.ProviderMediaId == "m1");
        Assert.Equal(MediaKind.Image, m1.Kind);
        var m1Likes = Assert.Single(m1.Insights, o => o.Key == InsightMetricKey.Likes);
        Assert.Equal(MetricAvailability.Available, m1Likes.Availability);
        Assert.Equal(1, m1Likes.Value);
        var m2Views = items.Single(i => i.ProviderMediaId == "m2").Insights
            .Single(o => o.Key == InsightMetricKey.Views);
        Assert.Equal(9, m2Views.Value);
        var reach = Assert.Single(overview.AccountInsights, o => o.Key == InsightMetricKey.Reach);
        Assert.Equal(100, reach.Value);
        Assert.Equal(MetricAvailability.NoData,
            Assert.Single(overview.AccountInsights, o => o.Key == InsightMetricKey.AccountsEngaged).Availability);
    }

    [Fact]
    public async Task StaleAndMissingCurrentFollowersAreTruthful()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var account = Connected(repo, tokens);

        // No snapshot at all → NoData.
        var noData = Assert.IsType<InstagramOverviewResult.Ok>(
            await NewUseCase(repo, tokens, new FakeMediaCatalogClient(), new FakeInsightsClient(), new FakeSnapshotStore())
                .ExecuteAsync(WorkspaceId, account.Id, default));
        Assert.Equal(CurrentFollowerState.NoData, noData.Overview.CurrentFollowers.State);
        Assert.Null(noData.Overview.CurrentFollowers.Value);

        // Yesterday's snapshot → Stale with provenance, never presented as live.
        var snapshots = new FakeSnapshotStore
        {
            Rows =
            [
                new FollowerSnapshotRecord(account.Id, Today.AddDays(-1), 9_900, FollowerSnapshotProvenance.Observed, Now.AddDays(-1)),
            ],
        };
        var stale = Assert.IsType<InstagramOverviewResult.Ok>(
            await NewUseCase(repo, tokens, new FakeMediaCatalogClient(), new FakeInsightsClient(), snapshots)
                .ExecuteAsync(WorkspaceId, account.Id, default));
        Assert.Equal(CurrentFollowerState.Stale, stale.Overview.CurrentFollowers.State);
        Assert.Equal(9_900, stale.Overview.CurrentFollowers.Value);
        Assert.Equal(FollowerSnapshotProvenance.Observed, stale.Overview.CurrentFollowers.Provenance);
    }

    [Fact]
    public async Task PermissionLossDisablesOnlyAnalyticsMediaCatalogAndFollowersSurvive()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var account = Connected(repo, tokens);
        var media = new FakeMediaCatalogClient { Items = [Item("m1", "Image", likeCount: 3, commentCount: 1)] };
        var insights = new FakeInsightsClient
        {
            AccountResult = new AccountInsightsResult.Failed(InsightsFailureKind.PermissionLoss, InsightsFailures.PermissionDenied),
        };
        var snapshots = new FakeSnapshotStore
        {
            Rows = [new FollowerSnapshotRecord(account.Id, Today, 10_000, FollowerSnapshotProvenance.Observed, Now)],
        };

        var result = Assert.IsType<InstagramOverviewResult.Ok>(
            await NewUseCase(repo, tokens, media, insights, snapshots).ExecuteAsync(WorkspaceId, account.Id, default));

        var overview = result.Overview;
        Assert.Equal(InsightsAvailability.PermissionRequired, overview.AnalyticsAvailability);
        // Media catalog + basic counts still present.
        Assert.Equal(MediaOverviewState.Available, overview.Media.State);
        Assert.Equal(3, overview.Media.LikeTotal);
        Assert.Equal(10_000, overview.CurrentFollowers.Value);
        Assert.Single(overview.FollowerHistory);
        // Media insight fan-out is SKIPPED entirely: no provider amplification.
        Assert.Equal(0, insights.MediaCalls);
        var m1Insights = overview.Media.Items!.Single().Insights;
        Assert.NotEmpty(m1Insights);
        Assert.All(m1Insights, o => Assert.Equal(MetricAvailability.PermissionRequired, o.Availability));
        Assert.All(overview.AccountInsights, o => Assert.Equal(MetricAvailability.PermissionRequired, o.Availability));
    }

    [Fact]
    public async Task BoundedConcurrencyNeverExceedsConfiguredMaximum()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var account = Connected(repo, tokens);
        var media = new FakeMediaCatalogClient
        {
            Items = [Item("m1"), Item("m2"), Item("m3"), Item("m4"), Item("m5")],
        };
        var gated = new GatedInsightsClient();
        var useCase = new GetInstagramOverviewUseCase(
            repo, new FakeTokenStore(tokens), media, gated, new FakeSnapshotStore(),
            new InsightsOptions { MaxConcurrentMediaInsights = 2 },
            new NullObservability(), new FixedClock(Now));

        var execute = useCase.ExecuteAsync(WorkspaceId, account.Id, default);

        // Two calls are in flight and blocked on their gates: no third call may start.
        await WaitUntilAsync(() => Volatile.Read(ref gated.Started) == 2);
        Assert.Equal(2, Volatile.Read(ref gated.Started));
        Assert.Equal(2, Volatile.Read(ref gated.InFlight));

        // Free one slot: exactly one more call may start (still ≤ 2 in flight).
        gated.Gates[0].TrySetResult();
        await WaitUntilAsync(() => Volatile.Read(ref gated.Started) == 3);
        Assert.Equal(2, Volatile.Read(ref gated.InFlight));

        // Free every gate held so far; calls 4-5 then start and add their own gates,
        // which are released once they exist. The overview completes with all 5 media
        // calls at max 2 concurrent.
        foreach (var gate in gated.Gates)
        {
            gate.TrySetResult();
        }

        await WaitUntilAsync(() => Volatile.Read(ref gated.Started) == 5);
        foreach (var gate in gated.Gates)
        {
            gate.TrySetResult();
        }

        var result = await execute;
        Assert.IsType<InstagramOverviewResult.Ok>(result);
        Assert.Equal(5, Volatile.Read(ref gated.Started));
        Assert.Equal(2, Volatile.Read(ref gated.MaxObservedConcurrent));
    }

    [Fact]
    public async Task CancellationStopsQueuedCallsAndMakesNoExtraProviderRequests()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var account = Connected(repo, tokens);
        var media = new FakeMediaCatalogClient
        {
            Items = [Item("m1"), Item("m2"), Item("m3"), Item("m4")],
        };
        var gated = new GatedInsightsClient();
        var useCase = new GetInstagramOverviewUseCase(
            repo, new FakeTokenStore(tokens), media, gated, new FakeSnapshotStore(),
            new InsightsOptions { MaxConcurrentMediaInsights = 2 },
            new NullObservability(), new FixedClock(Now));
        using var cts = new CancellationTokenSource();

        var execute = useCase.ExecuteAsync(WorkspaceId, account.Id, cts.Token);

        await WaitUntilAsync(() => Volatile.Read(ref gated.Started) == 2);
        cts.Cancel();
        // Release the in-flight calls so they can observe cancellation cleanly.
        foreach (var gate in gated.Gates)
        {
            gate.SetResult();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execute);
        // The queued calls were cancelled at the semaphore: no third/fourth provider call.
        Assert.Equal(2, Volatile.Read(ref gated.Started));
        Assert.Equal(2, Volatile.Read(ref gated.MaxObservedConcurrent));
    }

    [Fact]
    public async Task OneMediaFailureDegradesOnlyThatMedia()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var account = Connected(repo, tokens);
        var media = new FakeMediaCatalogClient { Items = [Item("ok", "Image"), Item("bad", "Video")] };
        var insights = new FakeInsightsClient
        {
            MediaResult = kind => kind == MediaKind.Video
                ? new MediaInsightsResult.Failed(InsightsFailureKind.RateLimited, InsightsFailures.RateLimited)
                : new MediaInsightsResult.Ok([MetricObservation.Available(InsightMetricKey.Likes, 7)]),
        };

        var result = Assert.IsType<InstagramOverviewResult.Ok>(
            await NewUseCase(repo, tokens, media, insights, new FakeSnapshotStore()).ExecuteAsync(WorkspaceId, account.Id, default));

        var items = result.Overview.Media.Items!;
        var okItem = items.Single(i => i.ProviderMediaId == "ok");
        Assert.Equal(MetricAvailability.Available, Assert.Single(okItem.Insights).Availability);
        var badItem = items.Single(i => i.ProviderMediaId == "bad");
        Assert.All(badItem.Insights, o => Assert.Equal(MetricAvailability.TemporarilyUnavailable, o.Availability));
        Assert.Equal(InsightsAvailability.Available, result.Overview.AnalyticsAvailability);
    }

    [Fact]
    public async Task UnknownMediaKindProducesNoProviderRequestAndEmptyInsights()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var account = Connected(repo, tokens);
        var media = new FakeMediaCatalogClient { Items = [Item("mystery", "Unknown")] };
        var insights = new FakeInsightsClient();

        var result = Assert.IsType<InstagramOverviewResult.Ok>(
            await NewUseCase(repo, tokens, media, insights, new FakeSnapshotStore()).ExecuteAsync(WorkspaceId, account.Id, default));

        Assert.Equal(0, insights.MediaCalls);
        Assert.Empty(result.Overview.Media.Items!.Single().Insights);
        Assert.Equal(MediaOverviewState.Available, result.Overview.Media.State);
    }

    [Fact]
    public async Task MissingLikeCountsMakeTotalsUntruthfullyIncomplete()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var account = Connected(repo, tokens);
        var media = new FakeMediaCatalogClient
        {
            Items = [Item("a", "Image", likeCount: 3, commentCount: 1), Item("b", "Image", likeCount: null, commentCount: 2)],
        };

        var result = Assert.IsType<InstagramOverviewResult.Ok>(
            await NewUseCase(repo, tokens, media, new FakeInsightsClient(), new FakeSnapshotStore()).ExecuteAsync(WorkspaceId, account.Id, default));

        Assert.Null(result.Overview.Media.LikeTotal);
        Assert.False(result.Overview.Media.LikeTotalComplete);
        Assert.Equal(3, result.Overview.Media.CommentTotal);
        Assert.True(result.Overview.Media.CommentTotalComplete);
    }

    [Fact]
    public async Task MediaCatalogFailureDegradesMediaSectionWithoutLosingRest()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var account = Connected(repo, tokens);
        var media = new FakeMediaCatalogClient
        {
            Override = new MediaCatalogResult.Failed(MediaCatalogFailures.Unavailable, Transient: true),
        };
        var snapshots = new FakeSnapshotStore
        {
            Rows = [new FollowerSnapshotRecord(account.Id, Today, 10_000, FollowerSnapshotProvenance.Observed, Now)],
        };

        var result = Assert.IsType<InstagramOverviewResult.Ok>(
            await NewUseCase(repo, tokens, media, new FakeInsightsClient(), snapshots).ExecuteAsync(WorkspaceId, account.Id, default));

        Assert.Equal(MediaOverviewState.TemporarilyUnavailable, result.Overview.Media.State);
        Assert.Null(result.Overview.Media.Items);
        Assert.Equal(10_000, result.Overview.CurrentFollowers.Value);
        Assert.Equal(InsightsAvailability.Available, result.Overview.AnalyticsAvailability);
    }

    [Fact]
    public async Task ContractDriftIsRecordedWithMetricKeyAndMediaKindOnly()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var account = Connected(repo, tokens);
        var media = new FakeMediaCatalogClient { Items = [Item("m1", "Reel")] };
        var insights = new FakeInsightsClient
        {
            MediaResult = _ => new MediaInsightsResult.Failed(InsightsFailureKind.ContractDrift, InsightsFailures.ContractDrift),
        };
        var observability = new NullObservability();

        var result = Assert.IsType<InstagramOverviewResult.Ok>(
            await NewUseCase(repo, tokens, media, insights, new FakeSnapshotStore(), observability: observability)
                .ExecuteAsync(WorkspaceId, account.Id, default));

        Assert.NotEmpty(observability.Drift);
        Assert.All(observability.Drift, d => Assert.Equal(MediaKind.Reel, d.Kind));
        Assert.Contains(observability.Drift, d => d.Key == InsightMetricKey.IgReelsAvgWatchTime);
        Assert.All(result.Overview.Media.Items!.Single().Insights, o => Assert.Equal(MetricAvailability.NoData, o.Availability));
    }

    [Fact]
    public async Task EmptyInsightDataIsNoDataNotZero()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var account = Connected(repo, tokens);
        var media = new FakeMediaCatalogClient { Items = [Item("m1", "Image")] };
        var insights = new FakeInsightsClient
        {
            AccountResult = new AccountInsightsResult.Ok([]), // empty provider data set
            MediaResult = _ => new MediaInsightsResult.Ok([]),
        };

        var result = Assert.IsType<InstagramOverviewResult.Ok>(
            await NewUseCase(repo, tokens, media, insights, new FakeSnapshotStore()).ExecuteAsync(WorkspaceId, account.Id, default));

        Assert.All(result.Overview.AccountInsights, o => Assert.Equal(MetricAvailability.NoData, o.Availability));
        Assert.All(result.Overview.Media.Items!.Single().Insights, o => Assert.Equal(MetricAvailability.NoData, o.Availability));
    }
}
