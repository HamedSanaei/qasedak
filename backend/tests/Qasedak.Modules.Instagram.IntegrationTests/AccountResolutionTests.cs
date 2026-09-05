using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Modules.Instagram.IntegrationTests;

/// <summary>
/// Deterministic inbound-routing resolution over real PostgreSQL:
/// ResolveActiveAccountAsync answers from active rows only, ignores disconnected
/// history regardless of physical row order, and reports duplicates as Ambiguous
/// instead of an order-dependent pick. No cross-schema access.
/// </summary>
[Collection(PostgresTestEnvironment.Name)]
public sealed class AccountResolutionTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    private InstagramDbContext NewContext() =>
        new(new DbContextOptionsBuilder<InstagramDbContext>()
            .UseNpgsql(fixture.Context.Database.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", InstagramDbContext.Schema))
            .Options);

    private static ConnectedAccount NewAccount(Guid workspaceId, string providerId)
    {
        var account = ConnectedAccount.Create(
            Guid.CreateVersion7(), workspaceId, providerId, ConnectionPath.InstagramLogin,
            ["instagram_business_basic"], Now.AddDays(30), Now);
        return account;
    }

    private async Task<Guid> SeedActiveAsync(Guid workspaceId, string providerId)
    {
        await using var context = NewContext();
        var account = NewAccount(workspaceId, providerId);
        await context.Accounts.AddAsync(account);
        await context.SaveChangesAsync();
        return account.Id;
    }

    private async Task<Guid> SeedDisconnectedAsync(Guid workspaceId, string providerId)
    {
        await using var context = NewContext();
        var account = NewAccount(workspaceId, providerId);
        await context.Accounts.AddAsync(account);
        await context.SaveChangesAsync();
        account.Disconnect(Now.AddMinutes(1));
        await context.SaveChangesAsync();
        return account.Id;
    }

    private async Task<AccountResolution> ResolveAsync(string providerId)
    {
        await using var context = NewContext();
        var repository = new EfConnectedAccountRepository(context);
        return await repository.ResolveActiveAccountAsync(providerId);
    }

    [Fact]
    public async Task ReconnectHistoryNeverShadowsTheActiveRow()
    {
        var workspaceId = Guid.CreateVersion7();
        var providerId = "ig-resolve-" + Guid.CreateVersion7().ToString("N");
        await SeedDisconnectedAsync(workspaceId, providerId);
        var activeId = await SeedActiveAsync(workspaceId, providerId);

        var resolution = await ResolveAsync(providerId);

        Assert.Equal(AccountResolutionStatus.Resolved, resolution.Status);
        Assert.Equal(activeId, resolution.Account!.Id);
        Assert.Equal(workspaceId, resolution.Account.WorkspaceId);
    }

    [Fact]
    public async Task ResolutionIsIndependentOfPhysicalRowOrder()
    {
        // Interleaved heap: the target identity's rows surround unrelated rows, and the
        // active row is not the most recently inserted one. Resolution must still
        // return exactly the active row — never a positional pick.
        var workspaceId = Guid.CreateVersion7();
        var providerId = "ig-order-" + Guid.CreateVersion7().ToString("N");
        await SeedDisconnectedAsync(workspaceId, providerId);
        await SeedActiveAsync(Guid.CreateVersion7(), "ig-noise-" + Guid.CreateVersion7().ToString("N"));
        var activeId = await SeedActiveAsync(workspaceId, providerId);
        await SeedDisconnectedAsync(Guid.CreateVersion7(), "ig-noise-" + Guid.CreateVersion7().ToString("N"));

        var resolution = await ResolveAsync(providerId);

        Assert.Equal(AccountResolutionStatus.Resolved, resolution.Status);
        Assert.Equal(activeId, resolution.Account!.Id);
        Assert.Equal(workspaceId, resolution.Account.WorkspaceId);
    }

    [Fact]
    public async Task DisconnectedOnlyIdentityIsNotFound()
    {
        var providerId = "ig-gone-" + Guid.CreateVersion7().ToString("N");
        await SeedDisconnectedAsync(Guid.CreateVersion7(), providerId);

        var resolution = await ResolveAsync(providerId);

        Assert.Equal(AccountResolutionStatus.NotFound, resolution.Status);
        Assert.Null(resolution.Account);
    }

    [Fact]
    public async Task UnknownIdentityIsNotFound()
    {
        var resolution = await ResolveAsync("ig-unknown-" + Guid.CreateVersion7().ToString("N"));

        Assert.Equal(AccountResolutionStatus.NotFound, resolution.Status);
        Assert.Null(resolution.Account);
    }

    [Fact]
    public async Task DuplicateActiveIdentityAcrossWorkspacesIsAmbiguous()
    {
        var providerId = "ig-dupe-" + Guid.CreateVersion7().ToString("N");
        await SeedActiveAsync(Guid.CreateVersion7(), providerId);
        await SeedActiveAsync(Guid.CreateVersion7(), providerId);

        var resolution = await ResolveAsync(providerId);

        Assert.Equal(AccountResolutionStatus.Ambiguous, resolution.Status);
        Assert.Null(resolution.Account);
    }

    [Fact]
    public async Task RoutingIndexExistsForActiveResolution()
    {
        await using var context = NewContext();
        var index = await context.Database.SqlQueryRaw<int>(
            "SELECT count(*) FROM pg_indexes WHERE schemaname = 'instagram' AND indexname = 'IX_connected_accounts_active_routing_identity'").ToListAsync();
        Assert.Equal([1], index);
    }
}
