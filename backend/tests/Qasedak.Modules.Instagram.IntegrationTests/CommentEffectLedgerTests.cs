using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Instagram.Application.Effects;
using Qasedak.Modules.Instagram.Infrastructure.Effects;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Modules.Instagram.IntegrationTests;

/// <summary>
/// Real-PostgreSQL tests for the global semantic effect claim (M13-009 §79): the unique
/// index on (ConnectedAccountId, ProviderCommentId, EffectType) makes exactly one of any
/// number of concurrent candidates win; scope keeps accounts and effect types independent;
/// restart-safe state machine (Reserved resume by owner, irreversible Attempting, success
/// reconciliation, Uncertain, terminal failure) is proven across fresh DbContext scopes;
/// rows never carry token material or raw provider responses.
/// </summary>
[Collection(PostgresTestEnvironment.Name)]
public sealed class CommentEffectLedgerTests(PostgreSqlFixture fixture)
{
    private const string OwnerA = "automation-A|1|event-1|0";

    private const string OwnerB = "automation-B|1|event-1|0";

    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private InstagramDbContext NewContext() =>
        new(new DbContextOptionsBuilder<InstagramDbContext>()
            .UseNpgsql(fixture.Context.Database.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", InstagramDbContext.Schema))
            .Options);

    private static CommentEffectClaimKey Key(Guid accountId, string commentId = "comment-1", InstagramEffectType effect = InstagramEffectType.PrivateReply) =>
        new(accountId, commentId, effect);

    [Fact]
    public async Task FirstClaimAcquiresReservedState()
    {
        var accountId = Guid.CreateVersion7();
        await using var context = NewContext();
        var ledger = new EfCommentEffectLedger(context);

        var claim = await ledger.ReserveAsync(Key(accountId), OwnerA, Now, default);

        Assert.NotNull(claim);
        Assert.Equal(InstagramEffectStatus.Reserved, claim!.Status);
        Assert.Equal(OwnerA, claim.OwnerOperationId);
    }

    [Fact]
    public async Task DuplicateSameKeyLosesForAnotherOwner()
    {
        var accountId = Guid.CreateVersion7();
        await using var context = NewContext();
        var ledger = new EfCommentEffectLedger(context);

        var first = await ledger.ReserveAsync(Key(accountId), OwnerA, Now, default);
        Assert.NotNull(first);

        var second = await ledger.ReserveAsync(Key(accountId), OwnerB, Now, default);

        Assert.Null(second);
    }

    [Fact]
    public async Task ConcurrentSameKeyHasExactlyOneWinner()
    {
        var accountId = Guid.CreateVersion7();
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            await using var context = NewContext();
            var ledger = new EfCommentEffectLedger(context);
            return await ledger.ReserveAsync(Key(accountId), $"owner-{i}|1|e|0", Now, default);
        }));

        var winners = outcomes.Where(c => c is not null).ToList();
        Assert.Single(winners);
        Assert.Equal(InstagramEffectStatus.Reserved, winners[0]!.Status);
    }

    [Fact]
    public async Task TwoMatchingAutomationsConcurrentlyProduceOneGlobalWinner()
    {
        var accountId = Guid.CreateVersion7();
        var results = await Task.WhenAll(
            Task.Run(async () =>
            {
                await using var context = NewContext();
                return await new EfCommentEffectLedger(context).ReserveAsync(Key(accountId), OwnerA, Now, default);
            }),
            Task.Run(async () =>
            {
                await using var context = NewContext();
                return await new EfCommentEffectLedger(context).ReserveAsync(Key(accountId), OwnerB, Now, default);
            }));

        var winners = results.Where(c => c is not null).ToList();

        Assert.Single(winners);
    }

    [Fact]
    public async Task SameCommentOnDifferentAccountsIsIndependent()
    {
        var accountA = Guid.CreateVersion7();
        var accountB = Guid.CreateVersion7();
        await using var context = NewContext();
        var ledger = new EfCommentEffectLedger(context);

        var claimA = await ledger.ReserveAsync(Key(accountA), OwnerA, Now, default);
        var claimB = await ledger.ReserveAsync(Key(accountB), OwnerA, Now, default);

        Assert.NotNull(claimA);
        Assert.NotNull(claimB);
        Assert.NotEqual(claimA!.Id, claimB!.Id);
    }

    [Fact]
    public async Task PrivateReplyAndPublicReplyAreIndependent()
    {
        var accountId = Guid.CreateVersion7();
        await using var context = NewContext();
        var ledger = new EfCommentEffectLedger(context);

        var privateClaim = await ledger.ReserveAsync(Key(accountId, effect: InstagramEffectType.PrivateReply), OwnerA, Now, default);
        var publicClaim = await ledger.ReserveAsync(Key(accountId, effect: InstagramEffectType.PublicCommentReply), OwnerA, Now, default);

        Assert.NotNull(privateClaim);
        Assert.NotNull(publicClaim);
    }

    [Fact]
    public async Task ReservedClaimResumesOnlyForTheSameOwner()
    {
        var accountId = Guid.CreateVersion7();
        var claimId = Guid.Empty;
        await using (var setup = NewContext())
        {
            var claim = await new EfCommentEffectLedger(setup).ReserveAsync(Key(accountId), OwnerA, Now, default);
            claimId = claim!.Id;
        }

        // Same owner resumes the Reserved claim (crash before attempt).
        await using (var resume = NewContext())
        {
            var resumed = await new EfCommentEffectLedger(resume).ReserveAsync(Key(accountId), OwnerA, Now, default);
            Assert.NotNull(resumed);
            Assert.Equal(claimId, resumed!.Id);
            Assert.Equal(InstagramEffectStatus.Reserved, resumed.Status);
        }

        // A competitor can never steal a Reserved claim.
        await using (var competitor = NewContext())
        {
            var stolen = await new EfCommentEffectLedger(competitor).ReserveAsync(Key(accountId), OwnerB, Now, default);
            Assert.Null(stolen);
        }
    }

    [Fact]
    public async Task AttemptMarkerIsIrreversibleAcrossRestart()
    {
        var accountId = Guid.CreateVersion7();
        var claimId = Guid.Empty;
        await using (var setup = NewContext())
        {
            var ledger = new EfCommentEffectLedger(setup);
            var claim = await ledger.ReserveAsync(Key(accountId), OwnerA, Now, default);
            claimId = claim!.Id;
            await ledger.MarkAttemptingAsync(claimId, Now, default);
        }

        await using var restarted = NewContext();
        var observed = await new EfCommentEffectLedger(restarted).FindAsync(Key(accountId), default);

        Assert.NotNull(observed);
        Assert.Equal(InstagramEffectStatus.Attempting, observed!.Status);
        Assert.NotNull(observed.AttemptedAtUtc);
        Assert.Null(observed.CompletedAtUtc);

        // Re-reserve by the same owner surfaces Attempting — the coordinator replays it as
        // uncertain and must never issue a second provider call.
        var resumed = await new EfCommentEffectLedger(restarted).ReserveAsync(Key(accountId), OwnerA, Now, default);
        Assert.NotNull(resumed);
        Assert.Equal(InstagramEffectStatus.Attempting, resumed!.Status);
    }

    [Fact]
    public async Task SuccessReconciliationSurvivesRestart()
    {
        var accountId = Guid.CreateVersion7();
        var claimId = Guid.Empty;
        await using (var setup = NewContext())
        {
            var ledger = new EfCommentEffectLedger(setup);
            var claim = await ledger.ReserveAsync(Key(accountId), OwnerA, Now, default);
            claimId = claim!.Id;
            await ledger.MarkAttemptingAsync(claimId, Now, default);
            await ledger.RecordSuccessAsync(claimId, "526-recipient-1", "mid-provider-1", Now, default);
        }

        // Fresh process: the provider success is durable — AutomationRun convergence
        // replays it with ZERO second Meta call.
        await using var restarted = NewContext();
        var observed = await new EfCommentEffectLedger(restarted).FindAsync(Key(accountId), default);

        Assert.NotNull(observed);
        Assert.Equal(InstagramEffectStatus.Succeeded, observed!.Status);
        Assert.Equal("526-recipient-1", observed.ProviderRecipientId);
        Assert.Equal("mid-provider-1", observed.ProviderMessageId);
        Assert.NotNull(observed.AttemptedAtUtc);
        Assert.NotNull(observed.CompletedAtUtc);
    }

    [Fact]
    public async Task UncertainOutcomeSurvivesRestart()
    {
        var accountId = Guid.CreateVersion7();
        var claimId = Guid.Empty;
        await using (var setup = NewContext())
        {
            var ledger = new EfCommentEffectLedger(setup);
            var claim = await ledger.ReserveAsync(Key(accountId), OwnerA, Now, default);
            claimId = claim!.Id;
            await ledger.MarkAttemptingAsync(claimId, Now, default);
            await ledger.RecordUncertainAsync(claimId, Now, default);
        }

        await using var restarted = NewContext();
        var observed = await new EfCommentEffectLedger(restarted).FindAsync(Key(accountId), default);

        Assert.NotNull(observed);
        Assert.Equal(InstagramEffectStatus.Uncertain, observed!.Status);
        Assert.NotNull(observed.CompletedAtUtc);
        Assert.Null(observed.ProviderMessageId);
    }

    [Fact]
    public async Task TerminalFailureSurvivesRestart()
    {
        var accountId = Guid.CreateVersion7();
        var claimId = Guid.Empty;
        await using (var setup = NewContext())
        {
            var ledger = new EfCommentEffectLedger(setup);
            var claim = await ledger.ReserveAsync(Key(accountId), OwnerA, Now, default);
            claimId = claim!.Id;
            await ledger.MarkAttemptingAsync(claimId, Now, default);
            await ledger.RecordTerminalFailureAsync(claimId, "privateReply.terminalFailed.rejectedByMeta", Now, default);
        }

        await using var restarted = NewContext();
        var observed = await new EfCommentEffectLedger(restarted).FindAsync(Key(accountId), default);

        Assert.NotNull(observed);
        Assert.Equal(InstagramEffectStatus.TerminalFailed, observed!.Status);
        Assert.Equal("privateReply.terminalFailed.rejectedByMeta", observed.FailureCode);
    }

    [Fact]
    public async Task EffectRowHasNoTokenOrRawResponseStorage()
    {
        var entity = NewContext().Model.FindEntityType(typeof(CommentEffectRow));
        Assert.NotNull(entity);

        var properties = entity!.GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("ProviderRecipientId", properties);
        Assert.Contains("ProviderMessageId", properties);
        Assert.Contains("FailureCode", properties);

        // Hard boundary: no credential material and no raw provider payload storage.
        Assert.DoesNotContain(properties, p => p.Contains("Token", StringComparison.Ordinal));
        Assert.DoesNotContain(properties, p => p.Contains("Body", StringComparison.Ordinal));
        Assert.DoesNotContain(properties, p => p.Contains("Response", StringComparison.Ordinal));
        Assert.DoesNotContain(properties, p => p.Contains("Secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoserDoesNotPoisonTheWinnersClaim()
    {
        var accountId = Guid.CreateVersion7();
        var winnerId = Guid.Empty;
        await using (var winner = NewContext())
        {
            var claim = await new EfCommentEffectLedger(winner).ReserveAsync(Key(accountId), OwnerA, Now, default);
            winnerId = claim!.Id;
        }

        // The losing attempt must leave the winner's claim fully usable.
        await using (var loser = NewContext())
        {
            var stolen = await new EfCommentEffectLedger(loser).ReserveAsync(Key(accountId), OwnerB, Now, default);
            Assert.Null(stolen);
        }

        await using var verify = NewContext();
        var observed = await new EfCommentEffectLedger(verify).FindAsync(Key(accountId), default);
        Assert.NotNull(observed);
        Assert.Equal(winnerId, observed!.Id);
        Assert.Equal(OwnerA, observed.OwnerOperationId);
        Assert.Equal(InstagramEffectStatus.Reserved, observed.Status);
    }
}
