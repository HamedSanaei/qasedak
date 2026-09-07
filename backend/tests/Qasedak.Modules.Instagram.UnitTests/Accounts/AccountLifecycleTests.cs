using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Application.Scheduling;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.FollowerSnapshots;
using Qasedak.Modules.Instagram.Application.HistorySync;
using Qasedak.Modules.Instagram.Application.OAuth;
using Qasedak.Modules.Instagram.Application.Subscriptions;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.UnitTests.TestSupport;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests;

public sealed class AccountLifecycleTests
{
    private const string WorkspaceId = "01914c8e-0000-7000-8000-000000000001";

    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeOAuthClient : IMetaOAuthClient
    {
        public CodeExchangeResult CodeResult { get; set; } =
            CodeExchangeResult.Ok(new("SHORT", "ig-1020", ["instagram_business_basic", "instagram_business_manage_messages"]));

        public LongLivedTokenResult LongLivedResult { get; set; } =
            LongLivedTokenResult.Ok(new("LONG-TOKEN", 60 * 24 * 3600L));

        public Task<CodeExchangeResult> ExchangeCodeAsync(CodeExchangeRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(CodeResult);

        public Task<LongLivedTokenResult> ExchangeShortLivedForLongLivedAsync(string shortLivedAccessToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(LongLivedResult);

        public Task<LongLivedTokenResult> RefreshLongLivedAsync(string longLivedAccessToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(LongLivedResult);
    }

    private sealed class FakeAccountRepository : IConnectedAccountRepository
    {
        public Dictionary<Guid, ConnectedAccount> Rows { get; } = [];

        public Task<ConnectedAccount?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.GetValueOrDefault(id));

        public Task<ConnectedAccount?> FindByProviderIdentityAsync(Guid workspaceId, string providerUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.Values.FirstOrDefault(a => a.WorkspaceId == workspaceId && a.ProviderUserId == providerUserId));

        public Task<AccountResolution> ResolveActiveAccountAsync(string providerAccountId, CancellationToken cancellationToken = default)
        {
            var active = Rows.Values.Where(a => a.ProviderUserId == providerAccountId && !a.IsDisconnected).ToArray();
            return Task.FromResult(active.Length switch
            {
                0 => AccountResolution.NotFound(),
                1 => AccountResolution.Resolved(active[0]),
                _ => AccountResolution.Ambiguous(),
            });
        }

        public Task<IReadOnlyList<ConnectedAccount>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ConnectedAccount> list = Rows.Values.Where(a => a.WorkspaceId == workspaceId).ToArray();
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<ConnectedAccount>> ListActiveAsync(int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConnectedAccount>>(Rows.Values.Where(a => !a.IsDisconnected).Take(Math.Max(1, limit)).ToArray());

        public Task AddAsync(ConnectedAccount account, CancellationToken cancellationToken = default)
        {
            Rows[account.Id] = account;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> DisconnectAsync(Guid accountId, DateTimeOffset disconnectedAtUtc, CancellationToken cancellationToken = default)
        {
            if (!Rows.TryGetValue(accountId, out var account) || account.IsDisconnected)
            {
                return Task.FromResult(false);
            }

            account.Disconnect(disconnectedAtUtc);
            return Task.FromResult(true);
        }
    }

    private sealed class FakeTokenStore : IProtectedTokenStore
    {
        public Dictionary<Guid, string> Tokens { get; } = [];

        public List<Guid> Deletions { get; } = [];

        public Task StoreAsync(Guid accountId, string accessToken, CancellationToken cancellationToken = default)
        {
            Tokens[accountId] = accessToken;
            return Task.CompletedTask;
        }

        public Task<string?> GetAsync(Guid accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Tokens.GetValueOrDefault(accountId));

        public Task DeleteAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            Tokens.Remove(accountId);
            Deletions.Add(accountId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProfileClient : IAccountProfileClient
    {
        public AccountProfileOutcome Result { get; set; } =
            new AccountProfileOutcome.Ok(new InstagramAccountProfile("ig-1020", "shop", "Shop", null, "Business"));

        public Task<AccountProfileOutcome> GetProfileAsync(string accessToken, string expectedProviderAccountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);
    }

    private sealed class FakeSubscriptionClient : ISubscriptionClient
    {
        public SubscriptionResult Result { get; set; } =
            SubscriptionResult.Subscribed(InstagramSubscriptionFields.Required);

        public List<IReadOnlyList<string>> Requests { get; } = [];

        public Task<SubscriptionResult> SubscribeAsync(string accessToken, string professionalAccountId, IReadOnlyList<string> fields, CancellationToken cancellationToken = default)
        {
            Requests.Add(fields);
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeScheduledWorkStore : IScheduledWorkStore
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

    private sealed class FakeOAuthStateStore : IOAuthStateStore
    {
        private readonly Dictionary<string, (Guid WorkspaceId, string RedirectUri, DateTimeOffset ExpiresAtUtc, bool Consumed)> _states = [];

        public Task<OAuthStateIssuance> IssueAsync(Guid workspaceId, string redirectUri, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            var state = "st-" + Guid.CreateVersion7().ToString("N");
            _states[state] = (workspaceId, redirectUri, now.Add(OAuthStatePolicy.Lifetime), false);
            return Task.FromResult(new OAuthStateIssuance(state, now.Add(OAuthStatePolicy.Lifetime)));
        }

        public Task<OAuthStateConsumption> ConsumeAsync(string state, Guid workspaceId, string redirectUri, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            if (!_states.TryGetValue(state ?? string.Empty, out var record))
            {
                return Task.FromResult(OAuthStateConsumption.NotFound);
            }

            if (record.WorkspaceId != workspaceId)
            {
                return Task.FromResult(OAuthStateConsumption.WorkspaceMismatch);
            }

            if (!string.Equals(record.RedirectUri, redirectUri, StringComparison.Ordinal))
            {
                return Task.FromResult(OAuthStateConsumption.RedirectMismatch);
            }

            if (record.ExpiresAtUtc <= now)
            {
                return Task.FromResult(OAuthStateConsumption.Expired);
            }

            if (record.Consumed)
            {
                return Task.FromResult(OAuthStateConsumption.AlreadyConsumed);
            }

            _states[state ?? string.Empty] = record with { Consumed = true };
            return Task.FromResult(OAuthStateConsumption.Consumed);
        }
    }

    private static (ConnectInstagramAccountUseCase Connect, DisconnectInstagramAccountUseCase Disconnect,
        ListWorkspaceConnectionsUseCase List, FakeAccountRepository Repo, FakeTokenStore Tokens,
        FakeProfileClient Profiles, FakeSubscriptionClient Subscriptions, FakeScheduledWorkStore Jobs,
        FakeOAuthStateStore States) NewSut(FakeOAuthClient? oauth = null)
    {
        oauth ??= new FakeOAuthClient();
        var repo = new FakeAccountRepository();
        var tokens = new FakeTokenStore();
        var profiles = new FakeProfileClient();
        var subscriptions = new FakeSubscriptionClient();
        var jobs = new FakeScheduledWorkStore();
        var states = new FakeOAuthStateStore();
        var clock = new FixedClock(Now);
        var ensureSync = new EnsureConversationSyncUseCase(new FakeProviderSyncOperationStore(), jobs, clock);
        return (
            new ConnectInstagramAccountUseCase(repo, tokens, oauth, profiles, subscriptions, jobs, states, ensureSync, clock),
            new DisconnectInstagramAccountUseCase(repo, tokens, clock),
            new ListWorkspaceConnectionsUseCase(repo),
            repo,
            tokens,
            profiles,
            subscriptions,
            jobs,
            states);
    }

    private static async Task<ConnectAccountResult> ConnectAsync(
        ConnectInstagramAccountUseCase connect, FakeOAuthStateStore states, Guid workspaceId, string code = "code")
    {
        var issuance = await states.IssueAsync(workspaceId, "https://cb.example/", Now);
        return await connect.ExecuteAsync(new(workspaceId, code, "https://cb.example/", issuance.State));
    }

    [Fact]
    public async Task ConnectExchangesCodeStoresTokenProtectedAndRecordsAggregate()
    {
        var (connect, _, _, repo, tokens, _, _, jobs, states) = NewSut();

        var result = await ConnectAsync(connect, states, Guid.Parse(WorkspaceId), "auth-code");

        Assert.True(result.Success, result.FailureCode);
        var account = repo.Rows[result.AccountId];
        Assert.Equal("ig-1020", account.ProviderUserId);
        Assert.Equal(ConnectionPath.InstagramLogin, account.Path);
        Assert.Equal(AccountHealth.Connected, account.Health);
        Assert.Equal(["instagram_business_basic", "instagram_business_manage_messages"], account.Scopes);
        Assert.Equal(Now.AddSeconds(60 * 24 * 3600L), account.TokenExpiresAtUtc);
        // Raw token material is only in the protected store.
        Assert.Equal("LONG-TOKEN", await tokens.GetAsync(result.AccountId));
        // Profile proven and subscription attempted at connect.
        Assert.Equal("shop", account.Username);
        Assert.Equal(SubscriptionHealth.Healthy, account.SubscriptionHealth);
        Assert.NotNull(account.ProfileUpdatedAtUtc);
        Assert.NotNull(account.LastSubscriptionCheckUtc);
        // One refresh occurrence for this token generation plus the first daily
        // follower snapshot (M13-007): both identifiers-only.
        var job = Assert.Single(jobs.Enqueued, j => j.WorkType == TokenRefreshPolicy.JobType);
        Assert.Equal(result.AccountId, job.ConnectedAccountId);
        Assert.DoesNotContain("LONG-TOKEN", job.PayloadJson);
        var snapshotJob = Assert.Single(jobs.Enqueued, j => j.WorkType == FollowerSnapshotPolicy.JobType);
        Assert.Equal(result.AccountId, snapshotJob.ConnectedAccountId);
        Assert.Equal(FollowerSnapshotPolicy.UtcDay(Now), FollowerSnapshotPolicy.ParsePayload(snapshotJob.PayloadJson)!.Value.SnapshotDateUtc);
        Assert.DoesNotContain("LONG-TOKEN", snapshotJob.PayloadJson);
    }

    [Fact]
    public async Task RejectedCodeExchangeFailsWithoutWritingAnything()
    {
        var oauth = new FakeOAuthClient
        {
            CodeResult = CodeExchangeResult.Fail(new(MetaOAuthFailureReason.RejectedByMeta, "400 OAuthException")),
        };
        var (connect, _, _, repo, tokens, _, _, _, states) = NewSut(oauth);
        var issuance = await states.IssueAsync(Guid.Parse(WorkspaceId), "https://cb.example/", Now);

        var result = await connect.ExecuteAsync(new(Guid.Parse(WorkspaceId), "bad-code", "https://cb.example/", issuance.State));

        Assert.False(result.Success);
        Assert.Equal(AccountFailures.OAuthRejected, result.FailureCode);
        Assert.Empty(repo.Rows);
        Assert.Empty(tokens.Tokens);
    }

    [Fact]
    public async Task FailedLongLivedUpgradeFailsTheConnection()
    {
        var oauth = new FakeOAuthClient
        {
            LongLivedResult = LongLivedTokenResult.Fail(new(MetaOAuthFailureReason.TransportFailure, "HTTP request failed.")),
        };
        var (connect, _, _, repo, _, _, _, _, states) = NewSut(oauth);

        var result = await ConnectAsync(connect, states, Guid.Parse(WorkspaceId));

        Assert.False(result.Success);
        Assert.Equal(AccountFailures.OAuthUnavailable, result.FailureCode);
        Assert.Empty(repo.Rows);
    }

    [Fact]
    public async Task DuplicateConnectionInSameWorkspaceIsRejected()
    {
        var (connect, _, _, repo, _, _, _, _, states) = NewSut();

        var first = await ConnectAsync(connect, states, Guid.Parse(WorkspaceId), "code-1");
        var second = await ConnectAsync(connect, states, Guid.Parse(WorkspaceId), "code-2");

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Equal(AccountFailures.AlreadyConnected, second.FailureCode);
    }

    [Fact]
    public async Task ActiveConnectionInAnotherWorkspaceIsRejectedWithoutNewRow()
    {
        var (connect, _, _, repo, _, _, _, _, states) = NewSut();
        var otherWorkspace = Guid.CreateVersion7();

        var first = await ConnectAsync(connect, states, Guid.Parse(WorkspaceId), "code-1");
        var second = await ConnectAsync(connect, states, otherWorkspace, "code-2");

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Equal(AccountFailures.AlreadyConnectedElsewhere, second.FailureCode);
        Assert.Single(repo.Rows);
    }

    [Fact]
    public async Task ReconnectAfterDisconnectIsAllowedInAnotherWorkspace()
    {
        var (connect, disconnect, _, repo, _, _, _, _, states) = NewSut();
        var otherWorkspace = Guid.CreateVersion7();

        var first = await ConnectAsync(connect, states, Guid.Parse(WorkspaceId), "code-1");
        Assert.True((await disconnect.ExecuteAsync(Guid.Parse(WorkspaceId), first.AccountId)).Success);
        var second = await ConnectAsync(connect, states, otherWorkspace, "code-2");

        Assert.True(second.Success, second.FailureCode);
        Assert.Equal(2, repo.Rows.Count);
    }

    [Fact]
    public async Task DisconnectDeletesTokenMaterialAndRecordsState()
    {
        var (connect, disconnect, _, repo, tokens, _, _, _, states) = NewSut();
        var connected = await ConnectAsync(connect, states, Guid.Parse(WorkspaceId));

        var result = await disconnect.ExecuteAsync(Guid.Parse(WorkspaceId), connected.AccountId);

        Assert.True(result.Success, result.FailureCode);
        Assert.Null(await tokens.GetAsync(connected.AccountId));
        Assert.Contains(connected.AccountId, tokens.Deletions);
        Assert.True(repo.Rows[connected.AccountId].IsDisconnected);
        Assert.Null(repo.Rows[connected.AccountId].TokenExpiresAtUtc);
        // Terminal disconnect bumps the concurrency counter so a stale
        // in-flight rotation loses its compare-and-swap.
        Assert.Equal(1u, repo.Rows[connected.AccountId].Version);
    }

    [Fact]
    public async Task SecondDisconnectReportsAlreadyDisconnected()
    {
        var (connect, disconnect, _, _, _, _, _, _, states) = NewSut();
        var connected = await ConnectAsync(connect, states, Guid.Parse(WorkspaceId));
        await disconnect.ExecuteAsync(Guid.Parse(WorkspaceId), connected.AccountId);

        var again = await disconnect.ExecuteAsync(Guid.Parse(WorkspaceId), connected.AccountId);

        Assert.False(again.Success);
        Assert.Equal(AccountFailures.AlreadyDisconnected, again.FailureCode);
    }

    [Fact]
    public async Task UnknownDisconnectTargetIsNotFound()
    {
        var (_, disconnect, _, _, _, _, _, _, _) = NewSut();

        var result = await disconnect.ExecuteAsync(Guid.CreateVersion7(), Guid.CreateVersion7());

        Assert.False(result.Success);
        Assert.Equal(AccountFailures.NotFound, result.FailureCode);
    }

    [Fact]
    public async Task ForeignWorkspaceDisconnectIsNotFound()
    {
        var (connect, disconnect, _, _, _, _, _, _, states) = NewSut();
        var connected = await ConnectAsync(connect, states, Guid.Parse(WorkspaceId));

        var result = await disconnect.ExecuteAsync(Guid.CreateVersion7(), connected.AccountId);

        Assert.False(result.Success);
        Assert.Equal(AccountFailures.NotFound, result.FailureCode);
    }

    [Fact]
    public async Task ConnectionListingNeverContainsTokenMaterial()
    {
        var (connect, disconnect, list, _, _, _, _, _, states) = NewSut();
        var connected = await ConnectAsync(connect, states, Guid.Parse(WorkspaceId));

        var connections = await list.ExecuteAsync(Guid.Parse(WorkspaceId));
        var record = Assert.Single(connections);

        Assert.Equal(connected.AccountId, record.AccountId);
        Assert.Equal("ig-1020", record.ProviderIdentity);
        Assert.Equal(nameof(ConnectionPath.InstagramLogin), record.Path);
        Assert.Equal(nameof(AccountHealth.Connected), record.Health);
        Assert.DoesNotContain("LONG-TOKEN", string.Join('|', connections.SelectMany(c => c.Scopes)));
        // After disconnect the default surface hides it; explicit include reveals terminal state.
        await disconnect.ExecuteAsync(Guid.Parse(WorkspaceId), connected.AccountId);
        var afterDisconnect = await list.ExecuteAsync(Guid.Parse(WorkspaceId));
        Assert.Empty(afterDisconnect);
        var includingDisconnected = await list.ExecuteAsync(Guid.Parse(WorkspaceId), includeDisconnected: true);
        Assert.Equal(nameof(AccountHealth.Connected), includingDisconnected.Single().Health);
        Assert.NotNull(includingDisconnected.Single().DisconnectedAtUtc);
    }

    [Fact]
    public async Task MissingStateIsRejectedBeforeAnyMetaCall()
    {
        var (connect, _, _, repo, tokens, _, _, _, _) = NewSut();

        var result = await connect.ExecuteAsync(new(Guid.Parse(WorkspaceId), "code", "https://cb.example/", null));

        Assert.False(result.Success);
        Assert.Equal(OAuthStateFailures.InvalidState, result.FailureCode);
        Assert.Empty(repo.Rows);
        Assert.Empty(tokens.Tokens);
    }

    [Fact]
    public async Task TamperedStateIsRejectedBeforeAnyMetaCall()
    {
        var (connect, _, _, repo, tokens, _, _, _, states) = NewSut();
        var issuance = await states.IssueAsync(Guid.Parse(WorkspaceId), "https://cb.example/", Now);

        var result = await connect.ExecuteAsync(new(
            Guid.Parse(WorkspaceId), "code", "https://cb.example/", issuance.State + "tampered"));

        Assert.False(result.Success);
        Assert.Equal(OAuthStateFailures.InvalidState, result.FailureCode);
        Assert.Empty(repo.Rows);
        Assert.Empty(tokens.Tokens);
    }

    [Fact]
    public async Task ReplayedStateIsRejectedOnSecondUse()
    {
        var (connect, _, _, repo, _, _, _, _, states) = NewSut();
        var workspaceId = Guid.Parse(WorkspaceId);
        var issuance = await states.IssueAsync(workspaceId, "https://cb.example/", Now);

        var first = await connect.ExecuteAsync(new(workspaceId, "code-1", "https://cb.example/", issuance.State));
        var second = await connect.ExecuteAsync(new(workspaceId, "code-2", "https://cb.example/", issuance.State));

        Assert.True(first.Success, first.FailureCode);
        Assert.False(second.Success);
        Assert.Equal(OAuthStateFailures.ReplayedState, second.FailureCode);
        Assert.Single(repo.Rows);
    }

    [Fact]
    public async Task ExpiredStateIsRejected()
    {
        var (connect, _, _, repo, _, _, _, _, states) = NewSut();
        var workspaceId = Guid.Parse(WorkspaceId);
        var issuance = await states.IssueAsync(workspaceId, "https://cb.example/", Now.Add(-OAuthStatePolicy.Lifetime).AddMinutes(-1));

        var result = await connect.ExecuteAsync(new(workspaceId, "code", "https://cb.example/", issuance.State));

        Assert.False(result.Success);
        Assert.Equal(OAuthStateFailures.ExpiredState, result.FailureCode);
        Assert.Empty(repo.Rows);
    }

    [Fact]
    public async Task ForeignWorkspaceStateIsRejected()
    {
        var (connect, _, _, repo, _, _, _, _, states) = NewSut();
        var issuance = await states.IssueAsync(Guid.Parse(WorkspaceId), "https://cb.example/", Now);

        var result = await connect.ExecuteAsync(new(Guid.CreateVersion7(), "code", "https://cb.example/", issuance.State));

        Assert.False(result.Success);
        Assert.Equal(OAuthStateFailures.WorkspaceMismatch, result.FailureCode);
        Assert.Empty(repo.Rows);
    }

    [Fact]
    public async Task RedirectSubstitutionIsRejected()
    {
        var (connect, _, _, repo, _, _, _, _, states) = NewSut();
        var workspaceId = Guid.Parse(WorkspaceId);
        var issuance = await states.IssueAsync(workspaceId, "https://cb.example/", Now);

        var result = await connect.ExecuteAsync(new(workspaceId, "code", "https://evil.example/", issuance.State));

        Assert.False(result.Success);
        Assert.Equal(OAuthStateFailures.RedirectMismatch, result.FailureCode);
        Assert.Empty(repo.Rows);
    }

    [Fact]
    public async Task ProfileIdentityMismatchAbortsConnectionWithoutLeftovers()
    {
        var (connect, _, _, repo, tokens, profiles, _, jobs, states) = NewSut();
        profiles.Result = new AccountProfileOutcome.IdentityMismatch("ig-1020", "99999999999999999");

        var result = await ConnectAsync(connect, states, Guid.Parse(WorkspaceId));

        Assert.False(result.Success);
        Assert.Equal(ProfileFailures.IdentityMismatch, result.FailureCode);
        Assert.Empty(tokens.Tokens);
        Assert.Empty(jobs.Enqueued);
        Assert.Empty(repo.Rows);
    }

    [Fact]
    public async Task TransientProfileFailureAbortsConnectionForRestart()
    {
        var (connect, _, _, repo, tokens, profiles, _, jobs, states) = NewSut();
        profiles.Result = new AccountProfileOutcome.Unavailable(ProfileFailures.Unavailable, Transient: true);

        var result = await ConnectAsync(connect, states, Guid.Parse(WorkspaceId));

        Assert.False(result.Success);
        Assert.Equal(ProfileFailures.Unavailable, result.FailureCode);
        Assert.Empty(tokens.Tokens);
        Assert.Empty(jobs.Enqueued);
        Assert.Empty(repo.Rows);
    }

    [Fact]
    public async Task PartialSubscriptionStillConnectsWithRepairNeeded()
    {
        var (connect, _, _, repo, _, _, subscriptions, _, states) = NewSut();
        subscriptions.Result = SubscriptionResult.Failed(SubscriptionFailures.Unavailable, transient: true);

        var result = await ConnectAsync(connect, states, Guid.Parse(WorkspaceId));

        Assert.True(result.Success, result.FailureCode);
        var account = repo.Rows[result.AccountId];
        Assert.Equal(SubscriptionHealth.NeedsRepair, account.SubscriptionHealth);
        Assert.NotNull(account.LastSubscriptionCheckUtc);
        // The repair endpoint (covered separately) recovers this without new OAuth.
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
