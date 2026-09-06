using Qasedak.BuildingBlocks.Application;
using Qasedak.Modules.Instagram.Application.Subscriptions;
using Qasedak.Modules.Instagram.Domain.Accounts;

namespace Qasedak.Modules.Instagram.Application.Accounts;

/// <summary>Outcome of an explicit subscription repair.</summary>
public readonly record struct RepairSubscriptionResult(bool Success, SubscriptionHealth Health, string? FailureCode)
{
    public static RepairSubscriptionResult Repaired(SubscriptionHealth health) => new(true, health, null);

    public static RepairSubscriptionResult Refused(string failureCode) => new(false, SubscriptionHealth.Unknown, failureCode);
}

/// <summary>
/// Repairs webhook subscriptions for exactly one connected account: resolves the
/// account, verifies workspace ownership and connected state, uses only that
/// account's protected token, subscribes the centrally owned desired field set and
/// persists truthful health. Never iterates sibling accounts; never touches OAuth.
/// </summary>
public sealed class RepairSubscriptionUseCase(
    IConnectedAccountRepository accounts,
    IProtectedTokenStore tokens,
    ISubscriptionClient subscriptions,
    IClock clock)
{
    public async Task<RepairSubscriptionResult> ExecuteAsync(Guid workspaceId, Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await accounts.FindByIdAsync(accountId, cancellationToken);
        if (account is null || account.WorkspaceId != workspaceId)
        {
            return RepairSubscriptionResult.Refused(AccountFailures.NotFound);
        }

        if (account.IsDisconnected)
        {
            return RepairSubscriptionResult.Refused(AccountFailures.AlreadyDisconnected);
        }

        var accessToken = await tokens.GetAsync(account.Id, cancellationToken);
        if (string.IsNullOrEmpty(accessToken))
        {
            account.ApplySubscription(SubscriptionHealth.NeedsRepair, SubscriptionFailures.Unavailable, clock.UtcNow);
            await accounts.SaveChangesAsync(cancellationToken);
            return RepairSubscriptionResult.Refused(AccountFailures.TokenMissing);
        }

        var result = await subscriptions.SubscribeAsync(
            accessToken, account.ProviderUserId, InstagramSubscriptionFields.Required, cancellationToken);
        account.ApplySubscription(
            result.Success ? SubscriptionHealth.Healthy : SubscriptionHealth.NeedsRepair,
            result.Success ? null : result.FailureCode,
            clock.UtcNow);
        await accounts.SaveChangesAsync(cancellationToken);

        if (result.Success)
        {
            return RepairSubscriptionResult.Repaired(SubscriptionHealth.Healthy);
        }

        return RepairSubscriptionResult.Refused(result.FailureCode ?? SubscriptionFailures.Unavailable);
    }
}
