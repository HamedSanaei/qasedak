using Microsoft.Extensions.Logging;
using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Application.Scheduling;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.OAuth;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.Infrastructure.Refresh;
using Qasedak.Modules.Instagram.UnitTests.TestSupport;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests.Refresh;

/// <summary>
/// M13-005: scheduled refresh handler — payload parsing, outcome mapping and
/// exactly-one next-occurrence chaining. Payloads carry identifiers only.
/// </summary>
public sealed class TokenRefreshScheduledHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

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
        public Task StoreAsync(Guid accountId, string accessToken, CancellationToken cancellationToken = default)
        {
            tokens[accountId] = accessToken;
            return Task.CompletedTask;
        }

        public Task<string?> GetAsync(Guid accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(tokens.GetValueOrDefault(accountId));

        public Task DeleteAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            tokens.Remove(accountId);
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

        public Task<TokenInspection> InspectAsync(string accessToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);
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

    private sealed class NullHandlerLogger : ILogger<TokenRefreshScheduledHandler>
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
            ConnectionPath.InstagramLogin, ["instagram_business_basic"],
            Now.AddDays(10), Now.AddDays(-50));
        repo.Rows[account.Id] = account;
        tokens[account.Id] = "CURRENT-TOKEN";
        return account;
    }

    private static ScheduledWorkItem ItemFor(Guid accountId, string payloadJson) =>
        new(Guid.CreateVersion7(), TokenRefreshPolicy.JobType,
            TokenRefreshPolicy.IdempotencyKey(accountId, Now.AddDays(10)), payloadJson, 1,
            accountId, WorkspaceId, Now, ScheduledWorkStatus.Claimed, 1, 8, Now,
            null, "test-worker", Now.AddMinutes(5), Now.AddDays(-7), Now, Now.AddMinutes(-1), null);

    private static (TokenRefreshScheduledHandler Handler, FakeOAuthClient OAuth, FakeWorkStore Jobs) NewSut(
        FakeRepository repo, Dictionary<Guid, string> tokens, FakeOAuthClient oauth, FakeInspector inspector)
    {
        var jobs = new FakeWorkStore();
        var refresh = new RefreshInstagramTokenUseCase(repo, new FakeTokenStore(tokens), oauth, inspector, new FixedClock(Now));
        return (new TokenRefreshScheduledHandler(refresh, jobs, new FixedClock(Now), new NullHandlerLogger()), oauth, jobs);
    }

    [Fact]
    public void HandlerServesTheDocumentedJobType()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var (handler, _, _) = NewSut(repo, tokens, new FakeOAuthClient(), new FakeInspector());

        Assert.Equal("instagram.token-refresh", handler.WorkType);
    }

    [Fact]
    public async Task MalformedPayloadIsPermanentWithoutTouchingMeta()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var oauth = new FakeOAuthClient();
        var (handler, _, jobs) = NewSut(repo, tokens, oauth, new FakeInspector());

        var outcome = await handler.HandleAsync(ItemFor(Guid.CreateVersion7(), """{"nope":1}"""), CancellationToken.None);

        var permanent = Assert.IsType<WorkOutcome.Permanent>(outcome);
        Assert.Equal("refresh.malformedPayload", permanent.FailureCode);
        Assert.Equal(0, oauth.RefreshCalls);
        Assert.Empty(jobs.Enqueued);
    }

    [Fact]
    public async Task RotationSucceedsAndChainsExactlyOneNextOccurrence()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var account = Connected(repo, tokens);
        var (handler, _, jobs) = NewSut(repo, tokens, new FakeOAuthClient(), new FakeInspector());
        var expectedExpiry = Now.AddSeconds(60 * 24 * 3600L);

        var outcome = await handler.HandleAsync(ItemFor(account.Id, TokenRefreshPolicy.RefreshPayload(account.Id)), CancellationToken.None);

        Assert.IsType<WorkOutcome.Succeeded>(outcome);
        var next = Assert.Single(jobs.Enqueued);
        Assert.Equal(TokenRefreshPolicy.JobType, next.WorkType);
        Assert.Equal(account.Id, next.ConnectedAccountId);
        Assert.Equal(WorkspaceId, next.WorkspaceId);
        Assert.Equal(TokenRefreshPolicy.IdempotencyKey(account.Id, expectedExpiry), next.IdempotencyKey);
        Assert.Equal(TokenRefreshPolicy.NextDueAt(expectedExpiry, Now), next.DueAtUtc);
        Assert.Equal(TokenRefreshPolicy.DefaultMaxAttempts, next.MaxAttempts);
        Assert.DoesNotContain("ROTATED-TOKEN", next.PayloadJson);
        Assert.DoesNotContain("CURRENT-TOKEN", next.PayloadJson);
        Assert.Equal(account.Id, TokenRefreshPolicy.ParseRefreshPayload(next.PayloadJson));
    }

    [Fact]
    public async Task DisconnectedAccountSkipsPermanentlyWithoutChaining()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var account = Connected(repo, tokens);
        account.Disconnect(Now);
        var (handler, oauth, jobs) = NewSut(repo, tokens, new FakeOAuthClient(), new FakeInspector());

        var outcome = await handler.HandleAsync(ItemFor(account.Id, TokenRefreshPolicy.RefreshPayload(account.Id)), CancellationToken.None);

        var permanent = Assert.IsType<WorkOutcome.Permanent>(outcome);
        Assert.Equal(AccountFailures.AlreadyDisconnected, permanent.FailureCode);
        Assert.Equal(0, oauth.RefreshCalls);
        Assert.Empty(jobs.Enqueued);
    }

    [Fact]
    public async Task TransientFailureIsRetryableWithoutChaining()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var account = Connected(repo, tokens);
        var oauth = new FakeOAuthClient
        {
            RefreshResult = LongLivedTokenResult.Fail(new(MetaOAuthFailureReason.TransportFailure, "down")),
        };
        var (handler, _, jobs) = NewSut(repo, tokens, oauth, new FakeInspector());

        var outcome = await handler.HandleAsync(ItemFor(account.Id, TokenRefreshPolicy.RefreshPayload(account.Id)), CancellationToken.None);

        var retryable = Assert.IsType<WorkOutcome.Retryable>(outcome);
        Assert.Equal(AccountFailures.OAuthUnavailable, retryable.FailureCode);
        Assert.Empty(jobs.Enqueued);
    }

    [Fact]
    public async Task PermanentFailureTerminatesWithoutChaining()
    {
        var repo = new FakeRepository();
        var tokens = new Dictionary<Guid, string>();
        var account = Connected(repo, tokens);
        var oauth = new FakeOAuthClient
        {
            RefreshResult = LongLivedTokenResult.Fail(new(MetaOAuthFailureReason.RejectedByMeta, "190")),
        };
        var inspector = new FakeInspector { Result = TokenInspection.From(TokenInspectionKind.Revoked, "gone") };
        var (handler, _, jobs) = NewSut(repo, tokens, oauth, inspector);

        var outcome = await handler.HandleAsync(ItemFor(account.Id, TokenRefreshPolicy.RefreshPayload(account.Id)), CancellationToken.None);

        var permanent = Assert.IsType<WorkOutcome.Permanent>(outcome);
        Assert.Equal(AccountFailures.OAuthRejected, permanent.FailureCode);
        Assert.Empty(jobs.Enqueued);
    }
}
