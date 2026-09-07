using Qasedak.BuildingBlocks.Application;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.Media;
using Qasedak.Modules.Instagram.Application.Webhooks;
using Qasedak.Modules.Instagram.Domain.Accounts;

namespace Qasedak.Modules.Instagram.Application.Reconciliation;

/// <summary>
/// One bounded comment-reconciliation sweep for ONE exact connected account (M13-013
/// Phase A). Invariants:
/// - no active comment automation for the account ⇒ ZERO provider calls (the scope
///   query is the gate; the scheduled occurrence still completes and chains);
/// - specific-source media ids are used directly (never re-resolved by permalink);
/// - AnySource reuses the M13-006 bounded recent-media traversal (no second media
///   enumeration adapter), unioned with specific ids and deduplicated;
/// - every sweep starts from the newest page (never resumes from a previous sweep's
///   `after` cursor — new comments can insert above a persisted cursor and create
///   permanent gaps; semantic run/effect idempotency removes duplicates);
/// - self/owner comments (FromId == ProviderAccountId) are skipped; comments without
///   an author id fail closed (never dispatched — a self-loop cannot be excluded);
/// - comments older than <see cref="CommentReconciliationPolicy.MaxCommentAge"/> are
///   not dispatched;
/// - recovered comments enter the EXISTING semantic automation path through
///   <see cref="IIntegrationEventDispatcher"/> with the provider CommentId as the
///   semantic identity — the same run ledger and M13-009 comment_effects make
///   webhook-vs-reconciliation convergence exact. No webhook body, no inbox row, no
///   HMAC, no second evaluator.
/// Provider HTTP always happens outside any DB transaction; cancellation stops
/// traversal without being classified as a provider failure.
/// </summary>
public sealed class CommentReconciliationUseCase(
    IConnectedAccountRepository accounts,
    IProtectedTokenStore tokens,
    ICommentReconciliationScopeQuery scopeQuery,
    IMediaCatalogClient mediaCatalog,
    IInstagramCommentHistoryClient comments,
    IIntegrationEventDispatcher dispatcher,
    IClock clock) : ICommentSweep
{
    /// <summary>Runs one sweep. Returns the low-cardinality outcome for the handler.</summary>
    public async Task<CommentSweepOutcome> ExecuteAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await accounts.FindByIdAsync(accountId, cancellationToken);
        if (account is null || account.IsDisconnected)
        {
            return new CommentSweepOutcome(ProviderTrafficAttempted: false, 0, 0, 0, 0, 0, 0, 0, 0, false, "account.notAvailable");
        }

        var accessToken = await tokens.GetAsync(account.Id, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new CommentSweepOutcome(ProviderTrafficAttempted: false, 0, 0, 0, 0, 0, 0, 0, 0, false, AccountFailures.TokenMissing);
        }

        var scope = await scopeQuery.ResolveAsync(account.WorkspaceId, account.Id, cancellationToken);
        if (scope is null || !scope.HasAnyCommentAutomation)
        {
            // Hard invariant (§14): no active comment automation ⇒ zero provider traffic.
            return new CommentSweepOutcome(ProviderTrafficAttempted: false, 0, 0, 0, 0, 0, 0, 0, 0, false, null);
        }

        var mediaIds = await ResolveMediaTargetsAsync(account, accessToken, scope, cancellationToken);
        if (mediaIds.Count == 0)
        {
            return new CommentSweepOutcome(ProviderTrafficAttempted: true, 0, 0, 0, 0, 0, 0, 0, 0, false, "media.none");
        }

        var outcome = new SweepCounters();
        var now = clock.UtcNow;

        foreach (var mediaId in mediaIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (outcome.CommentsObserved >= CommentReconciliationPolicy.MaxCommentsPerSweep)
            {
                break;
            }

            await SweepMediaAsync(account, accessToken, mediaId, now, outcome, cancellationToken);
        }

        return outcome.ToResult(providerTrafficAttempted: true);
    }

    private async Task<IReadOnlyList<string>> ResolveMediaTargetsAsync(
        ConnectedAccount account, string accessToken, CommentReconciliationScope scope, CancellationToken cancellationToken)
    {
        var targets = new List<string>(scope.SpecificSourceMediaIds.Distinct(StringComparer.Ordinal));
        if (targets.Count >= CommentReconciliationPolicy.MaxMediaPerSweep)
        {
            // Deterministic bounded subset; remaining specific ids are picked up by a
            // future sweep (each sweep re-reads the scope). No unbounded fan-out.
            return targets.Take(CommentReconciliationPolicy.MaxMediaPerSweep).ToList();
        }

        if (scope.HasAnySourceCommentAutomation)
        {
            // Reuse M13-006 bounded recent-media traversal; never a second enumeration.
            var remaining = CommentReconciliationPolicy.MaxMediaPerSweep - targets.Count;
            var recent = await mediaCatalog.GetRecentAsync(accessToken, account.ProviderUserId, account.Id, remaining, cancellationToken);
            if (recent is MediaCatalogResult.Ok ok)
            {
                var seen = new HashSet<string>(targets, StringComparer.Ordinal);
                foreach (var item in ok.Page.Items)
                {
                    if (seen.Add(item.ProviderMediaId))
                    {
                        targets.Add(item.ProviderMediaId);
                    }

                    if (targets.Count >= CommentReconciliationPolicy.MaxMediaPerSweep)
                    {
                        break;
                    }
                }
            }
        }

        return targets;
    }

    private async Task SweepMediaAsync(
        ConnectedAccount account, string accessToken, string mediaId, DateTimeOffset now,
        SweepCounters outcome, CancellationToken cancellationToken)
    {
        outcome.MediaScanned++;
        string? cursor = null;
        var pages = 0;
        var previousCursors = new HashSet<string>(StringComparer.Ordinal);

        while (pages < CommentReconciliationPolicy.MaxCommentPagesPerMedia &&
               outcome.CommentsObserved < CommentReconciliationPolicy.MaxCommentsPerSweep)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await comments.ListCommentsPageAsync(
                accessToken, account.ProviderUserId, mediaId, CommentReconciliationPolicy.PageSize, cursor, cancellationToken);

            if (result is not CommentHistoryResult.Ok ok)
            {
                if (result is CommentHistoryResult.Failed failed)
                {
                    outcome.RateLimited = failed.FailureCode == CommentHistoryFailures.RateLimited;
                    outcome.FailureCode = failed.FailureCode;
                }

                return; // bounded failure; the occurrence reports backoff/permanent to M13-004
            }

            pages++;
            outcome.CommentPagesScanned++;

            foreach (var comment in ok.Page.Comments)
            {
                outcome.CommentsObserved++;
                if (outcome.CommentsObserved > CommentReconciliationPolicy.MaxCommentsPerSweep)
                {
                    break;
                }

                if (now - comment.CreatedAtUtc > CommentReconciliationPolicy.MaxCommentAge)
                {
                    continue; // outside the conservative reconciliation horizon
                }

                if (comment.FromId is null)
                {
                    // Fail closed: without an author id a self-loop cannot be excluded.
                    outcome.UnknownAuthorSkipped++;
                    continue;
                }

                if (comment.FromId == account.ProviderUserId)
                {
                    // Provider/account-owned comment: zero automation dispatch, zero effects.
                    outcome.SelfSkipped++;
                    continue;
                }

                outcome.CommentsDispatched++;
                await DispatchRecoveredCommentAsync(account, comment, cancellationToken);
            }

            if (!ok.Page.HasMore || ok.Page.NextAfterCursor is null)
            {
                return;
            }

            // Only the bounded opaque cursor component is ever reused — never a provider URL.
            if (ok.Page.NextAfterCursor.Length > CommentReconciliationPolicy.MaxProviderCursorLength)
            {
                outcome.FailureCode = CommentHistoryFailures.CursorOversized;
                return;
            }

            if (!previousCursors.Add(ok.Page.NextAfterCursor))
            {
                outcome.FailureCode = CommentHistoryFailures.CursorLoop;
                return;
            }

            cursor = ok.Page.NextAfterCursor;
        }
    }

    private async Task DispatchRecoveredCommentAsync(ConnectedAccount account, ProviderCommentRow comment, CancellationToken cancellationToken)
    {
        // The provider timestamp is the authoritative comment creation time — never
        // notification time (§21). MediaId is preserved; OriginalMediaId stays null
        // because the Instagram Login comments surface cannot enumerate ad/boosted
        // originals (recorded coverage limitation).
        var recovered = new InstagramCommentCreated(
            EventId: $"recon:{account.Id:N}:{comment.CommentId}",
            WorkspaceId: account.WorkspaceId,
            ConnectedAccountId: account.Id,
            ProviderAccountId: account.ProviderUserId,
            CommentId: comment.CommentId,
            FromId: comment.FromId,
            CommenterUsername: comment.Username,
            Text: comment.Text,
            MediaId: comment.MediaId,
            OriginalMediaId: null,
            CreatedAtUtc: comment.CreatedAtUtc,
            IsLiveComment: false);

        // The fan-out reaches the SAME AutomationCommentBridge as webhook comments;
        // the run ledger keys on the provider comment id so webhook+reconciliation
        // converge on one logical trigger/run/effect.
        await dispatcher.DispatchAsync(recovered, cancellationToken);
    }

    private sealed class SweepCounters
    {
        public int MediaScanned;
        public int CommentPagesScanned;
        public int CommentsObserved;
        public int CommentsDispatched;
        public int UnknownAuthorSkipped;
        public int SelfSkipped;
        public bool RateLimited;
        public string? FailureCode;

        public CommentSweepOutcome ToResult(bool providerTrafficAttempted) => new(
            providerTrafficAttempted,
            MediaScanned,
            CommentPagesScanned,
            CommentsObserved,
            CommentsDispatched,
            Duplicates: 0,
            SelfSkipped,
            UnknownAuthorSkipped,
            UnsupportedSurfaceSkipped: 0,
            RateLimited,
            FailureCode);
    }
}
