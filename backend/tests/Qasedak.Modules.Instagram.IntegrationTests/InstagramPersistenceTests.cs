using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Application.Scheduling;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.FollowerSnapshots;
using Qasedak.Modules.Instagram.Application.HistorySync;
using Qasedak.Modules.Instagram.Application.OAuth;
using Qasedak.Modules.Instagram.Application.Subscriptions;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Qasedak.Modules.Instagram.Infrastructure.Protection;
using Xunit;

namespace Qasedak.Modules.Instagram.IntegrationTests;

[Collection(PostgresTestEnvironment.Name)]
public sealed class InstagramPersistenceTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    private static readonly byte[] ProtectionKey = Convert.FromBase64String(
        "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=");

    /// <summary>Deterministic OAuth stub: the HTTP adapter itself is contract-tested separately.</summary>
    private sealed class StubOAuthClient : IMetaOAuthClient
    {
        /// <summary>
        /// Canonical routing identity this stub issues. Unique per instance by default so
        /// tests sharing the collection database never collide on the global single-owner
        /// rule; tests needing a fixed identity set it explicitly.
        /// </summary>
        public string UserId { get; set; } = "ig-test-" + Guid.NewGuid().ToString("N");

        public Task<CodeExchangeResult> ExchangeCodeAsync(CodeExchangeRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(CodeExchangeResult.Ok(new(
                "SHORT-TOKEN", UserId, ["instagram_business_basic", "instagram_business_manage_comments"])));

        public Task<LongLivedTokenResult> ExchangeShortLivedForLongLivedAsync(
            string shortLivedAccessToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(LongLivedTokenResult.Ok(new("LONG-LIVED-RAW", 60 * 24 * 3600L)));

        public Task<LongLivedTokenResult> RefreshLongLivedAsync(
            string longLivedAccessToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(LongLivedTokenResult.Ok(new("ROTATED-RAW", 60 * 24 * 3600L)));
    }

    private InstagramDbContext NewContext() =>
        new(new DbContextOptionsBuilder<InstagramDbContext>()
            .UseNpgsql(fixture.Context.Database.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", InstagramDbContext.Schema))
            .Options);

    private sealed class StubProfileClient(string userId) : IAccountProfileClient
    {
        public Task<AccountProfileOutcome> GetProfileAsync(string accessToken, string expectedProviderAccountId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountProfileOutcome>(new AccountProfileOutcome.Ok(
                new InstagramAccountProfile(userId, "shop-" + userId, "Shop", null, "Business")));
    }

    private sealed class StubSubscriptionClient : ISubscriptionClient
    {
        public Task<SubscriptionResult> SubscribeAsync(string accessToken, string professionalAccountId, IReadOnlyList<string> fields, CancellationToken cancellationToken = default) =>
            Task.FromResult(SubscriptionResult.Subscribed(fields));
    }

    private sealed class RecordingScheduledWorkStore : IScheduledWorkStore
    {
        public List<ScheduledWorkEnqueue> Enqueued { get; } = [];

        public Task<(ScheduledWorkItem Item, bool Duplicate)> EnqueueAsync(ScheduledWorkEnqueue request, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            Enqueued.Add(request);
            return Task.FromResult((new ScheduledWorkItem(Guid.CreateVersion7(), request.WorkType, request.IdempotencyKey,
                request.PayloadJson, request.PayloadVersion, request.ConnectedAccountId, request.WorkspaceId,
                request.DueAtUtc, ScheduledWorkStatus.Pending, 0, request.MaxAttempts, request.DueAtUtc,
                null, null, null, now, now, null, null), false));
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

    private (ConnectInstagramAccountUseCase Connect, DisconnectInstagramAccountUseCase Disconnect,
        ListWorkspaceConnectionsUseCase List, IProtectedTokenStore Store,
        RecordingScheduledWorkStore Jobs, EfOAuthStateStore States) NewStack(StubOAuthClient? oauth = null)
    {
        oauth ??= new StubOAuthClient();
        var context = NewContext();
        var repo = new EfConnectedAccountRepository(context);
        ITokenProtector protector = new AesGcmTokenProtector(
            Options.Create(new TokenProtectionOptions { KeyBase64 = Convert.ToBase64String(ProtectionKey) }));
        var store = new ProtectedTokenStore(context, protector);
        var jobs = new RecordingScheduledWorkStore();
        var states = new EfOAuthStateStore(context);
        var ensureSync = new EnsureConversationSyncUseCase(new FakeProviderSyncOperationStore(), jobs, new FixedClock(Now));
        return (
            new ConnectInstagramAccountUseCase(repo, store, oauth,
                new StubProfileClient(oauth.UserId), new StubSubscriptionClient(), jobs,
                states, ensureSync, new FixedClock(Now)),
            new DisconnectInstagramAccountUseCase(repo, store, new FixedClock(Now)),
            new ListWorkspaceConnectionsUseCase(repo),
            store,
            jobs,
            states);
    }

    private static async Task<ConnectAccountResult> ConnectAsync(
        ConnectInstagramAccountUseCase connect, EfOAuthStateStore states, Guid workspaceId, string code = "code")
    {
        var issuance = await states.IssueAsync(workspaceId, "https://cb.example/", Now);
        return await connect.ExecuteAsync(new(workspaceId, code, "https://cb.example/", issuance.State));
    }

    [Fact]
    public async Task ConnectPersistsAccountAndEncryptedTokenEndToEnd()
    {
        var oauth = new StubOAuthClient { UserId = "ig-777-persist" };
        var (connect, _, list, store, _, states) = NewStack(oauth);
        var workspaceId = Guid.CreateVersion7();

        var result = await ConnectAsync(connect, states, workspaceId, "auth-code");

        Assert.True(result.Success, result.FailureCode);

        // Fresh context: state must come from the database.
        var connections = await list.ExecuteAsync(workspaceId);
        var record = Assert.Single(connections);
        Assert.Equal("ig-777-persist", record.ProviderIdentity);
        Assert.Equal(AccountHealth.Connected.ToString(), record.Health);

        // Raw token is retrievable only through the protected store and decrypts exactly.
        Assert.Equal("LONG-LIVED-RAW", await store.GetAsync(record.AccountId));

        // The stored row must NOT contain the plaintext.
        await using var verification = NewContext();
        var ciphertextRow = await verification.AccountTokens.SingleAsync(t => t.AccountId == record.AccountId);
        Assert.DoesNotContain("LONG-LIVED-RAW", ciphertextRow.Ciphertext);
        Assert.True(ciphertextRow.Ciphertext.Length > 40);
    }

    [Fact]
    public async Task ReconnectAfterDisconnectIsAllowedBySchema()
    {
        var (connect, disconnect, _, _, _, states) = NewStack();
        var workspaceId = Guid.CreateVersion7();

        var first = await ConnectAsync(connect, states, workspaceId);
        Assert.True(first.Success, first.FailureCode);
        var disconnectResult = await disconnect.ExecuteAsync(workspaceId, first.AccountId);
        Assert.True(disconnectResult.Success, disconnectResult.FailureCode);

        var second = await ConnectAsync(connect, states, workspaceId);

        Assert.True(second.Success, second.FailureCode);
        Assert.NotEqual(first.AccountId, second.AccountId);
    }

    [Fact]
    public async Task ActiveDuplicateConnectionViolatesPartialUniqueIndex()
    {
        var workspaceId = Guid.CreateVersion7();
        await using var context = NewContext();

        await context.Accounts.AddAsync(ConnectedAccount.Create(
            Guid.CreateVersion7(), workspaceId, "ig-dup", ConnectionPath.InstagramLogin,
            ["instagram_business_basic"], Now.AddHours(24), Now));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await context.Accounts.AddAsync(ConnectedAccount.Create(
            Guid.CreateVersion7(), workspaceId, "ig-dup", ConnectionPath.InstagramLogin,
            ["instagram_business_basic"], Now.AddHours(48), Now));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task RotationReplacesCiphertextAtomicallyAndUpdatesExpiry()
    {
        var (connect, _, list, store, _, states) = NewStack();
        var workspaceId = Guid.CreateVersion7();
        var connectResult = await ConnectAsync(connect, states, workspaceId);
        Assert.True(connectResult.Success, connectResult.FailureCode);

        await using var context = NewContext();
        var protector = new AesGcmTokenProtector(Options.Create(
            new TokenProtectionOptions { KeyBase64 = Convert.ToBase64String(ProtectionKey) }));
        var repo = new EfConnectedAccountRepository(context);
        var sameContextStore = new ProtectedTokenStore(context, protector);
        var account = (await repo.FindByIdAsync(connectResult.AccountId))!;
        account.ApplyTokenRotation(Now.AddDays(60).AddMinutes(5), Now);
        await sameContextStore.StoreAsync(account.Id, "ROTATED-RAW");
        await repo.SaveChangesAsync();

        context.ChangeTracker.Clear();
        var reloaded = (await repo.FindByIdAsync(account.Id))!;
        Assert.Equal(Now.AddDays(60).AddMinutes(5), reloaded.TokenExpiresAtUtc);
        Assert.Equal("ROTATED-RAW", await store.GetAsync(account.Id));
    }

    [Fact]
    public async Task DisconnectRemovesTheTokenRow()
    {
        var (connect, disconnect, _, _, _, states) = NewStack();
        var workspaceId = Guid.CreateVersion7();
        var result = await ConnectAsync(connect, states, workspaceId);
        Assert.True(result.Success, result.FailureCode);

        await using var context = NewContext();
        var beforeCount = await context.AccountTokens.CountAsync(t => t.AccountId == result.AccountId);
        Assert.Equal(1, beforeCount);

        var disconnectUseCase = new DisconnectInstagramAccountUseCase(
            new EfConnectedAccountRepository(context),
            new ProtectedTokenStore(context, new AesGcmTokenProtector(Options.Create(
                new TokenProtectionOptions { KeyBase64 = Convert.ToBase64String(ProtectionKey) }))),
            new FixedClock(Now));
        var outcome = await disconnectUseCase.ExecuteAsync(workspaceId, result.AccountId);

        Assert.True(outcome.Success, outcome.FailureCode);
        Assert.Equal(0, await context.AccountTokens.CountAsync(t => t.AccountId == result.AccountId));
    }

    [Fact]
    public async Task ConnectPersistsEnrichmentAndSchedulesRefreshOccurrence()
    {
        var userId = "ig-enrich-" + Guid.NewGuid().ToString("N");
        var oauth = new StubOAuthClient { UserId = userId };
        var (connect, _, _, _, jobs, states) = NewStack(oauth);
        var workspaceId = Guid.CreateVersion7();

        var result = await ConnectAsync(connect, states, workspaceId);
        Assert.True(result.Success, result.FailureCode);

        await using var verification = NewContext();
        var account = (await verification.Accounts.AsNoTracking()
            .SingleAsync(a => a.Id == result.AccountId))!;
        Assert.Equal("shop-" + userId, account.Username);
        Assert.Equal("Shop", account.DisplayName);
        Assert.Equal("Business", account.AccountType);
        Assert.Equal(Now, account.ProfileUpdatedAtUtc);
        Assert.Equal(SubscriptionHealth.Healthy, account.SubscriptionHealth);
        Assert.Equal(Now, account.LastSubscriptionCheckUtc);
        Assert.Equal(0u, account.Version);
        Assert.Equal(Now, account.LastTokenIssuedAtUtc);

        // M13-005 refresh occurrence plus the first daily follower snapshot (M13-007):
        // both identifiers-only, never token material.
        var job = Assert.Single(jobs.Enqueued, j => j.WorkType == TokenRefreshPolicy.JobType);
        var expectedExpiry = Now.AddSeconds(60 * 24 * 3600L);
        Assert.Equal(TokenRefreshPolicy.JobType, job.WorkType);
        Assert.Equal(result.AccountId, job.ConnectedAccountId);
        Assert.Equal(expectedExpiry.AddDays(-7), job.DueAtUtc);
        Assert.Equal(TokenRefreshPolicy.IdempotencyKey(result.AccountId, expectedExpiry), job.IdempotencyKey);
        Assert.Equal(result.AccountId, TokenRefreshPolicy.ParseRefreshPayload(job.PayloadJson));
        Assert.DoesNotContain("LONG-LIVED-RAW", job.PayloadJson);

        var snapshotJob = Assert.Single(jobs.Enqueued, j => j.WorkType == FollowerSnapshotPolicy.JobType);
        Assert.Equal(result.AccountId, snapshotJob.ConnectedAccountId);
        Assert.Equal(FollowerSnapshotPolicy.UtcDay(Now), FollowerSnapshotPolicy.ParsePayload(snapshotJob.PayloadJson)!.Value.SnapshotDateUtc);
        Assert.DoesNotContain("LONG-LIVED-RAW", snapshotJob.PayloadJson);
    }

    [Fact]
    public async Task LegacyRowsMaterializeWithSafeEnrichmentDefaults()
    {
        var accountId = Guid.CreateVersion7();
        var workspaceId = Guid.CreateVersion7();
        var providerId = "ig-legacy-" + Guid.NewGuid().ToString("N");
        await using (var setup = NewContext())
        {
            await setup.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO instagram.connected_accounts
                ("Id", "WorkspaceId", "ProviderUserId", "Path", "Scopes", "Health", "TokenExpiresAtUtc", "ConnectedAtUtc")
                VALUES ({0}, {1}, {2}, 1, 'instagram_business_basic', 1, {3}, {4})
                """,
                accountId, workspaceId, providerId, Now.AddDays(30), Now.AddDays(-30));
        }

        await using var verification = NewContext();
        var account = (await verification.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId))!;

        Assert.Equal(0u, account.Version);
        Assert.Equal(SubscriptionHealth.Unknown, account.SubscriptionHealth);
        Assert.Null(account.Username);
        Assert.Null(account.DisplayName);
        Assert.Null(account.ProfilePictureUrl);
        Assert.Null(account.AccountType);
        Assert.Null(account.ProfileUpdatedAtUtc);
        Assert.Null(account.SubscriptionDetail);
        Assert.Null(account.LastSubscriptionCheckUtc);
        Assert.Null(account.LastTokenIssuedAtUtc);
    }

    [Fact]
    public async Task ConcurrentRotationLosesCompareAndSwapWithoutOverwrite()
    {
        var (connect, _, _, _, _, states) = NewStack();
        var workspaceId = Guid.CreateVersion7();
        var result = await ConnectAsync(connect, states, workspaceId);
        Assert.True(result.Success, result.FailureCode);

        ITokenProtector protector = new AesGcmTokenProtector(
            Options.Create(new TokenProtectionOptions { KeyBase64 = Convert.ToBase64String(ProtectionKey) }));
        await using var first = NewContext();
        await using var second = NewContext();
        var firstRepo = new EfConnectedAccountRepository(first);
        var secondRepo = new EfConnectedAccountRepository(second);
        var firstStore = new ProtectedTokenStore(first, protector);
        var secondStore = new ProtectedTokenStore(second, protector);

        var winner = (await firstRepo.FindByIdAsync(result.AccountId))!;
        var loser = (await secondRepo.FindByIdAsync(result.AccountId))!;
        winner.ApplyTokenRotation(Now.AddDays(60), Now);
        await firstStore.StoreAsync(winner.Id, "WINNER-RAW");
        loser.ApplyTokenRotation(Now.AddDays(61), Now);
        await secondStore.StoreAsync(loser.Id, "LOSER-RAW");

        Assert.True(await firstRepo.TrySaveChangesAsync());
        Assert.False(await secondRepo.TrySaveChangesAsync());

        await using var verification = NewContext();
        var stored = (await verification.Accounts.AsNoTracking().SingleAsync(a => a.Id == result.AccountId))!;
        Assert.Equal(Now.AddDays(60), stored.TokenExpiresAtUtc);
        Assert.Equal(1u, stored.Version);
        // The loser's staged ciphertext rolled back with its failed save:
        // no partial local state survives a lost rotation.
        var verificationStore = new ProtectedTokenStore(verification, protector);
        Assert.Equal("WINNER-RAW", await verificationStore.GetAsync(result.AccountId));
    }

    [Fact]
    public async Task IssuePurgesExpiredStatesOpportunistically()
    {
        var store = new EfOAuthStateStore(NewContext());
        var workspaceId = Guid.CreateVersion7();
        await store.IssueAsync(workspaceId, "https://cb.example/", Now);

        await using (var before = NewContext())
        {
            Assert.Equal(1, await before.OAuthStates.CountAsync(r => r.WorkspaceId == workspaceId));
        }

        // A later issuance purges the now-expired row before inserting its own.
        var later = Now.Add(OAuthStatePolicy.Lifetime).AddMinutes(1);
        await store.IssueAsync(workspaceId, "https://cb.example/", later);

        await using var after = NewContext();
        var remaining = await after.OAuthStates.Where(r => r.WorkspaceId == workspaceId).ToListAsync();
        var survivor = Assert.Single(remaining);
        Assert.Equal(later, survivor.CreatedAtUtc);
    }

    private sealed class StubInspector : IMetaTokenInspector
    {
        public Task<TokenInspection> InspectAsync(string accessToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(TokenInspection.Healthy());
    }

    [Fact]
    public async Task StaleRotationAfterDisconnectLosesAndLeavesNoToken()
    {
        var oauth = new StubOAuthClient();
        var (connect, _, _, _, _, states) = NewStack(oauth);
        var workspaceId = Guid.CreateVersion7();
        var connected = await ConnectAsync(connect, states, workspaceId);
        Assert.True(connected.Success, connected.FailureCode);

        ITokenProtector protector = new AesGcmTokenProtector(
            Options.Create(new TokenProtectionOptions { KeyBase64 = Convert.ToBase64String(ProtectionKey) }));
        // A refresh scope loads the account BEFORE disconnect (stale copy).
        await using var refreshContext = NewContext();
        var refreshRepo = new EfConnectedAccountRepository(refreshContext);
        var refreshSut = new RefreshInstagramTokenUseCase(
            refreshRepo,
            new ProtectedTokenStore(refreshContext, protector),
            oauth,
            new StubInspector(),
            // Fifty days later the token is both due and past the minimum age rule.
            new FixedClock(Now.AddDays(50)));
        // Warm the scope's tracker while the account is still connected: this is
        // the stale worker whose in-memory copy outlives the disconnect commit.
        Assert.NotNull(await refreshRepo.FindByIdAsync(connected.AccountId));

        // Disconnect wins concurrently on a separate scope.
        await using var disconnectContext = NewContext();
        var disconnectSut = new DisconnectInstagramAccountUseCase(
            new EfConnectedAccountRepository(disconnectContext),
            new ProtectedTokenStore(disconnectContext, protector),
            new FixedClock(Now.AddDays(50)));
        Assert.True((await disconnectSut.ExecuteAsync(workspaceId, connected.AccountId)).Success);

        var outcome = await refreshSut.ExecuteAsync(connected.AccountId);

        Assert.IsType<TokenRefreshOutcome.Stale>(outcome);
        await using var verification = NewContext();
        var stored = await verification.Accounts.AsNoTracking().SingleAsync(a => a.Id == connected.AccountId);
        Assert.True(stored.IsDisconnected);
        Assert.Null(stored.TokenExpiresAtUtc);
        Assert.Equal(0, await verification.AccountTokens.CountAsync(t => t.AccountId == connected.AccountId));
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FakeProviderSyncOperationStore : IProviderSyncOperationStore
    {
        public Task<ProviderSyncOperation?> CreateOrGetActiveAsync(Guid operationId, Guid connectedAccountId, Guid workspaceId, SyncOperationKind kind, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProviderSyncOperation?>(new ProviderSyncOperation(
                operationId, connectedAccountId, workspaceId, kind, SyncOperationStatus.Queued, 0,
                0, 0, 0, 0, 0, 0, 0, 0, null, null, now, null, null, now));

        public Task<ProviderSyncOperation?> FindByIdAsync(Guid operationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProviderSyncOperation?>(null);

        public Task<bool> TryStartAsync(Guid operationId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task CheckpointAsync(Guid operationId, int stage, string? nextCursor, ProviderSyncCounters counters, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CompleteAsync(Guid operationId, ProviderSyncCounters counters, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task FailAsync(Guid operationId, string failureCategory, bool transient, ProviderSyncCounters counters, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ProviderSyncOperation>> ListRecentAsync(Guid connectedAccountId, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProviderSyncOperation>>([]);
    }
}
