using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Modules.Instagram.IntegrationTests;

/// <summary>
/// OAuth-state durability over real PostgreSQL: issue, valid consume, replay/tamper/
/// expiry/workspace/redirect rejection and exactly-one concurrent winner.
/// </summary>
[Collection(PostgresTestEnvironment.Name)]
public sealed class OAuthStateStoreTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 6, 0, 0, TimeSpan.Zero);

    private EfOAuthStateStore NewStore()
    {
        var options = new DbContextOptionsBuilder<InstagramDbContext>()
            .UseNpgsql(fixture.Context.Database.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", InstagramDbContext.Schema))
            .Options;
        return new EfOAuthStateStore(new InstagramDbContext(options));
    }

    [Fact]
    public async Task IssueThenConsumeSucceedsOnce()
    {
        var store = NewStore();
        var workspaceId = Guid.CreateVersion7();

        var issuance = await store.IssueAsync(workspaceId, "https://cb.example/", Now);
        Assert.False(string.IsNullOrWhiteSpace(issuance.State));
        Assert.Equal(Now.Add(OAuthStatePolicy.Lifetime), issuance.ExpiresAtUtc);

        Assert.Equal(
            OAuthStateConsumption.Consumed,
            await store.ConsumeAsync(issuance.State, workspaceId, "https://cb.example/", Now));
    }

    [Fact]
    public async Task ReplayIsRejectedAfterFirstConsume()
    {
        var store = NewStore();
        var workspaceId = Guid.CreateVersion7();
        var issuance = await store.IssueAsync(workspaceId, "https://cb.example/", Now);

        Assert.Equal(OAuthStateConsumption.Consumed,
            await store.ConsumeAsync(issuance.State, workspaceId, "https://cb.example/", Now));
        Assert.Equal(OAuthStateConsumption.AlreadyConsumed,
            await store.ConsumeAsync(issuance.State, workspaceId, "https://cb.example/", Now));
    }

    [Fact]
    public async Task ConcurrentConsumeHasExactlyOneWinner()
    {
        var workspaceId = Guid.CreateVersion7();
        var issuance = await NewStore().IssueAsync(workspaceId, "https://cb.example/", Now);
        var barrier = new TaskCompletionSource();

        var racers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            await barrier.Task;
            return await NewStore().ConsumeAsync(issuance.State, workspaceId, "https://cb.example/", Now);
        })).ToArray();
        barrier.SetResult();
        var outcomes = await Task.WhenAll(racers);

        Assert.Single(outcomes, o => o == OAuthStateConsumption.Consumed);
        Assert.All(outcomes.Where(o => o != OAuthStateConsumption.Consumed),
            o => Assert.Equal(OAuthStateConsumption.AlreadyConsumed, o));
    }

    [Fact]
    public async Task UnknownExpiredForeignAndRedirectMismatchedStatesFail()
    {
        var store = NewStore();
        var workspaceId = Guid.CreateVersion7();

        Assert.Equal(OAuthStateConsumption.NotFound,
            await store.ConsumeAsync("no-such-state", workspaceId, "https://cb.example/", Now));

        var issuance = await store.IssueAsync(workspaceId, "https://cb.example/", Now);
        Assert.Equal(OAuthStateConsumption.WorkspaceMismatch,
            await store.ConsumeAsync(issuance.State, Guid.CreateVersion7(), "https://cb.example/", Now));
        Assert.Equal(OAuthStateConsumption.RedirectMismatch,
            await store.ConsumeAsync(issuance.State, workspaceId, "https://evil.example/", Now));
        Assert.Equal(OAuthStateConsumption.Expired,
            await store.ConsumeAsync(issuance.State, workspaceId, "https://cb.example/",
                issuance.ExpiresAtUtc.AddMinutes(1)));
    }

    [Fact]
    public async Task OnlyHashesArePersisted()
    {
        var store = NewStore();
        var issuance = await store.IssueAsync(Guid.CreateVersion7(), "https://cb.example/", Now);

        await using var context = new InstagramDbContext(
            new DbContextOptionsBuilder<InstagramDbContext>()
                .UseNpgsql(fixture.Context.Database.GetConnectionString(), npgsql =>
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", InstagramDbContext.Schema))
                .Options);
        var rows = await context.OAuthStates.ToListAsync();
        Assert.DoesNotContain(rows, r => r.StateHash == issuance.State);
        Assert.All(rows, r => Assert.Equal(64, r.StateHash.Length));
    }
}
