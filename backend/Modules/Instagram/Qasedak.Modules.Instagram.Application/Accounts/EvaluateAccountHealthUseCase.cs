using Qasedak.BuildingBlocks.Application;
using Qasedak.Modules.Instagram.Application.OAuth;
using Qasedak.Modules.Instagram.Domain.Accounts;

namespace Qasedak.Modules.Instagram.Application.Accounts;

/// <summary>The surfaced health state after one evaluation round.</summary>
public readonly record struct AccountHealthEvaluation(
    Guid AccountId,
    bool Success,
    string Health,
    string? HealthDetail,
    DateTimeOffset? ExpiresAtUtc,
    string? FailureCode)
{
    public static AccountHealthEvaluation Ok(ConnectedAccount account) => new(
        account.Id, true, account.Health.ToString(), account.HealthDetail, account.TokenExpiresAtUtc, null);

    public static AccountHealthEvaluation Fail(Guid accountId, string failureCode) => new(
        accountId, false, string.Empty, null, null, failureCode);
}

/// <summary>
/// Evaluates one connected account's token health and persists the resulting aggregate
/// state. Local rules run first (expiry in the past is decisive without a network call);
/// otherwise the token is inspected live against Meta. Transient inspection failures leave
/// health untouched — degraded state must be caused by Meta, never by network noise
/// (lifecycle contract §5: unhealthy states are never silently retried into health).
/// </summary>
public sealed class EvaluateAccountHealthUseCase(
    IConnectedAccountRepository accounts,
    IProtectedTokenStore tokens,
    IMetaTokenInspector inspector,
    IClock clock)
{
    public async Task<AccountHealthEvaluation> ExecuteAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await accounts.FindByIdAsync(accountId, cancellationToken);
        if (account is null || account.IsDisconnected)
        {
            return AccountHealthEvaluation.Fail(accountId, AccountFailures.NotFound);
        }

        // Decisive local rule: an Instagram-Login token past its expiry is expired.
        if (account.Path == ConnectionPath.InstagramLogin
            && account.TokenExpiresAtUtc is { } expiry
            && expiry <= clock.UtcNow)
        {
            account.MarkExpired();
            await accounts.SaveChangesAsync(cancellationToken);
            return AccountHealthEvaluation.Ok(account);
        }

        var accessToken = await tokens.GetAsync(accountId, cancellationToken);
        if (accessToken is null)
        {
            // Token material missing while the account claims connection: actionable fault.
            account.MarkUnhealthy("Token material is missing; reconnect required.");
            await accounts.SaveChangesAsync(cancellationToken);
            return AccountHealthEvaluation.Ok(account);
        }

        var inspection = await inspector.InspectAsync(accessToken, cancellationToken);
        switch (inspection.Kind)
        {
            case TokenInspectionKind.Healthy:
                // Surface the expiring-soon window so refresh can be scheduled.
                if (account.Path == ConnectionPath.InstagramLogin
                    && account.TokenExpiresAtUtc - clock.UtcNow is { } remaining
                    && remaining <= TimeSpan.FromDays(7))
                {
                    account.MarkExpiringSoon();
                }
                else
                {
                    account.ApplyTokenRotation(
                        account.TokenExpiresAtUtc ?? clock.UtcNow.AddDays(60), clock.UtcNow);
                }

                break;

            case TokenInspectionKind.Expired:
                account.MarkExpired();
                break;

            case TokenInspectionKind.Revoked:
                account.MarkRevoked(inspection.Detail ?? "Meta reported the session as invalidated.");
                break;

            case TokenInspectionKind.PermissionLoss:
                account.MarkUnhealthy(inspection.Detail ?? "A granted permission is no longer valid.");
                break;

            case TokenInspectionKind.Transient:
                // Leave persisted health untouched; caller may retry later.
                return AccountHealthEvaluation.Ok(account);

            default:
                return AccountHealthEvaluation.Fail(accountId, AccountFailures.NotFound);
        }

        await accounts.SaveChangesAsync(cancellationToken);
        return AccountHealthEvaluation.Ok(account);
    }
}
