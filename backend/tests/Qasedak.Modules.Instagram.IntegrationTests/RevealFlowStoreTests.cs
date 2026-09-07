using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Instagram.Application.RevealFlow;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Modules.Instagram.IntegrationTests;

/// <summary>
/// Real-PostgreSQL tests for the durable reveal-flow continuation (M13-011 §68): the
/// (ConnectedAccountId, ProviderCommentId) unique index makes exactly one flow per
/// logical origin; the Revealing CAS is the single-reveal authority under concurrency;
/// every transition survives process restarts (fresh DbContext scopes); a crash window
/// (Attempting without persisted outcome) never yields a second authority; correlation
/// token hashes are globally unique and the raw token never persists.
/// </summary>
[Collection(PostgresTestEnvironment.Name)]
public sealed class RevealFlowStoreTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly RevealFlowContent Content = new(
        "opening text", "gate prompt", "Continue", null, null, "reveal text");

    private InstagramDbContext NewContext() =>
        new(new DbContextOptionsBuilder<InstagramDbContext>()
            .UseNpgsql(fixture.Context.Database.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", InstagramDbContext.Schema))
            .Options);

    private static async Task<Guid> SeedOpenFlowAsync(InstagramDbContext context, Guid accountId, string commentId, string participant)
    {
        var store = new EfRevealFlowStore(context);
        var flowId = RevealFlowId.For(accountId, commentId);
        var flow = await store.CreateOrGetAsync(flowId, Guid.CreateVersion7(), accountId, commentId, participant,
            isLiveComment: false, Now.AddMinutes(-5), FollowGateMode.Disabled, Content,
            automationId: null, automationVersionNumber: null, triggerEventId: null, actionIndex: null, Now, default);
        Assert.Equal(flowId, flow.Id);
        Assert.True(await store.TryMarkOpeningAttemptedAsync(flowId, Now, default));
        Assert.True(await store.TryOpenAsync(flowId, "mid-opening", "526-" + participant, "526-" + participant, Now, default));
        Assert.True(await store.TryAdvanceUserResponseAsync(flowId, Now.AddMinutes(-1), default));
        return flowId;
    }

    [Fact]
    public async Task ConcurrentCreatesForSameOriginYieldExactlyOneRow()
    {
        var accountId = Guid.CreateVersion7();
        var flowId = RevealFlowId.For(accountId, "comment-x");
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var context = NewContext();
            var store = new EfRevealFlowStore(context);
            return await store.CreateOrGetAsync(flowId, Guid.CreateVersion7(), accountId, "comment-x", "participant-x",
                isLiveComment: false, Now, FollowGateMode.Disabled, Content, null, null, null, null, Now, default);
        }));

        Assert.All(outcomes, flow => Assert.Equal(flowId, flow.Id));
        await using var verify = NewContext();
        Assert.Single(await verify.RevealFlows.Where(r => r.ConnectedAccountId == accountId).ToListAsync());
    }

    [Fact]
    public async Task ConcurrentRevealAuthorityHasExactlyOneWinner()
    {
        var accountId = Guid.CreateVersion7();
        var participant = "participant-c";
        await using (var seed = NewContext())
        {
            var flowId = await SeedOpenFlowAsync(seed, accountId, "comment-c", participant);
            var store = new EfRevealFlowStore(seed);
            var token = RevealCorrelation.Create();
            Assert.True(await store.TryStartGatePromptAsync(flowId, token.Sha256Hash, Now, default));
            Assert.True(await store.RecordGatePromptSuccessAsync(flowId, "mid-gate", Now, default));
        }

        var flowId2 = RevealFlowId.For(accountId, "comment-c");
        var winners = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var context = NewContext();
            var store = new EfRevealFlowStore(context);
            return await store.TryMarkRevealingAsync(flowId2, Now, default);
        }));

        Assert.Equal(1, winners.Count(w => w));
    }

    [Fact]
    public async Task CrashWindowAfterAttemptNeverYieldsASecondAuthority()
    {
        var accountId = Guid.CreateVersion7();
        var participant = "participant-r";
        var flowId = await SeedOpenFlowAsync(NewContext(), accountId, "comment-r", participant);

        // The crash window: gate attempt marked but outcome never recorded — restart.
        await using (var first = NewContext())
        {
            var store = new EfRevealFlowStore(first);
            var token = RevealCorrelation.Create();
            Assert.True(await store.TryStartGatePromptAsync(flowId, token.Sha256Hash, Now, default));
        }

        // Restart: redelivery of the user message must NOT issue a second prompt (CAS fails).
        await using var second = NewContext();
        var restarted = new EfRevealFlowStore(second);
        var anotherToken = RevealCorrelation.Create();
        Assert.False(await restarted.TryStartGatePromptAsync(flowId, anotherToken.Sha256Hash, Now, default));

        var flow = await restarted.GetByIdAsync(flowId);
        Assert.Equal(RevealFlowState.PreparingGatePrompt, flow!.State);
        Assert.Equal(RevealGatePromptStatus.Attempting, flow.GatePromptStatus);
    }

    [Fact]
    public async Task RevealSuccessPersistsProviderIdentityForReplayAfterRestart()
    {
        var accountId = Guid.CreateVersion7();
        var participant = "participant-s";
        var flowId = await SeedOpenFlowAsync(NewContext(), accountId, "comment-s", participant);

        await using (var first = NewContext())
        {
            var store = new EfRevealFlowStore(first);
            var token = RevealCorrelation.Create();
            Assert.True(await store.TryStartGatePromptAsync(flowId, token.Sha256Hash, Now, default));
            Assert.True(await store.RecordGatePromptSuccessAsync(flowId, "mid-gate", Now, default));
            Assert.True(await store.TryMarkRevealingAsync(flowId, Now, default));
            Assert.True(await store.RecordRevealSuccessAsync(flowId, "mid-reveal", Now, default));
        }

        // Restart: the durable outcome is replayed — Revealed with the stored identity.
        await using var restarted = NewContext();
        var flow = await new EfRevealFlowStore(restarted).GetByIdAsync(flowId);
        Assert.Equal(RevealFlowState.Revealed, flow!.State);
        Assert.Equal(RevealSendStatus.Succeeded, flow.RevealStatus);
        Assert.Equal("mid-reveal", flow.RevealProviderMessageId);
        Assert.Equal("526-" + participant, flow.ParticipantIGSID);
        Assert.Equal("mid-opening", flow.OpeningPrivateReplyMessageId);
    }

    [Fact]
    public async Task CorrelationTokenHashesAreGloballyUniqueAndRawTokensNeverPersist()
    {
        var accountId = Guid.CreateVersion7();
        var token = RevealCorrelation.Create();

        await using (var first = NewContext())
        {
            var store = new EfRevealFlowStore(first);
            var flowId = await SeedOpenFlowAsync(first, accountId, "comment-u1", "participant-u1");
            Assert.True(await store.TryStartGatePromptAsync(flowId, token.Sha256Hash, Now, default));
            Assert.True(await store.RecordGatePromptSuccessAsync(flowId, "mid-gate", Now, default));
        }

        // A second flow attempting the same hash fails closed (unique index backstop).
        await using var second = NewContext();
        var secondStore = new EfRevealFlowStore(second);
        var secondFlowId = await SeedOpenFlowAsync(second, accountId, "comment-u2", "participant-u2");
        Assert.False(await secondStore.TryStartGatePromptAsync(secondFlowId, token.Sha256Hash, Now, default));

        // Lookup by hash resolves the owning flow; the raw token is nowhere in the row.
        var owner = await secondStore.GetByCorrelationTokenHashAsync(token.Sha256Hash);
        Assert.NotNull(owner);
        Assert.Equal("comment-u1", owner!.ProviderCommentId);
        var raw = await second.RevealFlows.AsNoTracking().Where(r => r.Id == owner.Id).Select(r => r.CorrelationTokenHash).SingleAsync();
        Assert.Equal(token.Sha256Hash, raw);
    }

    [Fact]
    public async Task ReplyToCorrelationAndPendingScansAreDurableAcrossRestarts()
    {
        var accountId = Guid.CreateVersion7();
        var flowId = await SeedOpenFlowAsync(NewContext(), accountId, "comment-p", "participant-p");

        await using var restarted = NewContext();
        var store = new EfRevealFlowStore(restarted);

        var byOpening = await store.GetByOpeningMessageIdAsync(accountId, "mid-opening", "526-participant-p");
        Assert.NotNull(byOpening);
        Assert.Equal(flowId, byOpening!.Id);

        var pending = await store.GetAwaitingResponseAsync(accountId, "526-participant-p");
        Assert.Single(pending);
    }

    [Fact]
    public async Task TerminalTransitionsAreIrreversibleFromTerminalStates()
    {
        var accountId = Guid.CreateVersion7();
        var flowId = await SeedOpenFlowAsync(NewContext(), accountId, "comment-t", "participant-t");

        await using var context = NewContext();
        var store = new EfRevealFlowStore(context);
        Assert.True(await store.TryTerminalAsync(flowId, RevealFlowState.Expired, RevealFlowFailures.WindowExpired, Now, default));

        // A later terminal write must not resurrect or overwrite the terminal outcome.
        Assert.False(await store.TryTerminalAsync(flowId, RevealFlowState.TerminalFailed, "reveal.x", Now, default));
        Assert.False(await store.TryMarkRevealingAsync(flowId, Now, default));

        var flow = await store.GetByIdAsync(flowId);
        Assert.Equal(RevealFlowState.Expired, flow!.State);
        Assert.Equal(RevealFlowFailures.WindowExpired, flow.FailureCode);
    }
}
