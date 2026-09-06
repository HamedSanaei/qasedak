using Qasedak.BuildingBlocks.Application;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.Subscriptions;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.UnitTests.TestSupport;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests.Accounts;

/// <summary>
/// M13-005: explicit subscription repair — exact-account scope, ownership,
/// truthful health persistence, token-redaction. No live Meta calls.
/// </summary>
public sealed class RepairSubscriptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid WorkspaceId = Guid.Parse("01914c8e-0000-7000-8000-000000000003");

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
        public Task StoreAsync(Guid accountId, string accessToken, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string?> GetAsync(Guid accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(tokens.GetValueOrDefault(accountId));

        public Task DeleteAsync(Guid accountId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSubscriptionClient : ISubscriptionClient
    {
        public SubscriptionResult Result { get; set; } =
            SubscriptionResult.Subscribed(InstagramSubscriptionFields.Required);

        public List<string> SeenTokens { get; } = [];

        public List<string> SeenAccountIds { get; } = [];

        public List<IReadOnlyList<string>> Requests { get; } = [];

        public Task<SubscriptionResult> SubscribeAsync(string accessToken, string professionalAccountId, IReadOnlyList<string> fields, CancellationToken cancellationToken = default)
        {
            SeenTokens.Add(accessToken);
            SeenAccountIds.Add(professionalAccountId);
            Requests.Add(fields);
            return Task.FromResult(Result);
        }
    }

    private static ConnectedAccount Connected(FakeRepository repo)
    {
        var account = ConnectedAccount.Create(
            Guid.CreateVersion7(), WorkspaceId, "ig-9",
            ConnectionPath.InstagramLogin, ["instagram_business_basic"],
            Now.AddDays(10), Now.AddDays(-50));
        repo.Rows[account.Id] = account;
        return account;
    }

    [Fact]
    public async Task SuccessfulRepairRestoresHealthyWithServerOwnedFieldSet()
    {
        var repo = new FakeRepository();
        var account = Connected(repo);
        account.ApplySubscription(SubscriptionHealth.NeedsRepair, SubscriptionFailures.Unavailable, Now.AddDays(-1));
        var tokens = new Dictionary<Guid, string> { [account.Id] = "CURRENT-TOKEN" };
        var subscriptions = new FakeSubscriptionClient();
        var sut = new RepairSubscriptionUseCase(repo, new FakeTokenStore(tokens), subscriptions, new FixedClock(Now));

        var result = await sut.ExecuteAsync(WorkspaceId, account.Id);

        Assert.True(result.Success);
        Assert.Equal(SubscriptionHealth.Healthy, result.Health);
        Assert.Equal(SubscriptionHealth.Healthy, account.SubscriptionHealth);
        Assert.Null(account.SubscriptionDetail);
        Assert.Equal(Now, account.LastSubscriptionCheckUtc);
        Assert.Equal(InstagramSubscriptionFields.Required, Assert.Single(subscriptions.Requests));
        Assert.Equal(["CURRENT-TOKEN"], subscriptions.SeenTokens);
        // The edge is addressed by the exact professional identity, never a sibling.
        Assert.Equal(["ig-9"], subscriptions.SeenAccountIds);
    }

    [Fact]
    public async Task ForeignWorkspaceRepairIsNotFoundAndTouchesNothing()
    {
        var repo = new FakeRepository();
        var account = Connected(repo);
        var subscriptions = new FakeSubscriptionClient();
        var sut = new RepairSubscriptionUseCase(
            repo, new FakeTokenStore(new Dictionary<Guid, string>()), subscriptions, new FixedClock(Now));

        var result = await sut.ExecuteAsync(Guid.CreateVersion7(), account.Id);

        Assert.False(result.Success);
        Assert.Equal(AccountFailures.NotFound, result.FailureCode);
        Assert.Empty(subscriptions.Requests);
        Assert.Equal(SubscriptionHealth.Unknown, account.SubscriptionHealth);
    }

    [Fact]
    public async Task DisconnectedAccountRepairIsRefused()
    {
        var repo = new FakeRepository();
        var account = Connected(repo);
        account.Disconnect(Now);
        var subscriptions = new FakeSubscriptionClient();
        var sut = new RepairSubscriptionUseCase(
            repo, new FakeTokenStore(new Dictionary<Guid, string>()), subscriptions, new FixedClock(Now));

        var result = await sut.ExecuteAsync(WorkspaceId, account.Id);

        Assert.False(result.Success);
        Assert.Equal(AccountFailures.AlreadyDisconnected, result.FailureCode);
        Assert.Empty(subscriptions.Requests);
    }

    [Fact]
    public async Task MissingTokenMaterialRecordsNeedsRepairAndRefuses()
    {
        var repo = new FakeRepository();
        var account = Connected(repo);
        var subscriptions = new FakeSubscriptionClient();
        var sut = new RepairSubscriptionUseCase(
            repo, new FakeTokenStore(new Dictionary<Guid, string>()), subscriptions, new FixedClock(Now));

        var result = await sut.ExecuteAsync(WorkspaceId, account.Id);

        Assert.False(result.Success);
        Assert.Equal(AccountFailures.TokenMissing, result.FailureCode);
        Assert.Equal(SubscriptionHealth.NeedsRepair, account.SubscriptionHealth);
        Assert.Empty(subscriptions.Requests);
    }

    [Fact]
    public async Task ProviderFailureRecordsTruthfulHealthAndRefuses()
    {
        var repo = new FakeRepository();
        var account = Connected(repo);
        var tokens = new Dictionary<Guid, string> { [account.Id] = "CURRENT-TOKEN" };
        var subscriptions = new FakeSubscriptionClient
        {
            Result = SubscriptionResult.Failed(SubscriptionFailures.PermissionDenied, transient: false),
        };
        var sut = new RepairSubscriptionUseCase(repo, new FakeTokenStore(tokens), subscriptions, new FixedClock(Now));

        var result = await sut.ExecuteAsync(WorkspaceId, account.Id);

        Assert.False(result.Success);
        Assert.Equal(SubscriptionFailures.PermissionDenied, result.FailureCode);
        Assert.Equal(SubscriptionHealth.NeedsRepair, account.SubscriptionHealth);
        Assert.Equal(SubscriptionFailures.PermissionDenied, account.SubscriptionDetail);
    }
}
