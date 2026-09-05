using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Qasedak.BuildingBlocks.Application;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.OAuth;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Qasedak.Modules.Instagram.Infrastructure.Protection;
using Xunit;

namespace Qasedak.Modules.Instagram.IntegrationTests;

[Collection(PostgresTestEnvironment.Name)]
public sealed class InstagramPersistenceTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    private static readonly byte[] ProtectionKey = Convert.FromBase64String(
        "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=");

    /// <summary>Deterministic OAuth stub: the HTTP adapter itself is contract-tested separately.</summary>
    private sealed class StubOAuthClient : IMetaOAuthClient
    {
        /// <summary>
        /// Canonical routing identity this stub issues. Unique per instance by default so
        /// tests sharing the collection database never collide on the global single-owner
        /// rule; tests needing a fixed identity set it explicitly.
        /// </summary>
        public string UserId { get; set; } = "ig-test-" + Guid.NewGuid().ToString("N");

        public Task<CodeExchangeResult> ExchangeCodeAsync(CodeExchangeRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(CodeExchangeResult.Ok(new(
                "SHORT-TOKEN", UserId, ["instagram_business_basic", "instagram_business_manage_comments"])));

        public Task<LongLivedTokenResult> ExchangeShortLivedForLongLivedAsync(
            string shortLivedAccessToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(LongLivedTokenResult.Ok(new("LONG-LIVED-RAW", 60 * 24 * 3600L)));

        public Task<LongLivedTokenResult> RefreshLongLivedAsync(
            string longLivedAccessToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(LongLivedTokenResult.Ok(new("ROTATED-RAW", 60 * 24 * 3600L)));
    }

    private InstagramDbContext NewContext() =>
        new(new DbContextOptionsBuilder<InstagramDbContext>()
            .UseNpgsql(fixture.Context.Database.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", InstagramDbContext.Schema))
            .Options);

    private (ConnectInstagramAccountUseCase Connect, DisconnectInstagramAccountUseCase Disconnect,
        ListWorkspaceConnectionsUseCase List, IProtectedTokenStore Store) NewStack(StubOAuthClient? oauth = null)
    {
        var context = NewContext();
        var repo = new EfConnectedAccountRepository(context);
        ITokenProtector protector = new AesGcmTokenProtector(
            Options.Create(new TokenProtectionOptions { KeyBase64 = Convert.ToBase64String(ProtectionKey) }));
        var store = new ProtectedTokenStore(context, protector);
        return (
            new ConnectInstagramAccountUseCase(repo, store, oauth ?? new StubOAuthClient(), new FixedClock(Now)),
            new DisconnectInstagramAccountUseCase(repo, store, new FixedClock(Now)),
            new ListWorkspaceConnectionsUseCase(repo),
            store);
    }

    [Fact]
    public async Task ConnectPersistsAccountAndEncryptedTokenEndToEnd()
    {
        var oauth = new StubOAuthClient { UserId = "ig-777-persist" };
        var (connect, _, list, store) = NewStack(oauth);
        var workspaceId = Guid.CreateVersion7();

        var result = await connect.ExecuteAsync(new(workspaceId, "auth-code", "https://cb.example/"));

        Assert.True(result.Success, result.FailureCode);

        // Fresh context: state must come from the database.
        var connections = await list.ExecuteAsync(workspaceId);
        var record = Assert.Single(connections);
        Assert.Equal("ig-777-persist", record.ProviderIdentity);
        Assert.Equal(AccountHealth.Connected.ToString(), record.Health);

        // Raw token is retrievable only through the protected store and decrypts exactly.
        Assert.Equal("LONG-LIVED-RAW", await store.GetAsync(record.AccountId));

        // The stored row must NOT contain the plaintext.
        await using var verification = NewContext();
        var ciphertextRow = await verification.AccountTokens.SingleAsync(t => t.AccountId == record.AccountId);
        Assert.DoesNotContain("LONG-LIVED-RAW", ciphertextRow.Ciphertext);
        Assert.True(ciphertextRow.Ciphertext.Length > 40);
    }

    [Fact]
    public async Task ReconnectAfterDisconnectIsAllowedBySchema()
    {
        var (connect, disconnect, _, _) = NewStack();
        var workspaceId = Guid.CreateVersion7();

        var first = await connect.ExecuteAsync(new(workspaceId, "code", "https://cb.example/"));
        Assert.True(first.Success, first.FailureCode);
        var disconnectResult = await disconnect.ExecuteAsync(first.AccountId);
        Assert.True(disconnectResult.Success, disconnectResult.FailureCode);

        var second = await connect.ExecuteAsync(new(workspaceId, "code", "https://cb.example/"));

        Assert.True(second.Success, second.FailureCode);
        Assert.NotEqual(first.AccountId, second.AccountId);
    }

    [Fact]
    public async Task ActiveDuplicateConnectionViolatesPartialUniqueIndex()
    {
        var workspaceId = Guid.CreateVersion7();
        await using var context = NewContext();

        await context.Accounts.AddAsync(ConnectedAccount.Create(
            Guid.CreateVersion7(), workspaceId, "ig-dup", ConnectionPath.InstagramLogin,
            ["instagram_business_basic"], Now.AddHours(24), Now));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await context.Accounts.AddAsync(ConnectedAccount.Create(
            Guid.CreateVersion7(), workspaceId, "ig-dup", ConnectionPath.InstagramLogin,
            ["instagram_business_basic"], Now.AddHours(48), Now));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task RotationReplacesCiphertextAtomicallyAndUpdatesExpiry()
    {
        var (connect, _, list, store) = NewStack();
        var workspaceId = Guid.CreateVersion7();
        var connectResult = await connect.ExecuteAsync(new(workspaceId, "code", "https://cb.example/"));
        Assert.True(connectResult.Success, connectResult.FailureCode);

        await using var context = NewContext();
        var protector = new AesGcmTokenProtector(Options.Create(
            new TokenProtectionOptions { KeyBase64 = Convert.ToBase64String(ProtectionKey) }));
        var repo = new EfConnectedAccountRepository(context);
        var sameContextStore = new ProtectedTokenStore(context, protector);
        var account = (await repo.FindByIdAsync(connectResult.AccountId))!;
        account.ApplyTokenRotation(Now.AddDays(60).AddMinutes(5), Now);
        await sameContextStore.StoreAsync(account.Id, "ROTATED-RAW");
        await repo.SaveChangesAsync();

        context.ChangeTracker.Clear();
        var reloaded = (await repo.FindByIdAsync(account.Id))!;
        Assert.Equal(Now.AddDays(60).AddMinutes(5), reloaded.TokenExpiresAtUtc);
        Assert.Equal("ROTATED-RAW", await store.GetAsync(account.Id));
    }

    [Fact]
    public async Task DisconnectRemovesTheTokenRow()
    {
        var (connect, disconnect, _, _) = NewStack();
        var result = await connect.ExecuteAsync(new(Guid.CreateVersion7(), "code", "https://cb.example/"));
        Assert.True(result.Success, result.FailureCode);

        await using var context = NewContext();
        var beforeCount = await context.AccountTokens.CountAsync(t => t.AccountId == result.AccountId);
        Assert.Equal(1, beforeCount);

        var disconnectUseCase = new DisconnectInstagramAccountUseCase(
            new EfConnectedAccountRepository(context),
            new ProtectedTokenStore(context, new AesGcmTokenProtector(Options.Create(
                new TokenProtectionOptions { KeyBase64 = Convert.ToBase64String(ProtectionKey) }))),
            new FixedClock(Now));
        var outcome = await disconnectUseCase.ExecuteAsync(result.AccountId);

        Assert.True(outcome.Success, outcome.FailureCode);
        Assert.Equal(0, await context.AccountTokens.CountAsync(t => t.AccountId == result.AccountId));
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
