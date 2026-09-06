using Qasedak.BuildingBlocks.Application;
using Qasedak.Modules.Instagram.Application.OAuth;
using Qasedak.Modules.Instagram.Domain.Accounts;

namespace Qasedak.Modules.Instagram.Application.Accounts;

/// <summary>Outcome of one token-refresh execution for an exact account.</summary>
public abstract record TokenRefreshOutcome
{
    /// <summary>Rotation committed locally; carries the new expiry for next scheduling.</summary>
    public sealed record Rotated(DateTimeOffset NewExpiryUtc) : TokenRefreshOutcome;

    /// <summary>Terminal no-op (disconnected, missing token, never-expiring path).</summary>
    public sealed record Skipped(string FailureCode) : TokenRefreshOutcome;

    /// <summary>Another worker won the rotation concurrently; caller decides idempotently.</summary>
    public sealed record Stale(DateTimeOffset? KnownExpiryUtc) : TokenRefreshOutcome;

    /// <summary>Transient failure; safe to retry with backoff, health untouched.</summary>
    public sealed record Retryable(string FailureCode) : TokenRefreshOutcome;

    /// <summary>Permanent failure; health updated truthfully, no further automatic retry.</summary>
    public sealed record Permanent(string FailureCode) : TokenRefreshOutcome;
}

/// <summary>
/// Rotates one exact account's long-lived token (M13-005). Flow: load tracked
/// aggregate → resolve protected token → Meta refresh OUTSIDE any long transaction
/// → mutate aggregate + replace ciphertext → ONE SaveChanges (atomic, generation
/// guarded). A stale concurrent rotation surfaces as Stale instead of overwriting.
/// Health degrades only on classified permanent failures (via a follow-up token
/// inspection, never on transport noise).
///
/// At-least-once window: if the host crashes after Meta accepts the refresh but
/// before the single local commit, NOTHING is committed (staged ciphertext rolls
/// back with the aggregate). The retry then refreshes from the previous stored
/// token; when Meta has already invalidated it, the rejection is classified by
/// live inspection (typically Expired → reconnect required) instead of being
/// retried blindly. Exactly-once provider refresh is not claimed.
/// </summary>
public sealed class RefreshInstagramTokenUseCase(
    IConnectedAccountRepository accounts,
    IProtectedTokenStore tokens,
    IMetaOAuthClient oauth,
    IMetaTokenInspector inspector,
    IClock clock)
{
    public async Task<TokenRefreshOutcome> ExecuteAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await accounts.FindByIdAsync(accountId, cancellationToken);
        if (account is null)
        {
            return new TokenRefreshOutcome.Skipped(AccountFailures.NotFound);
        }

        if (account.IsDisconnected)
        {
            return new TokenRefreshOutcome.Skipped(AccountFailures.AlreadyDisconnected);
        }

        if (account.TokenExpiresAtUtc is null)
        {
            // Never-expiring path (e.g. FB Page tokens): nothing to refresh.
            return new TokenRefreshOutcome.Skipped(AccountFailures.RefreshNotRequired);
        }

        if (!TokenRefreshPolicy.SatisfiesAgeRule(account.LastTokenIssuedAtUtc, clock.UtcNow))
        {
            // Provider minimum-age rule: Meta rejects refreshes of tokens younger
            // than 24h. Back off and retry later; health is untouched.
            return new TokenRefreshOutcome.Retryable(AccountFailures.OAuthUnavailable);
        }

        var accessToken = await tokens.GetAsync(account.Id, cancellationToken);
        if (string.IsNullOrEmpty(accessToken))
        {
            account.MarkUnhealthy("Stored token is missing; reconnect the account.");
            if (!await accounts.TrySaveChangesAsync(cancellationToken))
            {
                // Lost to a concurrent mutation (e.g. disconnect): retrying
                // re-observes the new state instead of throwing past the caller.
                return new TokenRefreshOutcome.Stale(account.TokenExpiresAtUtc);
            }

            return new TokenRefreshOutcome.Permanent(AccountFailures.TokenMissing);
        }

        var knownExpiry = account.TokenExpiresAtUtc;
        var refreshed = await oauth.RefreshLongLivedAsync(accessToken, cancellationToken);
        if (refreshed.Failure is not null)
        {
            return await MapFailureAsync(account, knownExpiry, refreshed.Failure.Reason, cancellationToken);
        }

        var rotatedExpiry = clock.UtcNow.AddSeconds(refreshed.Success!.ExpiresInSeconds);
        account.ApplyTokenRotation(rotatedExpiry, clock.UtcNow);
        await tokens.StoreAsync(account.Id, refreshed.Success.AccessToken, cancellationToken);
        // One atomic save covers aggregate + ciphertext (shared context). A concurrent
        // rotation surfaces as a stale loss instead of an overwrite.
        if (!await accounts.TrySaveChangesAsync(cancellationToken))
        {
            return new TokenRefreshOutcome.Stale(knownExpiry);
        }

        return new TokenRefreshOutcome.Rotated(rotatedExpiry);
    }

    private async Task<TokenRefreshOutcome> MapFailureAsync(
        ConnectedAccount account,
        DateTimeOffset? knownExpiry,
        MetaOAuthFailureReason reason,
        CancellationToken cancellationToken)
    {
        switch (reason)
        {
            case MetaOAuthFailureReason.TransportFailure:
                return new TokenRefreshOutcome.Retryable(AccountFailures.OAuthUnavailable);
            case MetaOAuthFailureReason.MalformedResponse:
                return new TokenRefreshOutcome.Retryable(AccountFailures.OAuthUnavailable);
            default:
                {
                    // Rejected by Meta: classify with a live inspection so transient noise
                    // never degrades health and permanent states are named precisely.
                    var current = await tokens.GetAsync(account.Id, cancellationToken);
                    var inspection = string.IsNullOrEmpty(current)
                        ? TokenInspection.From(TokenInspectionKind.Revoked, "Token material is gone.")
                        : await inspector.InspectAsync(current, cancellationToken);
                    switch (inspection.Kind)
                    {
                        case TokenInspectionKind.Expired:
                            account.MarkExpired();
                            break;
                        case TokenInspectionKind.Revoked:
                            account.MarkRevoked("Meta reports the grant revoked or invalid.");
                            break;
                        case TokenInspectionKind.PermissionLoss:
                            account.MarkUnhealthy("A required Meta permission was removed; reconnect or re-grant it.");
                            break;
                        default:
                            return new TokenRefreshOutcome.Retryable(AccountFailures.OAuthUnavailable);
                    }

                    // Health writes tolerate a lost concurrent mutation the same way
                    // rotation does: the retry re-observes instead of throwing.
                    if (!await accounts.TrySaveChangesAsync(cancellationToken))
                    {
                        return new TokenRefreshOutcome.Stale(knownExpiry);
                    }

                    return inspection.Kind switch
                    {
                        TokenInspectionKind.Expired => new TokenRefreshOutcome.Permanent(AccountFailures.TokenExpired),
                        _ => new TokenRefreshOutcome.Permanent(AccountFailures.OAuthRejected),
                    };
                }
        }
    }
}
