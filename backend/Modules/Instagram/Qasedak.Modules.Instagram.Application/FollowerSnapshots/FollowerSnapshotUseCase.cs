using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.Insights;

namespace Qasedak.Modules.Instagram.Application.FollowerSnapshots;

/// <summary>Outcome of one daily follower-observation execution for an exact account.</summary>
public abstract record FollowerSnapshotOutcome
{
    /// <summary>Direct observation persisted (or already present at equal/higher quality).</summary>
    public sealed record Observed(long FollowerCount, DateOnly SnapshotDateUtc, DateTimeOffset ObservedAtUtc, bool Persisted) : FollowerSnapshotOutcome;

    /// <summary>Provider answered without a usable value; terminal for the occurrence, no row fabricated.</summary>
    public sealed record NoData(string FailureCode) : FollowerSnapshotOutcome;

    /// <summary>Transient provider/transport failure; safe to retry with backoff, nothing persisted.</summary>
    public sealed record Retryable(string FailureCode) : FollowerSnapshotOutcome;

    /// <summary>
    /// Account-level terminal state (unknown/disconnected/missing token/revoked/expired/
    /// permission loss at account level): no provider retry for this occurrence and no
    /// snapshot row; account health machinery owns follow-up.
    /// </summary>
    public sealed record AccountTerminal(string FailureCode) : FollowerSnapshotOutcome;
}

/// <summary>
/// Daily follower observation (M13-007): exact-account gate → protected token → ONE
/// bounded provider read of the verified absolute <c>followers_count</c> field → short
/// local conditional upsert (account/day uniqueness + provenance precedence enforced by
/// PostgreSQL). No database transaction is held during the provider call. A missing
/// provider value is NoData, never a fabricated row and never 0.
/// </summary>
public sealed class FollowerSnapshotUseCase(
    IConnectedAccountRepository accounts,
    IProtectedTokenStore tokens,
    IInstagramInsightsClient insights,
    IFollowerSnapshotStore snapshots,
    Qasedak.BuildingBlocks.Application.IClock clock)
{
    public async Task<FollowerSnapshotOutcome> ExecuteAsync(
        Guid accountId,
        DateOnly snapshotDateUtc,
        CancellationToken cancellationToken = default)
    {
        var account = await accounts.FindByIdAsync(accountId, cancellationToken);
        if (account is null)
        {
            return new FollowerSnapshotOutcome.AccountTerminal(AccountFailures.NotFound);
        }

        if (account.IsDisconnected)
        {
            // Zero token read, zero provider call, no fabrication, no reconnect.
            return new FollowerSnapshotOutcome.AccountTerminal(AccountFailures.AlreadyDisconnected);
        }

        var accessToken = await tokens.GetAsync(account.Id, cancellationToken);
        if (string.IsNullOrEmpty(accessToken))
        {
            // Active account without token material: actionable inconsistency; never fall
            // back to another account's token.
            return new FollowerSnapshotOutcome.AccountTerminal(AccountFailures.TokenMissing);
        }

        var observed = await insights.GetFollowerCountAsync(accessToken, account.ProviderUserId, cancellationToken);
        switch (observed)
        {
            case FollowerCountResult.Value value:
                {
                    var observedAtUtc = clock.UtcNow;
                    var persisted = await snapshots.UpsertAsync(
                        new FollowerSnapshotRecord(accountId, snapshotDateUtc, value.FollowerCount,
                            FollowerSnapshotProvenance.Observed, observedAtUtc),
                        cancellationToken);
                    return new FollowerSnapshotOutcome.Observed(value.FollowerCount, snapshotDateUtc, observedAtUtc, persisted);
                }

            case FollowerCountResult.NoData noData:
                return new FollowerSnapshotOutcome.NoData(noData.FailureCode);

            case FollowerCountResult.Failed failed:
                return failed.Kind switch
                {
                    // Rate limits, Meta 5xx and transport failures are retryable: no fake
                    // snapshot row is ever written as a fallback.
                    InsightsFailureKind.RateLimited or InsightsFailureKind.Transient or InsightsFailureKind.Transport =>
                        new FollowerSnapshotOutcome.Retryable(failed.FailureCode),
                    // Basic-permission loss / authentication problems are account-level:
                    // the account-health machinery (M13-005) owns them; do not retry this
                    // occurrence forever.
                    InsightsFailureKind.PermissionLoss or InsightsFailureKind.Authentication =>
                        new FollowerSnapshotOutcome.AccountTerminal(failed.FailureCode),
                    // Malformed responses / contract drift: terminal for the occurrence
                    // (retrying cannot fix a broken shape); no fabrication; the bounded
                    // next-day schedule continues to detect recovery.
                    _ => new FollowerSnapshotOutcome.NoData(failed.FailureCode),
                };

            default:
                return new FollowerSnapshotOutcome.NoData("followers.unknownProviderOutcome");
        }
    }
}
