using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Application.Scheduling;
using Qasedak.Modules.Instagram.Application.FollowerSnapshots;
using Qasedak.Modules.Instagram.Application.OAuth;
using Qasedak.Modules.Instagram.Application.Subscriptions;
using Qasedak.Modules.Instagram.Domain.Accounts;

namespace Qasedak.Modules.Instagram.Application.Accounts;

/// <summary>Command: complete a Business Login connection for a workspace.</summary>
public sealed record ConnectInstagramAccountCommand(Guid WorkspaceId, string AuthorizationCode, string RedirectUri, string? OAuthState);

/// <summary>Outcome of connecting an account.</summary>
public readonly record struct ConnectAccountResult(bool Success, Guid AccountId, string? FailureCode, SubscriptionHealth? SubscriptionHealth)
{
    public static ConnectAccountResult Ok(Guid accountId, SubscriptionHealth subscriptionHealth) =>
        new(true, accountId, null, subscriptionHealth);

    public static ConnectAccountResult Fail(string failureCode) => new(false, Guid.Empty, failureCode, null);
}

/// <summary>
/// Connects an Instagram professional account to a workspace through Business Login:
/// validates single-use server-issued OAuth state first, exchanges the code
/// server-side, proves the professional identity via the profile endpoint BEFORE
/// any write (identity failures persist nothing), then creates the aggregate,
/// stores the token only in the protected store, subscribes webhook fields
/// (recording truthful health, never blocking on it) and enqueues the first
/// token-refresh occurrence. State is consumed before any Meta call, so tampered,
/// replayed, foreign-workspace or redirect-mismatched callbacks never reach Meta.
/// </summary>
public sealed class ConnectInstagramAccountUseCase(
    IConnectedAccountRepository accounts,
    IProtectedTokenStore tokens,
    IMetaOAuthClient oauth,
    IAccountProfileClient profiles,
    ISubscriptionClient subscriptions,
    IScheduledWorkStore scheduledWork,
    IOAuthStateStore oauthStates,
    IClock clock)
{
    public async Task<ConnectAccountResult> ExecuteAsync(ConnectInstagramAccountCommand command, CancellationToken cancellationToken = default)
    {
        if (command.WorkspaceId == Guid.Empty)
        {
            return ConnectAccountResult.Fail(AccountFailures.NotFound);
        }

        if (string.IsNullOrWhiteSpace(command.OAuthState))
        {
            return ConnectAccountResult.Fail(OAuthStateFailures.InvalidState);
        }

        var consumption = await oauthStates.ConsumeAsync(
            command.OAuthState!, command.WorkspaceId, command.RedirectUri, clock.UtcNow, cancellationToken);
        if (consumption != OAuthStateConsumption.Consumed)
        {
            return ConnectAccountResult.Fail(consumption switch
            {
                OAuthStateConsumption.Expired => OAuthStateFailures.ExpiredState,
                OAuthStateConsumption.AlreadyConsumed => OAuthStateFailures.ReplayedState,
                OAuthStateConsumption.WorkspaceMismatch => OAuthStateFailures.WorkspaceMismatch,
                OAuthStateConsumption.RedirectMismatch => OAuthStateFailures.RedirectMismatch,
                _ => OAuthStateFailures.InvalidState,
            });
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

        // Global single-owner enforcement (M13-002 invariant, preserved here).
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

        // Identity proof before any write: the profile endpoint must return this
        // exact professional id for the exchanged token. A hard identity failure
        // aborts the connection with nothing persisted; the one-shot state is
        // already consumed, so the user restarts the authorization flow
        // (documented, never weakened by rebinding).
        var profile = await profiles.GetProfileAsync(token.AccessToken, shortLived.InstagramUserId, cancellationToken);
        if (profile is not AccountProfileOutcome.Ok proven)
        {
            return ConnectAccountResult.Fail(profile is AccountProfileOutcome.IdentityMismatch
                ? ProfileFailures.IdentityMismatch
                : ProfileFailures.Unavailable);
        }
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

        account.ApplyProfile(
            proven.Profile.Username,
            proven.Profile.DisplayName,
            proven.Profile.ProfilePictureUrl,
            proven.Profile.AccountType,
            clock.UtcNow);

        // Subscriptions never block a proven connection: any failure is recorded
        // truthfully as repairable state and the repair endpoint recovers it.
        // The edge is addressed by the explicit professional account id.
        var subscription = await subscriptions.SubscribeAsync(
            token.AccessToken, account.ProviderUserId, InstagramSubscriptionFields.Required, cancellationToken);
        account.ApplySubscription(
            subscription.Success ? SubscriptionHealth.Healthy : SubscriptionHealth.NeedsRepair,
            subscription.Success ? null : subscription.FailureCode,
            clock.UtcNow);
        await accounts.SaveChangesAsync(cancellationToken);

        // First refresh occurrence, keyed to this token generation so retries dedupe
        // while the next rotation schedules a distinct occurrence. Due respects both
        // the pre-expiry window and the provider minimum-age rule, so a freshly
        // issued token is never refreshed before Meta accepts it.
        var firstDueAtUtc = new[]
        {
            TokenRefreshPolicy.NextDueAt(expiresAtUtc, clock.UtcNow),
            clock.UtcNow.Add(TokenRefreshPolicy.MinimumTokenAge),
        }.Max();
        await scheduledWork.EnqueueAsync(
            new ScheduledWorkEnqueue(
                TokenRefreshPolicy.JobType,
                TokenRefreshPolicy.IdempotencyKey(account.Id, expiresAtUtc),
                TokenRefreshPolicy.RefreshPayload(account.Id),
                PayloadVersion: 1,
                ConnectedAccountId: account.Id,
                WorkspaceId: command.WorkspaceId,
                DueAtUtc: firstDueAtUtc,
                MaxAttempts: TokenRefreshPolicy.DefaultMaxAttempts),
            clock.UtcNow,
            cancellationToken);

        // First daily follower snapshot occurrence (M13-007): enqueued at connect so
        // newly connected accounts are scheduled immediately; occurrence-specific
        // account/day idempotency key; identifiers-only payload. The short delay gives
        // the connection a moment to settle before the first observation.
        var snapshotDayUtc = FollowerSnapshotPolicy.UtcDay(clock.UtcNow);
        await scheduledWork.EnqueueAsync(
            new ScheduledWorkEnqueue(
                FollowerSnapshotPolicy.JobType,
                FollowerSnapshotPolicy.IdempotencyKey(account.Id, snapshotDayUtc),
                FollowerSnapshotPolicy.Payload(account.Id, snapshotDayUtc),
                PayloadVersion: 1,
                ConnectedAccountId: account.Id,
                WorkspaceId: command.WorkspaceId,
                DueAtUtc: clock.UtcNow.AddMinutes(5),
                MaxAttempts: FollowerSnapshotPolicy.DefaultMaxAttempts),
            clock.UtcNow,
            cancellationToken);

        return ConnectAccountResult.Ok(account.Id, account.SubscriptionHealth);
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
/// token material and records the disconnection on the aggregate. Workspace ownership
/// is verified first (foreign accounts read as not found); a concurrent token rotation
/// is survived with one reload-and-retry so disconnect never resurrects a rotated token.
/// No provider unsubscribe call is made on the Instagram Login path: no official
/// unsubscribe endpoint exists there, and routing already fails closed for
/// disconnected accounts.
/// </summary>
public sealed class DisconnectInstagramAccountUseCase(
    IConnectedAccountRepository accounts,
    IProtectedTokenStore tokens,
    IClock clock)
{
    public async Task<DisconnectAccountResult> ExecuteAsync(Guid workspaceId, Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await accounts.FindByIdAsync(accountId, cancellationToken);
        if (account is null || account.WorkspaceId != workspaceId)
        {
            return DisconnectAccountResult.Fail(AccountFailures.NotFound);
        }

        if (account.IsDisconnected)
        {
            return DisconnectAccountResult.Fail(AccountFailures.AlreadyDisconnected);
        }

        // Delete–save–delete: a concurrent rotation landing between the save and the
        // trailing delete would otherwise orphan fresh ciphertext on a disconnected
        // account. Both deletes are idempotent; the repository retries once on
        // optimistic-concurrency loss.
        await tokens.DeleteAsync(account.Id, cancellationToken);
        var disconnected = await accounts.DisconnectAsync(account.Id, clock.UtcNow, cancellationToken);
        if (!disconnected)
        {
            return DisconnectAccountResult.Fail(AccountFailures.AlreadyDisconnected);
        }

        await tokens.DeleteAsync(account.Id, cancellationToken);
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
                a.DisconnectedAtUtc,
                a.Username,
                a.DisplayName,
                a.ProfilePictureUrl,
                a.AccountType,
                a.ProfileUpdatedAtUtc,
                a.SubscriptionHealth.ToString(),
                a.SubscriptionDetail,
                a.LastSubscriptionCheckUtc))
            .ToArray();
    }
}
