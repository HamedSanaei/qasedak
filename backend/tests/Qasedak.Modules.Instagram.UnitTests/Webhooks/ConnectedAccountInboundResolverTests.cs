using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.Webhooks;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Qasedak.Modules.Instagram.Infrastructure.Webhooks;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests;

/// <summary>
/// The enrichment resolver must map the deterministic M13-002 verdict exactly: Resolved
/// carries the account's workspace + id; NotFound and Ambiguous carry nothing and never
/// guess. Null/blank provider identities resolve to NotFound without a repository call.
/// </summary>
public sealed class ConnectedAccountInboundResolverTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class FakeAccountRepository(AccountResolution resolution) : IConnectedAccountRepository
    {
        public string? LastProviderAccountId { get; private set; }

        public Task<AccountResolution> ResolveActiveAccountAsync(string providerAccountId, CancellationToken cancellationToken = default)
        {
            LastProviderAccountId = providerAccountId;
            return Task.FromResult(resolution);
        }

        public Task<ConnectedAccount?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<ConnectedAccount?>(null);

        public Task<ConnectedAccount?> FindByProviderIdentityAsync(Guid workspaceId, string providerUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ConnectedAccount?>(null);

        public Task<IReadOnlyList<ConnectedAccount>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConnectedAccount>>([]);

        public Task<IReadOnlyList<ConnectedAccount>> ListActiveAsync(int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConnectedAccount>>([]);

        public Task<bool> DisconnectAsync(Guid accountId, DateTimeOffset disconnectedAtUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task AddAsync(ConnectedAccount account, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private static ConnectedAccount Account() => ConnectedAccount.Create(
        AccountId,
        WorkspaceId,
        "17841400000000000",
        ConnectionPath.InstagramLogin,
        ["instagram_business_manage_messages"],
        DateTimeOffset.UtcNow.AddDays(30),
        DateTimeOffset.UtcNow);

    [Fact]
    public async Task ResolvedAccountCarriesWorkspaceAndAccountKeys()
    {
        var repository = new FakeAccountRepository(AccountResolution.Resolved(Account()));
        var resolver = new ConnectedAccountInboundResolver(repository);

        var resolution = await resolver.ResolveAsync("17841400000000000");

        Assert.Equal(InboundAccountStatus.Resolved, resolution.Status);
        Assert.Equal(WorkspaceId, resolution.WorkspaceId);
        Assert.Equal(AccountId, resolution.ConnectedAccountId);
        Assert.Equal("17841400000000000", repository.LastProviderAccountId);
    }

    [Fact]
    public async Task NotFoundAndAmbiguousCarryNoAccount()
    {
        var notFound = new ConnectedAccountInboundResolver(new FakeAccountRepository(AccountResolution.NotFound()));
        Assert.Equal(InboundAccountStatus.NotFound, (await notFound.ResolveAsync("unknown")).Status);
        Assert.Null((await notFound.ResolveAsync("unknown")).ConnectedAccountId);

        var ambiguous = new ConnectedAccountInboundResolver(new FakeAccountRepository(AccountResolution.Ambiguous()));
        var resolution = await ambiguous.ResolveAsync("17841400000000000");
        Assert.Equal(InboundAccountStatus.Ambiguous, resolution.Status);
        Assert.Null(resolution.WorkspaceId);
        Assert.Null(resolution.ConnectedAccountId);
    }

    [Fact]
    public async Task BlankIdentityResolvesNotFoundWithoutRepositoryCall()
    {
        var repository = new FakeAccountRepository(AccountResolution.Resolved(Account()));
        var resolver = new ConnectedAccountInboundResolver(repository);

        Assert.Equal(InboundAccountStatus.NotFound, (await resolver.ResolveAsync(null)).Status);
        Assert.Equal(InboundAccountStatus.NotFound, (await resolver.ResolveAsync("  ")).Status);
        Assert.Null(repository.LastProviderAccountId);
    }
}
