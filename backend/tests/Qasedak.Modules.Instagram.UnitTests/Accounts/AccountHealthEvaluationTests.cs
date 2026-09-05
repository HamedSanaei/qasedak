using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.OAuth;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.UnitTests.TestSupport;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests;

public sealed class AccountHealthEvaluationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    private sealed class StubInspector(TokenInspection result) : IMetaTokenInspector
    {
        public static readonly StubInspector Healthy = new(TokenInspection.Healthy());

        public Task<TokenInspection> InspectAsync(string accessToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class RecordingStore : IProtectedTokenStore
    {
        public string? Token { get; set; } = "RAW-TOKEN";

        public Task StoreAsync(Guid accountId, string accessToken, CancellationToken cancellationToken = default)
        {
            Token = accessToken;
            return Task.CompletedTask;
        }

        public Task<string?> GetAsync(Guid accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Token);

        public Task DeleteAsync(Guid accountId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class InMemoryAccountRepository : IConnectedAccountRepository
    {
        private readonly Dictionary<Guid, ConnectedAccount> _rows = [];

        public InMemoryAccountRepository(params ConnectedAccount[] seed) =>
            seed.ToList().ForEach(a => _rows[a.Id] = a);

        public ConnectedAccount Single() => _rows.Values.Single();

        public Task<ConnectedAccount?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_rows.GetValueOrDefault(id));

        public Task<ConnectedAccount?> FindByProviderIdentityAsync(Guid workspaceId, string providerUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_rows.Values.FirstOrDefault(a => a.WorkspaceId == workspaceId && a.ProviderUserId == providerUserId));

        public Task<IReadOnlyList<ConnectedAccount>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ConnectedAccount> list = _rows.Values.Where(a => a.WorkspaceId == workspaceId).ToArray();
            return Task.FromResult(list);
        }

        public Task AddAsync(ConnectedAccount account, CancellationToken cancellationToken = default)
        {
            _rows[account.Id] = account;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<AccountResolution> ResolveActiveAccountAsync(string providerAccountId, CancellationToken cancellationToken = default)
        {
            var active = _rows.Values.Where(a => a.ProviderUserId == providerAccountId && !a.IsDisconnected).ToArray();
            return Task.FromResult(active.Length switch
            {
                0 => AccountResolution.NotFound(),
                1 => AccountResolution.Resolved(active[0]),
                _ => AccountResolution.Ambiguous(),
            });
        }
    }

    private static ConnectedAccount NewInstagramAccount(TimeSpan? expiryIn = null) =>
        ConnectedAccount.FromState(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "ig-1", ConnectionPath.InstagramLogin,
            ["instagram_business_basic"], AccountHealth.Connected, null,
            expiryIn is null ? Now.AddDays(30) : Now.Add(expiryIn.Value),
            Now.AddDays(-1), null);

    [Fact]
    public async Task ExpiredTokenShortCircuitsWithoutNetworkCall()
    {
        var repo = new InMemoryAccountRepository(NewInstagramAccount(expiryIn: TimeSpan.FromHours(-1)));
        var evaluator = new EvaluateAccountHealthUseCase(repo, new RecordingStore(), StubInspector.Healthy, new FixedClock(Now));

        var result = await evaluator.ExecuteAsync(repo.Single().Id);

        Assert.True(result.Success, result.FailureCode);
        Assert.Equal(nameof(AccountHealth.Expired), result.Health);
        Assert.Equal(AccountHealth.Expired, repo.Single().Health);
    }

    [Fact]
    public async Task RevokedInspectionMarksRevokedWithActionableDetail()
    {
        var repo = new InMemoryAccountRepository(NewInstagramAccount());
        var evaluator = new EvaluateAccountHealthUseCase(
            repo, new RecordingStore(),
            new StubInspector(TokenInspection.From(TokenInspectionKind.Revoked, "The account owner revoked access.")),
            new FixedClock(Now));

        var result = await evaluator.ExecuteAsync(repo.Single().Id);

        Assert.True(result.Success, result.FailureCode);
        Assert.Equal(nameof(AccountHealth.Revoked), result.Health);
        Assert.Contains("revoked", result.HealthDetail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PermissionLossSurfacesUnhealthy()
    {
        var repo = new InMemoryAccountRepository(NewInstagramAccount());
        var evaluator = new EvaluateAccountHealthUseCase(
            repo, new RecordingStore(),
            new StubInspector(TokenInspection.From(TokenInspectionKind.PermissionLoss, "A required permission was removed.")),
            new FixedClock(Now));

        var result = await evaluator.ExecuteAsync(repo.Single().Id);

        Assert.True(result.Success, result.FailureCode);
        Assert.Equal(nameof(AccountHealth.Unhealthy), result.Health);
    }

    [Fact]
    public async Task TransientInspectionLeavesPersistedHealthUntouched()
    {
        var repo = new InMemoryAccountRepository(NewInstagramAccount());
        var evaluator = new EvaluateAccountHealthUseCase(
            repo, new RecordingStore(),
            new StubInspector(TokenInspection.From(TokenInspectionKind.Transient, "Meta returned status 503.")),
            new FixedClock(Now));

        var result = await evaluator.ExecuteAsync(repo.Single().Id);

        Assert.True(result.Success, result.FailureCode);
        Assert.Equal(nameof(AccountHealth.Connected), result.Health);
        Assert.Equal(AccountHealth.Connected, repo.Single().Health);
    }

    [Fact]
    public async Task HealthyInspectionInsideSevenDayWindowFlagsExpiringSoon()
    {
        var repo = new InMemoryAccountRepository(NewInstagramAccount(expiryIn: TimeSpan.FromDays(5)));
        var evaluator = new EvaluateAccountHealthUseCase(
            repo, new RecordingStore(), StubInspector.Healthy, new FixedClock(Now));

        var result = await evaluator.ExecuteAsync(repo.Single().Id);

        Assert.True(result.Success, result.FailureCode);
        Assert.Equal(nameof(AccountHealth.ExpiringSoon), result.Health);
    }

    [Fact]
    public async Task MissingTokenMaterialIsAnActionableFault()
    {
        var repo = new InMemoryAccountRepository(NewInstagramAccount());
        var evaluator = new EvaluateAccountHealthUseCase(
            repo, new RecordingStore { Token = null }, StubInspector.Healthy, new FixedClock(Now));

        var result = await evaluator.ExecuteAsync(repo.Single().Id);

        Assert.True(result.Success, result.FailureCode);
        Assert.Equal(nameof(AccountHealth.Unhealthy), result.Health);
        Assert.NotNull(result.HealthDetail);
    }

    [Fact]
    public async Task DisconnectedOrUnknownAccountsAreNotFound()
    {
        var disconnected = ConnectedAccount.FromState(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "ig-2", ConnectionPath.FacebookLogin,
            ["instagram_business_basic"], AccountHealth.Connected, null, null, Now.AddDays(-9), Now.AddDays(-1));
        var repo = new InMemoryAccountRepository(disconnected);
        var evaluator = new EvaluateAccountHealthUseCase(repo, new RecordingStore(), StubInspector.Healthy, new FixedClock(Now));

        var forDisconnected = await evaluator.ExecuteAsync(disconnected.Id);
        var forUnknown = await evaluator.ExecuteAsync(Guid.CreateVersion7());

        Assert.False(forDisconnected.Success);
        Assert.False(forUnknown.Success);
        Assert.Equal(AccountFailures.NotFound, forDisconnected.FailureCode);
        Assert.Equal(AccountFailures.NotFound, forUnknown.FailureCode);
    }
}
