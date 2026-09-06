using Qasedak.BuildingBlocks.Application;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.Effects;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.UnitTests.TestSupport;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests.Effects;

/// <summary>
/// Deterministic coordinator tests (M13-009): the global semantic claim is acquired
/// before any provider mutation; every crash window (Reserved resume by owner only,
/// Attempting, Succeeded-without-run-save, timeout) replays from the ledger with ZERO
/// second Meta calls; deterministic local rejections happen before the claim.
/// </summary>
public sealed class CommentPrivateReplyCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Guid _workspaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid _accountId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly ConnectedAccount _account = ConnectedAccount.Create(
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "178414000000012345",
        ConnectionPath.InstagramLogin,
        ["instagram_business_basic", "instagram_business_manage_comments"],
        tokenExpiresAtUtc: new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero),
        connectedAtUtc: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));

    private sealed class FakeAccountRepository(ConnectedAccount? account) : IConnectedAccountRepository
    {
        public Task<ConnectedAccount?> FindByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(account);
        public Task<ConnectedAccount?> FindByProviderIdentityAsync(Guid workspaceId, string providerUserId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AccountResolution> ResolveActiveAccountAsync(string providerAccountId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ConnectedAccount>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ConnectedAccount>> ListActiveAsync(int limit, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DisconnectAsync(Guid accountId, DateTimeOffset disconnectedAtUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(ConnectedAccount account, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> TrySaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeTokenStore(string? token) : IProtectedTokenStore
    {
        public Task StoreAsync(Guid accountId, string accessToken, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> GetAsync(Guid accountId, CancellationToken ct = default) => Task.FromResult(token);
        public Task DeleteAsync(Guid accountId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeReferenceReader(Func<CommentReferenceReadResult> script) : ICommentReferenceReader
    {
        public int Calls { get; private set; }

        public Task<CommentReferenceReadResult> ReadCreatedAtUtcAsync(string accessToken, string commentId, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(script());
        }
    }

    /// <summary>In-memory ledger mirroring the PostgreSQL unique-index semantics.</summary>
    private sealed class InMemoryEffectLedger : ICommentEffectLedger
    {
        private readonly Dictionary<(Guid Account, string Comment, InstagramEffectType Effect), CommentEffectClaim> _claims = [];

        public void Seed(Guid account, string comment, InstagramEffectType effect, string owner, InstagramEffectStatus status,
            string? recipientId = null, string? messageId = null)
        {
            _claims[(account, comment, effect)] = new CommentEffectClaim(
                Guid.CreateVersion7(), account, comment, effect, owner, status,
                status == InstagramEffectStatus.Attempting ? Now : null,
                status is InstagramEffectStatus.Succeeded or InstagramEffectStatus.TerminalFailed or InstagramEffectStatus.Uncertain ? Now : null,
                recipientId, messageId, status == InstagramEffectStatus.TerminalFailed ? "privateReply.terminalFailed.rejectedByMeta" : null);
        }

        public Task<CommentEffectClaim?> ReserveAsync(CommentEffectClaimKey key, string ownerOperationId, DateTimeOffset nowUtc, CancellationToken ct = default)
        {
            var tuple = (key.ConnectedAccountId, key.ProviderCommentId, key.EffectType);
            if (_claims.TryGetValue(tuple, out var existing))
            {
                // Mirrors the PostgreSQL ledger: the same logical owner may resume ANY
                // state (the coordinator replays it); a competitor never may.
                return Task.FromResult(existing.OwnerOperationId == ownerOperationId
                    ? existing
                    : (CommentEffectClaim?)null);
            }

            var claim = new CommentEffectClaim(Guid.CreateVersion7(), key.ConnectedAccountId, key.ProviderCommentId, key.EffectType,
                ownerOperationId, InstagramEffectStatus.Reserved, null, null, null, null, null);
            _claims[tuple] = claim;
            return Task.FromResult<CommentEffectClaim?>(claim);
        }

        public Task<CommentEffectClaim?> FindAsync(CommentEffectClaimKey key, CancellationToken ct = default)
        {
            _claims.TryGetValue((key.ConnectedAccountId, key.ProviderCommentId, key.EffectType), out var claim);
            return Task.FromResult(claim);
        }

        public async Task MarkAttemptingAsync(Guid claimId, DateTimeOffset nowUtc, CancellationToken ct = default)
        {
            await MutateAsync(claimId, c => c with { Status = InstagramEffectStatus.Attempting, AttemptedAtUtc = nowUtc });
        }

        public async Task RecordSuccessAsync(Guid claimId, string providerRecipientId, string providerMessageId, DateTimeOffset nowUtc, CancellationToken ct = default)
        {
            await MutateAsync(claimId, c => c with
            {
                Status = InstagramEffectStatus.Succeeded,
                ProviderRecipientId = providerRecipientId,
                ProviderMessageId = providerMessageId,
                CompletedAtUtc = nowUtc,
            });
        }

        public async Task RecordTerminalFailureAsync(Guid claimId, string failureCode, DateTimeOffset nowUtc, CancellationToken ct = default)
        {
            await MutateAsync(claimId, c => c with { Status = InstagramEffectStatus.TerminalFailed, FailureCode = failureCode, CompletedAtUtc = nowUtc });
        }

        public async Task RecordUncertainAsync(Guid claimId, DateTimeOffset nowUtc, CancellationToken ct = default)
        {
            await MutateAsync(claimId, c => c with { Status = InstagramEffectStatus.Uncertain, CompletedAtUtc = nowUtc });
        }

        private async Task MutateAsync(Guid claimId, Func<CommentEffectClaim, CommentEffectClaim> mutate)
        {
            var key = _claims.Keys.Single(k => _claims[k].Id == claimId);
            _claims[key] = mutate(_claims[key]);
            await Task.CompletedTask;
        }
    }

    private sealed class RecordingPrivateReplyClient : ICommentPrivateReplyClient
    {
        private readonly Func<PrivateReplySendResult> _script;

        public RecordingPrivateReplyClient(Func<PrivateReplySendResult>? script = null) => _script = script ?? (() => PrivateReplySendResult.Ok("526-customer-1", "mid-1"));

        public List<(string AccessToken, string ProviderAccountId, string CommentId, string Text)> Calls { get; } = [];

        public Task<PrivateReplySendResult> SendPrivateReplyAsync(string accessToken, string providerAccountId, string commentId, string text, CancellationToken ct = default)
        {
            Calls.Add((accessToken, providerAccountId, commentId, text));
            return Task.FromResult(_script());
        }
    }

    private sealed class RecordingObservability : ICommentEffectObservability
    {
        public List<string> Events { get; } = [];

        public void ClaimAcquired(InstagramEffectType effectType) => Events.Add("claimAcquired");
        public void ClaimConflict(InstagramEffectType effectType) => Events.Add("claimConflict");
        public void PolicyRejected(InstagramEffectType effectType, string outcome) => Events.Add($"policyRejected:{outcome}");
        public void Attempted(InstagramEffectType effectType) => Events.Add("attempted");
        public void Succeeded(InstagramEffectType effectType) => Events.Add("succeeded");
        public void Failed(InstagramEffectType effectType, string failureCategory) => Events.Add($"failed:{failureCategory}");
        public void Uncertain(InstagramEffectType effectType) => Events.Add("uncertain");
    }

    private sealed record Harness(
        CommentPrivateReplyCoordinator Coordinator,
        RecordingPrivateReplyClient Client,
        InMemoryEffectLedger Ledger,
        FakeReferenceReader Reference,
        RecordingObservability Observability);

    private Harness CreateHarness(
        ConnectedAccount? account = null,
        string? token = "ig-user-token",
        CommentReferenceReadResult? reference = null,
        Func<PrivateReplySendResult>? sendScript = null)
    {
        var client = new RecordingPrivateReplyClient(sendScript);
        var ledger = new InMemoryEffectLedger();
        var reader = new FakeReferenceReader(() => reference ?? new CommentReferenceReadResult.Found(Now.AddDays(-1)));
        var observability = new RecordingObservability();
        var coordinator = new CommentPrivateReplyCoordinator(
            new FakeAccountRepository(account ?? _account),
            new FakeTokenStore(token),
            reader,
            ledger,
            client,
            new FixedClock(Now),
            observability);
        return new Harness(coordinator, client, ledger, reader, observability);
    }

    private CommentPrivateReplyCommand Command(string commentId = "comment-1", bool isLive = false) => new(
        _workspaceId, _accountId, commentId, isLive, "DM: thanks for asking about price!",
        "automation-1|1|event-1|0", Now.AddMinutes(-2));

    [Fact]
    public async Task DeliversByCommentIdAndRecordsSuccessIdentity()
    {
        var h = CreateHarness();

        var result = await h.Coordinator.ExecuteAsync(Command(), default);

        Assert.True(result.Delivered);
        Assert.Null(result.FailureCode);
        Assert.Equal("526-customer-1", result.ProviderRecipientId);
        Assert.Equal("mid-1", result.ProviderMessageId);

        var call = Assert.Single(h.Client.Calls);
        Assert.Equal("ig-user-token", call.AccessToken);
        Assert.Equal("178414000000012345", call.ProviderAccountId);
        Assert.Equal("comment-1", call.CommentId);
        Assert.Equal("DM: thanks for asking about price!", call.Text);

        var claim = await h.Ledger.FindAsync(new CommentEffectClaimKey(_accountId, "comment-1", InstagramEffectType.PrivateReply), default);
        Assert.NotNull(claim);
        Assert.Equal(InstagramEffectStatus.Succeeded, claim.Status);
        Assert.Equal("526-customer-1", claim.ProviderRecipientId);
        Assert.Equal("mid-1", claim.ProviderMessageId);
        Assert.Equal(["claimAcquired", "attempted", "succeeded"], h.Observability.Events);
    }

    [Fact]
    public async Task AlreadyClaimedByAnotherAutomationMakesZeroProviderCalls()
    {
        var h = CreateHarness();
        h.Ledger.Seed(_accountId, "comment-1", InstagramEffectType.PrivateReply, "automation-2|1|event-1|0", InstagramEffectStatus.Succeeded, "r-2", "m-2");

        var result = await h.Coordinator.ExecuteAsync(Command(), default);

        Assert.False(result.Delivered);
        Assert.Equal(PrivateReplyOutcomeCodes.AlreadyClaimed, result.FailureCode);
        Assert.Empty(h.Client.Calls);
        Assert.Equal(["claimConflict"], h.Observability.Events);
    }

    [Fact]
    public async Task SameOwnerReservedClaimResumesAndSendsExactlyOnce()
    {
        var h = CreateHarness();
        h.Ledger.Seed(_accountId, "comment-1", InstagramEffectType.PrivateReply, "automation-1|1|event-1|0", InstagramEffectStatus.Reserved);

        var result = await h.Coordinator.ExecuteAsync(Command(), default);

        Assert.True(result.Delivered);
        Assert.Single(h.Client.Calls);
        Assert.Equal(["claimAcquired", "attempted", "succeeded"], h.Observability.Events);
    }

    [Fact]
    public async Task CompetitorCannotStealReservedClaim()
    {
        var h = CreateHarness();
        h.Ledger.Seed(_accountId, "comment-1", InstagramEffectType.PrivateReply, "automation-1|1|event-1|0", InstagramEffectStatus.Reserved);

        var competitor = Command() with { OwnerOperationId = "automation-2|1|event-1|0" };
        var result = await h.Coordinator.ExecuteAsync(competitor, default);

        Assert.Equal(PrivateReplyOutcomeCodes.AlreadyClaimed, result.FailureCode);
        Assert.Empty(h.Client.Calls);
    }

    [Fact]
    public async Task SucceededEffectReplaysWithoutSecondCall()
    {
        var h = CreateHarness();
        h.Ledger.Seed(_accountId, "comment-1", InstagramEffectType.PrivateReply, "automation-1|1|event-1|0", InstagramEffectStatus.Succeeded, "r-kept", "m-kept");

        var result = await h.Coordinator.ExecuteAsync(Command(), default);

        Assert.True(result.Delivered);
        Assert.Equal("r-kept", result.ProviderRecipientId);
        Assert.Equal("m-kept", result.ProviderMessageId);
        Assert.Empty(h.Client.Calls);
        Assert.Equal(["claimAcquired"], h.Observability.Events);
    }

    [Fact]
    public async Task AttemptingStateReplaysUncertainWithoutSecondCall()
    {
        var h = CreateHarness();
        h.Ledger.Seed(_accountId, "comment-1", InstagramEffectType.PrivateReply, "automation-1|1|event-1|0", InstagramEffectStatus.Attempting);

        var result = await h.Coordinator.ExecuteAsync(Command(), default);

        Assert.Equal(PrivateReplyOutcomeCodes.Uncertain, result.FailureCode);
        Assert.Empty(h.Client.Calls);
        Assert.Equal(["claimAcquired"], h.Observability.Events);
    }

    [Fact]
    public async Task TerminalFailedEffectReplaysWithoutSecondCall()
    {
        var h = CreateHarness();
        h.Ledger.Seed(_accountId, "comment-1", InstagramEffectType.PrivateReply, "automation-1|1|event-1|0", InstagramEffectStatus.TerminalFailed);

        var result = await h.Coordinator.ExecuteAsync(Command(), default);

        // The replay surfaces the stored truthful code (terminal failure with category).
        Assert.StartsWith(PrivateReplyOutcomeCodes.TerminalFailed, result.FailureCode, StringComparison.Ordinal);
        Assert.Empty(h.Client.Calls);
    }

    [Fact]
    public async Task TransportTimeoutRecordsUncertainAndNeverRetries()
    {
        var h = CreateHarness(sendScript: () => PrivateReplySendResult.Fail(PrivateReplyFailureReason.TransportFailure, "timed out"));

        var first = await h.Coordinator.ExecuteAsync(Command(), default);
        Assert.Equal(PrivateReplyOutcomeCodes.Uncertain, first.FailureCode);

        // Redelivery/resume: zero second Meta call, same truthful outcome.
        var second = await h.Coordinator.ExecuteAsync(Command(), default);
        Assert.Equal(PrivateReplyOutcomeCodes.Uncertain, second.FailureCode);

        Assert.Single(h.Client.Calls);
        Assert.Equal(["claimAcquired", "attempted", "uncertain"], h.Observability.Events.Take(3));
        var claim = await h.Ledger.FindAsync(new CommentEffectClaimKey(_accountId, "comment-1", InstagramEffectType.PrivateReply), default);
        Assert.NotNull(claim);
        Assert.Equal(InstagramEffectStatus.Uncertain, claim.Status);
    }

    [Fact]
    public async Task ProviderRejectionIsTerminalAndNeverRetried()
    {
        var h = CreateHarness(sendScript: () => PrivateReplySendResult.Fail(PrivateReplyFailureReason.RejectedByMeta, "permission denied"));

        var first = await h.Coordinator.ExecuteAsync(Command(), default);
        Assert.Equal(PrivateReplyOutcomeCodes.TerminalFailed, first.FailureCode);

        var second = await h.Coordinator.ExecuteAsync(Command(), default);
        // The replay surfaces the stored truthful code (attempted + rejected).
        Assert.StartsWith(PrivateReplyOutcomeCodes.TerminalFailed, second.FailureCode, StringComparison.Ordinal);

        Assert.Single(h.Client.Calls);
        Assert.Equal(["claimAcquired", "attempted", "failed:rejectedByMeta"], h.Observability.Events.Take(3));
        var claim = await h.Ledger.FindAsync(new CommentEffectClaimKey(_accountId, "comment-1", InstagramEffectType.PrivateReply), default);
        Assert.NotNull(claim);
        Assert.Equal(InstagramEffectStatus.TerminalFailed, claim.Status);
    }

    [Fact]
    public async Task ExpiredCommentRejectsBeforeClaimAndMeta()
    {
        var h = CreateHarness(reference: new CommentReferenceReadResult.Found(Now.AddDays(-10)));

        var result = await h.Coordinator.ExecuteAsync(Command(), default);

        Assert.Equal(PrivateReplyOutcomeCodes.PolicyRejected + ".DefinitelyExpired", result.FailureCode);
        Assert.Empty(h.Client.Calls);
        Assert.Equal(["policyRejected:DefinitelyExpired"], h.Observability.Events);
    }

    [Fact]
    public async Task MissingCommentReferenceRejectsBeforeClaimAndMeta()
    {
        var h = CreateHarness(reference: new CommentReferenceReadResult.NotFound());

        var result = await h.Coordinator.ExecuteAsync(Command(), default);

        Assert.Equal(PrivateReplyOutcomeCodes.PolicyRejected + ".commentNotFound", result.FailureCode);
        Assert.Empty(h.Client.Calls);
        Assert.DoesNotContain(h.Observability.Events, e => e.StartsWith("claim", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnavailableReferenceFallsBackToNotificationGuard()
    {
        var h = CreateHarness(reference: new CommentReferenceReadResult.Unavailable());

        var result = await h.Coordinator.ExecuteAsync(Command(), default);

        // Notification is 2 minutes old → not provably expired → attempt once, Meta final.
        Assert.True(result.Delivered);
        Assert.Single(h.Client.Calls);
    }

    [Fact]
    public async Task MissingTokenFailsBeforeClaimAndMeta()
    {
        var h = CreateHarness(token: null);

        var result = await h.Coordinator.ExecuteAsync(Command(), default);

        Assert.Equal(PrivateReplyOutcomeCodes.AccountUnavailable, result.FailureCode);
        Assert.Empty(h.Client.Calls);
        Assert.Empty(h.Observability.Events);
    }

    [Fact]
    public async Task DisconnectedAccountFailsBeforeClaimAndMeta()
    {
        var disconnected = ConnectedAccount.FromState(
            _accountId, _workspaceId, "178414000000012345", ConnectionPath.InstagramLogin,
            ["instagram_business_basic"], AccountHealth.Connected, null, null,
            Now.AddDays(-30), disconnectedAtUtc: Now);
        var h = CreateHarness(account: disconnected);

        var result = await h.Coordinator.ExecuteAsync(Command(), default);

        Assert.Equal(PrivateReplyOutcomeCodes.AccountUnavailable, result.FailureCode);
        Assert.Empty(h.Client.Calls);
    }

    [Fact]
    public async Task WrongWorkspaceAccountFailsBeforeClaimAndMeta()
    {
        var otherWorkspace = ConnectedAccount.Create(
            _accountId,
            Guid.Parse("99999999-9999-9999-9999-999999999999"),
            "178414000000012345",
            ConnectionPath.InstagramLogin,
            ["instagram_business_basic"],
            tokenExpiresAtUtc: Now.AddDays(30),
            connectedAtUtc: Now.AddDays(-30));
        var h = CreateHarness(account: otherWorkspace);

        var result = await h.Coordinator.ExecuteAsync(Command(), default);

        Assert.Equal(PrivateReplyOutcomeCodes.AccountUnavailable, result.FailureCode);
        Assert.Empty(h.Client.Calls);
    }

    [Fact]
    public async Task MissingCommentIdFailsBeforeClaimAndMeta()
    {
        var h = CreateHarness();

        var result = await h.Coordinator.ExecuteAsync(Command(commentId: null!), default);

        Assert.Equal(PrivateReplyOutcomeCodes.PolicyRejected + ".missingOrigin", result.FailureCode);
        Assert.Empty(h.Client.Calls);
    }

    [Fact]
    public async Task LiveCommentIsAttemptedOnceNeverUsingSevenDayRule()
    {
        // Live comment whose creation time is 6 days old: the 7-day rule must NOT apply;
        // the broadcast is Meta's call. Attempt exactly once.
        var h = CreateHarness(reference: new CommentReferenceReadResult.Found(Now.AddDays(-6)));

        var result = await h.Coordinator.ExecuteAsync(Command(isLive: true), default);

        Assert.True(result.Delivered);
        Assert.Single(h.Client.Calls);

        // Never a second call on resume.
        await h.Coordinator.ExecuteAsync(Command(isLive: true), default);
        Assert.Single(h.Client.Calls);
    }

    [Fact]
    public async Task PublicReplyEffectIsIndependentOfPrivateReply()
    {
        var h = CreateHarness();
        h.Ledger.Seed(_accountId, "comment-1", InstagramEffectType.PublicCommentReply, "some-owner", InstagramEffectStatus.Succeeded, "r-pub", "m-pub");

        // A consumed PublicReply claim must not suppress the PrivateReply on the same comment.
        var result = await h.Coordinator.ExecuteAsync(Command(), default);

        Assert.True(result.Delivered);
        Assert.Single(h.Client.Calls);
    }
}
