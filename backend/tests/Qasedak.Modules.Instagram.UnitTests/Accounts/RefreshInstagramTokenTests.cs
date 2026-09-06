using Qasedak.BuildingBlocks.Application;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.OAuth;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.UnitTests.TestSupport;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests.Accounts;

/// <summary>
/// M13-005: token-refresh execution semantics — rotation atomicity, stale
/// concurrency, permanent-vs-transient classification, age rule and redaction.
/// No live Meta calls; the OAuth client and inspector are scripted.
/// </summary>
public sealed class RefreshInstagramTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid WorkspaceId = Guid.Parse("01914c8e-0000-7000-8000-000000000002");

    private sealed class FakeRepository : IConnectedAccountRepository
    {
        public Dictionary<Guid, ConnectedAccount> Rows { get; } = [];

        public bool SaveConflict { get; set; }

        public Task<ConnectedAccount?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.GetValueOrDefault(id));

        public Task<ConnectedAccount?> FindByProviderIdentityAsync(Guid workspaceId, string providerUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.Values.FirstOrDefault(a => a.WorkspaceId == workspaceId && a.ProviderUserId == providerUserId));

        public Task<AccountResolution> ResolveActiveAccountAsync(string providerAccountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(AccountResolution.NotFound());

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

        public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(!SaveConflict);

        public Task<bool> DisconnectAsync(Guid accountId, DateTimeOffset disconnectedAtUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class FakeTokenStore : IProtectedTokenStore
    {
        public Dictionary<Guid, string> Tokens { get; } = [];

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
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOAuthClient : IMetaOAuthClient
    {
        public LongLivedTokenResult RefreshResult { get; set; } =
            LongLivedTokenResult.Ok(new("ROTATED-TOKEN", 60 * 24 * 3600L));

        public int RefreshCalls { get; private set; }

        public Task<CodeExchangeResult> ExchangeCodeAsync(CodeExchangeRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LongLivedTokenResult> ExchangeShortLivedForLongLivedAsync(string shortLivedAccessToken, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LongLivedTokenResult> RefreshLongLivedAsync(string longLivedAccessToken, CancellationToken cancellationToken = default)
        {
            RefreshCalls++;
            return Task.FromResult(RefreshResult);
        }
    }

    private sealed class FakeInspector : IMetaTokenInspector
    {
        public TokenInspection Result { get; set; } = TokenInspection.Healthy();

        public int Calls { get; private set; }

        public Task<TokenInspection> InspectAsync(string accessToken, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private static ConnectedAccount Connected(FakeRepository repo, FakeTokenStore tokens, DateTimeOffset? issuedAt = null)
    {
        var connectedAt = issuedAt ?? Now.AddDays(-50);
        var account = ConnectedAccount.Create(
            Guid.CreateVersion7(), WorkspaceId, "ig-7",
            ConnectionPath.InstagramLogin, ["instagram_business_basic"],
            connectedAt.AddDays(60), connectedAt);
        repo.Rows[account.Id] = account;
        tokens.Tokens[account.Id] = "CURRENT-TOKEN";
        return account;
    }

    private static RefreshInstagramTokenUseCase NewSut(
        FakeRepository repo, FakeTokenStore tokens, FakeOAuthClient oauth, FakeInspector inspector) =>
        new(repo, tokens, oauth, inspector, new FixedClock(Now));

    [Fact]
    public async Task SuccessfulRefreshRotatesCiphertextExpiryAndHealthAtomically()
    {
        var repo = new FakeRepository();
        var tokens = new FakeTokenStore();
        var oauth = new FakeOAuthClient();
        var account = Connected(repo, tokens);
        var sut = NewSut(repo, tokens, oauth, new FakeInspector());

        var outcome = await sut.ExecuteAsync(account.Id);

        var rotated = Assert.IsType<TokenRefreshOutcome.Rotated>(outcome);
        Assert.Equal(Now.AddSeconds(60 * 24 * 3600L), rotated.NewExpiryUtc);
        Assert.Equal("ROTATED-TOKEN", await tokens.GetAsync(account.Id));
        Assert.Equal(rotated.NewExpiryUtc, account.TokenExpiresAtUtc);
        Assert.Equal(AccountHealth.Connected, account.Health);
        Assert.Equal(1u, account.Version);
        Assert.Equal(Now, account.LastTokenIssuedAtUtc);
    }

    [Fact]
    public async Task UnknownAccountIsSkippedWithoutMetaCall()
    {
        var oauth = new FakeOAuthClient();
        var sut = NewSut(new FakeRepository(), new FakeTokenStore(), oauth, new FakeInspector());

        var outcome = await sut.ExecuteAsync(Guid.CreateVersion7());

        var skipped = Assert.IsType<TokenRefreshOutcome.Skipped>(outcome);
        Assert.Equal(AccountFailures.NotFound, skipped.FailureCode);
        Assert.Equal(0, oauth.RefreshCalls);
    }

    [Fact]
    public async Task DisconnectedAccountIsSkippedWithoutMetaCall()
    {
        var repo = new FakeRepository();
        var tokens = new FakeTokenStore();
        var oauth = new FakeOAuthClient();
        var account = Connected(repo, tokens);
        account.Disconnect(Now);
        var sut = NewSut(repo, tokens, oauth, new FakeInspector());

        var outcome = await sut.ExecuteAsync(account.Id);

        var skipped = Assert.IsType<TokenRefreshOutcome.Skipped>(outcome);
        Assert.Equal(AccountFailures.AlreadyDisconnected, skipped.FailureCode);
        Assert.Equal(0, oauth.RefreshCalls);
    }

    [Fact]
    public async Task NeverExpiringAccountIsSkippedAsNotRequired()
    {
        var repo = new FakeRepository();
        var tokens = new FakeTokenStore();
        var oauth = new FakeOAuthClient();
        var account = ConnectedAccount.FromState(
            Guid.CreateVersion7(), WorkspaceId, "page-1", ConnectionPath.FacebookLogin,
            ["pages_show_list"], AccountHealth.Connected, null, null, Now.AddDays(-90), null);
        repo.Rows[account.Id] = account;
        tokens.Tokens[account.Id] = "PAGE-TOKEN";
        var sut = NewSut(repo, tokens, oauth, new FakeInspector());

        var outcome = await sut.ExecuteAsync(account.Id);

        var skipped = Assert.IsType<TokenRefreshOutcome.Skipped>(outcome);
        Assert.Equal(AccountFailures.RefreshNotRequired, skipped.FailureCode);
        Assert.Equal(0, oauth.RefreshCalls);
    }

    [Fact]
    public async Task MissingTokenMaterialIsPermanentAndActionable()
    {
        var repo = new FakeRepository();
        var tokens = new FakeTokenStore();
        var account = Connected(repo, tokens);
        tokens.Tokens.Remove(account.Id);
        var sut = NewSut(repo, tokens, new FakeOAuthClient(), new FakeInspector());

        var outcome = await sut.ExecuteAsync(account.Id);

        var permanent = Assert.IsType<TokenRefreshOutcome.Permanent>(outcome);
        Assert.Equal(AccountFailures.TokenMissing, permanent.FailureCode);
        Assert.Equal(AccountHealth.Unhealthy, account.Health);
        Assert.NotNull(account.HealthDetail);
    }

    [Fact]
    public async Task TokenYoungerThanMinimumAgeBacksOffWithoutMetaCall()
    {
        var repo = new FakeRepository();
        var tokens = new FakeTokenStore();
        var oauth = new FakeOAuthClient();
        // Issued one hour ago: Meta would reject the refresh, so back off instead.
        var account = Connected(repo, tokens, issuedAt: Now.AddHours(-1));
        var sut = NewSut(repo, tokens, oauth, new FakeInspector());

        var outcome = await sut.ExecuteAsync(account.Id);

        Assert.IsType<TokenRefreshOutcome.Retryable>(outcome);
        Assert.Equal(0, oauth.RefreshCalls);
        Assert.Equal(AccountHealth.Connected, account.Health);
    }

    [Fact]
    public async Task TransportFailureIsRetryableAndLeavesHealthUntouched()
    {
        var repo = new FakeRepository();
        var tokens = new FakeTokenStore();
        var oauth = new FakeOAuthClient
        {
            RefreshResult = LongLivedTokenResult.Fail(new(MetaOAuthFailureReason.TransportFailure, "down")),
        };
        var inspector = new FakeInspector();
        var account = Connected(repo, tokens);
        var sut = NewSut(repo, tokens, oauth, inspector);

        var outcome = await sut.ExecuteAsync(account.Id);

        var retryable = Assert.IsType<TokenRefreshOutcome.Retryable>(outcome);
        Assert.Equal(AccountFailures.OAuthUnavailable, retryable.FailureCode);
        Assert.Equal(AccountHealth.Connected, account.Health);
        Assert.Equal(0, inspector.Calls);
    }

    [Theory]
    [InlineData(TokenInspectionKind.Expired, AccountHealth.Expired, "account.tokenExpired", false)]
    [InlineData(TokenInspectionKind.Revoked, AccountHealth.Revoked, "account.oauthRejected", true)]
    [InlineData(TokenInspectionKind.PermissionLoss, AccountHealth.Unhealthy, "account.oauthRejected", true)]
    public async Task RejectedRefreshClassifiesPermanentHealthPrecisely(
        TokenInspectionKind kind, AccountHealth expectedHealth, string expectedCode, bool expectDetail)
    {
        var repo = new FakeRepository();
        var tokens = new FakeTokenStore();
        var oauth = new FakeOAuthClient
        {
            RefreshResult = LongLivedTokenResult.Fail(new(MetaOAuthFailureReason.RejectedByMeta, "190")),
        };
        var inspector = new FakeInspector { Result = TokenInspection.From(kind, "classified") };
        var account = Connected(repo, tokens);
        var sut = NewSut(repo, tokens, oauth, inspector);

        var outcome = await sut.ExecuteAsync(account.Id);

        var permanent = Assert.IsType<TokenRefreshOutcome.Permanent>(outcome);
        Assert.Equal(expectedCode, permanent.FailureCode);
        Assert.Equal(expectedHealth, account.Health);
        Assert.Equal(expectDetail, account.HealthDetail is not null);
    }

    [Fact]
    public async Task RejectedRefreshWithTransientInspectionStaysRetryable()
    {
        var repo = new FakeRepository();
        var tokens = new FakeTokenStore();
        var oauth = new FakeOAuthClient
        {
            RefreshResult = LongLivedTokenResult.Fail(new(MetaOAuthFailureReason.RejectedByMeta, "190")),
        };
        var inspector = new FakeInspector { Result = TokenInspection.From(TokenInspectionKind.Transient, "noise") };
        var account = Connected(repo, tokens);
        var sut = NewSut(repo, tokens, oauth, inspector);

        var outcome = await sut.ExecuteAsync(account.Id);

        Assert.IsType<TokenRefreshOutcome.Retryable>(outcome);
        Assert.Equal(AccountHealth.Connected, account.Health);
    }

    [Fact]
    public async Task LostConcurrencyRaceSurfacesAsStaleInsteadOfOverwrite()
    {
        var repo = new FakeRepository { SaveConflict = true };
        var tokens = new FakeTokenStore();
        var account = Connected(repo, tokens);
        var sut = NewSut(repo, tokens, new FakeOAuthClient(), new FakeInspector());

        var outcome = await sut.ExecuteAsync(account.Id);

        Assert.IsType<TokenRefreshOutcome.Stale>(outcome);
    }
}
