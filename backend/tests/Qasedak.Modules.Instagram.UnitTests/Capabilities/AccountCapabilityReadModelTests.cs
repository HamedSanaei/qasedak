using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.Capabilities;
using Qasedak.Modules.Instagram.Application.OAuth;
using Qasedak.Modules.Instagram.Domain.Accounts;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests.Capabilities;

public sealed class AccountCapabilityReadModelTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("0199a111-1111-7111-8111-111111111111");
    private static readonly Guid AccountId = Guid.Parse("0199a222-2222-7222-8222-222222222222");

    private static readonly string[] FullScopes =
    [
        InstagramAuthorizationScopes.Basic,
        InstagramAuthorizationScopes.ManageMessages,
        InstagramAuthorizationScopes.ManageComments,
        InstagramCapabilityPolicy.ManageInsightsScope,
    ];

    private static ConnectedAccount Account(
        IReadOnlyList<string>? scopes = null,
        AccountHealth health = AccountHealth.Connected,
        SubscriptionHealth subscription = SubscriptionHealth.Healthy,
        DateTimeOffset? disconnectedAtUtc = null,
        Guid? workspaceId = null) => ConnectedAccount.FromState(
        AccountId,
        workspaceId ?? WorkspaceId,
        "ig-capability-1",
        ConnectionPath.InstagramLogin,
        scopes ?? FullScopes,
        health,
        null,
        DateTimeOffset.UtcNow.AddDays(20),
        DateTimeOffset.UtcNow.AddDays(-10),
        disconnectedAtUtc,
        subscriptionHealth: subscription);

    private static InstagramCapabilityState State(
        AccountCapabilitiesRecord record,
        InstagramCapability capability) =>
        Assert.Single(record.Capabilities, item => item.Capability == capability).State;

    [Fact]
    public void FullLocalStateEnablesShippedCapabilitiesButFollowGateRemainsUnsupported()
    {
        var result = InstagramCapabilityPolicy.Evaluate(Account());

        foreach (var capability in Enum.GetValues<InstagramCapability>().Where(c => c != InstagramCapability.FollowGate))
        {
            Assert.Equal(InstagramCapabilityState.Available, State(result, capability));
        }

        Assert.Equal(InstagramCapabilityState.Unsupported, State(result, InstagramCapability.FollowGate));
    }

    [Fact]
    public void MissingPermissionsFailClosedPerCapability()
    {
        var result = InstagramCapabilityPolicy.Evaluate(Account(scopes: [InstagramAuthorizationScopes.Basic]));

        Assert.Equal(InstagramCapabilityState.Available, State(result, InstagramCapability.MediaCatalog));
        Assert.Equal(InstagramCapabilityState.PermissionRequired, State(result, InstagramCapability.Analytics));
        Assert.Equal(InstagramCapabilityState.PermissionRequired, State(result, InstagramCapability.Messaging));
        Assert.Equal(InstagramCapabilityState.PermissionRequired, State(result, InstagramCapability.CommentAutomation));
        Assert.Equal(InstagramCapabilityState.PermissionRequired, State(result, InstagramCapability.PrivateReply));
        Assert.Equal(InstagramCapabilityState.PermissionRequired, State(result, InstagramCapability.PublicReply));
        Assert.Equal(InstagramCapabilityState.PermissionRequired, State(result, InstagramCapability.RevealFlow));
        Assert.Equal(InstagramCapabilityState.PermissionRequired, State(result, InstagramCapability.ConversationHistorySync));
        Assert.Equal(InstagramCapabilityState.Unsupported, State(result, InstagramCapability.FollowGate));
    }

    [Fact]
    public void SubscriptionDegradationOnlyBlocksSubscriptionDependentCapabilities()
    {
        var result = InstagramCapabilityPolicy.Evaluate(Account(subscription: SubscriptionHealth.NeedsRepair));

        Assert.Equal(InstagramCapabilityState.TemporarilyUnavailable, State(result, InstagramCapability.Messaging));
        Assert.Equal(InstagramCapabilityState.TemporarilyUnavailable, State(result, InstagramCapability.CommentAutomation));
        Assert.Equal(InstagramCapabilityState.TemporarilyUnavailable, State(result, InstagramCapability.RevealFlow));
        Assert.Equal(InstagramCapabilityState.Available, State(result, InstagramCapability.PrivateReply));
        Assert.Equal(InstagramCapabilityState.Available, State(result, InstagramCapability.PublicReply));
        Assert.Equal(InstagramCapabilityState.Available, State(result, InstagramCapability.ConversationHistorySync));
    }

    [Theory]
    [InlineData(AccountHealth.Expired)]
    [InlineData(AccountHealth.Revoked)]
    [InlineData(AccountHealth.Unhealthy)]
    public void UnhealthyAccountFailsClosedForEverySupportedCapability(AccountHealth health)
    {
        var result = InstagramCapabilityPolicy.Evaluate(Account(health: health));

        foreach (var capability in Enum.GetValues<InstagramCapability>().Where(c => c != InstagramCapability.FollowGate))
        {
            Assert.Equal(InstagramCapabilityState.Unhealthy, State(result, capability));
        }
        Assert.Equal(InstagramCapabilityState.Unsupported, State(result, InstagramCapability.FollowGate));
    }

    [Fact]
    public void DisconnectedAccountFailsClosedForEverySupportedCapability()
    {
        var result = InstagramCapabilityPolicy.Evaluate(Account(disconnectedAtUtc: DateTimeOffset.UtcNow));

        foreach (var capability in Enum.GetValues<InstagramCapability>().Where(c => c != InstagramCapability.FollowGate))
        {
            Assert.Equal(InstagramCapabilityState.Disconnected, State(result, capability));
        }
        Assert.Equal(InstagramCapabilityState.Unsupported, State(result, InstagramCapability.FollowGate));
    }

    [Fact]
    public async Task UseCaseRequiresExactWorkspaceAndAccount()
    {
        var repository = new FakeRepository(Account());
        var useCase = new GetAccountCapabilitiesUseCase(repository);

        var exact = await useCase.ExecuteAsync(WorkspaceId, AccountId);
        var ok = Assert.IsType<AccountCapabilitiesResult.Ok>(exact);
        Assert.Equal(AccountId, ok.Value.AccountId);

        var foreign = await useCase.ExecuteAsync(Guid.CreateVersion7(), AccountId);
        Assert.IsType<AccountCapabilitiesResult.NotFound>(foreign);

        var unknown = await useCase.ExecuteAsync(WorkspaceId, Guid.CreateVersion7());
        Assert.IsType<AccountCapabilitiesResult.NotFound>(unknown);
    }

    private sealed class FakeRepository(ConnectedAccount row) : IConnectedAccountRepository
    {
        public Task<ConnectedAccount?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(id == row.Id ? row : null);

        public Task<ConnectedAccount?> FindByProviderIdentityAsync(
            Guid workspaceId, string providerUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ConnectedAccount?>(null);

        public Task<AccountResolution> ResolveActiveAccountAsync(
            string providerAccountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(AccountResolution.NotFound());

        public Task<IReadOnlyList<ConnectedAccount>> ListByWorkspaceAsync(
            Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConnectedAccount>>([]);

        public Task<IReadOnlyList<ConnectedAccount>> ListActiveAsync(
            int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConnectedAccount>>([]);

        public Task<bool> DisconnectAsync(
            Guid accountId, DateTimeOffset disconnectedAtUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task AddAsync(ConnectedAccount account, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
