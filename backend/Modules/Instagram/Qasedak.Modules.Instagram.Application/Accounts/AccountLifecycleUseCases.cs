using Qasedak.BuildingBlocks.Application;
using Qasedak.Modules.Instagram.Application.OAuth;
using Qasedak.Modules.Instagram.Domain.Accounts;

namespace Qasedak.Modules.Instagram.Application.Accounts;

/// <summary>Command: complete a Business Login connection for a workspace.</summary>
public sealed record ConnectInstagramAccountCommand(Guid WorkspaceId, string AuthorizationCode, string RedirectUri);

/// <summary>Outcome of connecting an account.</summary>
public readonly record struct ConnectAccountResult(bool Success, Guid AccountId, string? FailureCode)
{
    public static ConnectAccountResult Ok(Guid accountId) => new(true, accountId, null);

    public static ConnectAccountResult Fail(string failureCode) => new(false, Guid.Empty, failureCode);
}

/// <summary>
/// Connects an Instagram professional account to a workspace through the fast
/// Business Login flow (ADR-006 path 1): exchanges the authorization code for a short-lived
/// token, immediately upgrades it to a long-lived token (server-side), stores the raw token
/// only in the protected store, and records the connection aggregate with its scope snapshot.
/// </summary>
public sealed class ConnectInstagramAccountUseCase(
    IConnectedAccountRepository accounts,
    IProtectedTokenStore tokens,
    IMetaOAuthClient oauth,
    IClock clock)
{
    public async Task<ConnectAccountResult> ExecuteAsync(ConnectInstagramAccountCommand command, CancellationToken cancellationToken = default)
    {
        if (command.WorkspaceId == Guid.Empty)
        {
            return ConnectAccountResult.Fail(AccountFailures.NotFound);
        }

        var exchange = await oauth.ExchangeCodeAsync(new(command.AuthorizationCode, command.RedirectUri), cancellationToken);
        if (exchange.Failure is not null)
        {
            return ConnectAccountResult.Fail(Map(exchange.Failure.Reason));
        }

        var shortLived = exchange.Success!;
        var longLived = await oauth.ExchangeShortLivedForLongLivedAsync(shortLived.AccessToken, cancellationToken);
        if (longLived.Failure is not null)
        {
            return ConnectAccountResult.Fail(Map(longLived.Failure.Reason));
        }

        var existing = await accounts.FindByProviderIdentityAsync(command.WorkspaceId, shortLived.InstagramUserId, cancellationToken);
        if (existing is not null && !existing.IsDisconnected)
        {
            return ConnectAccountResult.Fail(AccountFailures.AlreadyConnected);
        }

        // Global single-owner enforcement: one professional account has one inbox, so
        // at most one workspace may actively own its routing identity. Disconnected
        // history never blocks a (re)connection; duplicate active owners fail closed
        // here so webhooks can never face an ambiguous choice later.
        var active = await accounts.ResolveActiveAccountAsync(shortLived.InstagramUserId, cancellationToken);
        if (active.Status == AccountResolutionStatus.Resolved && active.Account is not null
            && active.Account.WorkspaceId != command.WorkspaceId)
        {
            return ConnectAccountResult.Fail(AccountFailures.AlreadyConnectedElsewhere);
        }

        if (active.Status == AccountResolutionStatus.Ambiguous)
        {
            return ConnectAccountResult.Fail(AccountFailures.AlreadyConnectedElsewhere);
        }

        var token = longLived.Success!;
        var expiresAtUtc = clock.UtcNow.AddSeconds(token.ExpiresInSeconds);
        var account = ConnectedAccount.Create(
            Guid.CreateVersion7(),
            command.WorkspaceId,
            shortLived.InstagramUserId,
            ConnectionPath.InstagramLogin,
            shortLived.GrantedPermissions,
            expiresAtUtc,
            clock.UtcNow);

        await accounts.AddAsync(account, cancellationToken);
        await tokens.StoreAsync(account.Id, token.AccessToken, cancellationToken);
        await accounts.SaveChangesAsync(cancellationToken);

        return ConnectAccountResult.Ok(account.Id);
    }

    private static string Map(MetaOAuthFailureReason reason) => reason switch
    {
        MetaOAuthFailureReason.RejectedByMeta => AccountFailures.OAuthRejected,
        _ => AccountFailures.OAuthUnavailable,
    };
}

/// <summary>Outcome of disconnecting an account.</summary>
public readonly record struct DisconnectAccountResult(bool Success, string? FailureCode)
{
    public static DisconnectAccountResult Ok() => new(true, null);

    public static DisconnectAccountResult Fail(string failureCode) => new(false, failureCode);
}

/// <summary>
/// Disconnects a connected account: terminal operator action that deletes all protected
/// token material and records the disconnection on the aggregate.
/// </summary>
public sealed class DisconnectInstagramAccountUseCase(
    IConnectedAccountRepository accounts,
    IProtectedTokenStore tokens,
    IClock clock)
{
    public async Task<DisconnectAccountResult> ExecuteAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await accounts.FindByIdAsync(accountId, cancellationToken);
        if (account is null)
        {
            return DisconnectAccountResult.Fail(AccountFailures.NotFound);
        }

        if (account.IsDisconnected)
        {
            return DisconnectAccountResult.Fail(AccountFailures.AlreadyDisconnected);
        }

        account.Disconnect(clock.UtcNow);
        await tokens.DeleteAsync(account.Id, cancellationToken);
        await accounts.SaveChangesAsync(cancellationToken);

        return DisconnectAccountResult.Ok();
    }
}

/// <summary>Lists the connection-state surface for a workspace. Token values are never included.</summary>
public sealed class ListWorkspaceConnectionsUseCase(IConnectedAccountRepository accounts)
{
    public async Task<IReadOnlyList<ConnectionStateRecord>> ExecuteAsync(
        Guid workspaceId, bool includeDisconnected = false, CancellationToken cancellationToken = default)
    {
        var all = await accounts.ListByWorkspaceAsync(workspaceId, cancellationToken);
        return all
            .Where(a => includeDisconnected || !a.IsDisconnected)
            .Select(a => new ConnectionStateRecord(
                a.Id,
                a.WorkspaceId,
                a.ProviderUserId,
                a.Path.ToString(),
                a.Scopes.ToArray(),
                a.Health.ToString(),
                a.HealthDetail,
                a.TokenExpiresAtUtc,
                a.ConnectedAtUtc,
                a.DisconnectedAtUtc))
            .ToArray();
    }
}
