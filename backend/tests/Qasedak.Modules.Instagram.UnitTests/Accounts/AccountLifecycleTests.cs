using Qasedak.BuildingBlocks.Application;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.OAuth;
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

        public Task<IReadOnlyList<ConnectedAccount>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ConnectedAccount> list = Rows.Values.Where(a => a.WorkspaceId == workspaceId).ToArray();
            return Task.FromResult(list);
        }

        public Task AddAsync(ConnectedAccount account, CancellationToken cancellationToken = default)
        {
            Rows[account.Id] = account;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<Guid?> FindWorkspaceIdByProviderIdentityAsync(string providerUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.Values.FirstOrDefault(a => a.ProviderUserId == providerUserId)?.WorkspaceId);
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

    private static (ConnectInstagramAccountUseCase Connect, DisconnectInstagramAccountUseCase Disconnect,
        ListWorkspaceConnectionsUseCase List, FakeAccountRepository Repo, FakeTokenStore Tokens) NewSut(
        FakeOAuthClient? oauth = null)
    {
        oauth ??= new FakeOAuthClient();
        var repo = new FakeAccountRepository();
        var tokens = new FakeTokenStore();
        var clock = new FixedClock(Now);
        return (
            new ConnectInstagramAccountUseCase(repo, tokens, oauth, clock),
            new DisconnectInstagramAccountUseCase(repo, tokens, clock),
            new ListWorkspaceConnectionsUseCase(repo),
            repo,
            tokens);
    }

    [Fact]
    public async Task ConnectExchangesCodeStoresTokenProtectedAndRecordsAggregate()
    {
        var (connect, _, _, repo, tokens) = NewSut();

        var result = await connect.ExecuteAsync(new(Guid.Parse(WorkspaceId), "auth-code", "https://cb.example/"));

        Assert.True(result.Success, result.FailureCode);
        var account = repo.Rows[result.AccountId];
        Assert.Equal("ig-1020", account.ProviderUserId);
        Assert.Equal(ConnectionPath.InstagramLogin, account.Path);
        Assert.Equal(AccountHealth.Connected, account.Health);
        Assert.Equal(["instagram_business_basic", "instagram_business_manage_messages"], account.Scopes);
        Assert.Equal(Now.AddSeconds(60 * 24 * 3600L), account.TokenExpiresAtUtc);
        // Raw token material is only in the protected store.
        Assert.Equal("LONG-TOKEN", await tokens.GetAsync(result.AccountId));
    }

    [Fact]
    public async Task RejectedCodeExchangeFailsWithoutWritingAnything()
    {
        var oauth = new FakeOAuthClient
        {
            CodeResult = CodeExchangeResult.Fail(new(MetaOAuthFailureReason.RejectedByMeta, "400 OAuthException")),
        };
        var (connect, _, _, repo, tokens) = NewSut(oauth);

        var result = await connect.ExecuteAsync(new(Guid.Parse(WorkspaceId), "bad-code", "https://cb.example/"));

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
        var (connect, _, _, repo, _) = NewSut(oauth);

        var result = await connect.ExecuteAsync(new(Guid.Parse(WorkspaceId), "code", "https://cb.example/"));

        Assert.False(result.Success);
        Assert.Equal(AccountFailures.OAuthUnavailable, result.FailureCode);
        Assert.Empty(repo.Rows);
    }

    [Fact]
    public async Task DuplicateConnectionInSameWorkspaceIsRejected()
    {
        var (connect, _, _, repo, _) = NewSut();

        var first = await connect.ExecuteAsync(new(Guid.Parse(WorkspaceId), "code-1", "https://cb.example/"));
        var second = await connect.ExecuteAsync(new(Guid.Parse(WorkspaceId), "code-2", "https://cb.example/"));

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Equal(AccountFailures.AlreadyConnected, second.FailureCode);
    }

    [Fact]
    public async Task DisconnectDeletesTokenMaterialAndRecordsState()
    {
        var (connect, disconnect, _, repo, tokens) = NewSut();
        var connected = await connect.ExecuteAsync(new(Guid.Parse(WorkspaceId), "code", "https://cb.example/"));

        var result = await disconnect.ExecuteAsync(connected.AccountId);

        Assert.True(result.Success, result.FailureCode);
        Assert.Null(await tokens.GetAsync(connected.AccountId));
        Assert.Contains(connected.AccountId, tokens.Deletions);
        Assert.True(repo.Rows[connected.AccountId].IsDisconnected);
        Assert.Null(repo.Rows[connected.AccountId].TokenExpiresAtUtc);
    }

    [Fact]
    public async Task SecondDisconnectReportsAlreadyDisconnected()
    {
        var (connect, disconnect, _, _, _) = NewSut();
        var connected = await connect.ExecuteAsync(new(Guid.Parse(WorkspaceId), "code", "https://cb.example/"));
        await disconnect.ExecuteAsync(connected.AccountId);

        var again = await disconnect.ExecuteAsync(connected.AccountId);

        Assert.False(again.Success);
        Assert.Equal(AccountFailures.AlreadyDisconnected, again.FailureCode);
    }

    [Fact]
    public async Task UnknownDisconnectTargetIsNotFound()
    {
        var (_, disconnect, _, _, _) = NewSut();

        var result = await disconnect.ExecuteAsync(Guid.CreateVersion7());

        Assert.False(result.Success);
        Assert.Equal(AccountFailures.NotFound, result.FailureCode);
    }

    [Fact]
    public async Task ConnectionListingNeverContainsTokenMaterial()
    {
        var (connect, disconnect, list, _, _) = NewSut();
        var connected = await connect.ExecuteAsync(new(Guid.Parse(WorkspaceId), "code", "https://cb.example/"));

        var connections = await list.ExecuteAsync(Guid.Parse(WorkspaceId));
        var record = Assert.Single(connections);

        Assert.Equal(connected.AccountId, record.AccountId);
        Assert.Equal("ig-1020", record.ProviderIdentity);
        Assert.Equal(nameof(ConnectionPath.InstagramLogin), record.Path);
        Assert.Equal(nameof(AccountHealth.Connected), record.Health);
        Assert.DoesNotContain("LONG-TOKEN", string.Join('|', connections.SelectMany(c => c.Scopes)));
        // After disconnect the default surface hides it; explicit include reveals terminal state.
        await disconnect.ExecuteAsync(connected.AccountId);
        var afterDisconnect = await list.ExecuteAsync(Guid.Parse(WorkspaceId));
        Assert.Empty(afterDisconnect);
        var includingDisconnected = await list.ExecuteAsync(Guid.Parse(WorkspaceId), includeDisconnected: true);
        Assert.Equal(nameof(AccountHealth.Connected), includingDisconnected.Single().Health);
        Assert.NotNull(includingDisconnected.Single().DisconnectedAtUtc);
    }
}
