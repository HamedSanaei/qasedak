namespace Qasedak.Modules.Instagram.Application.Reconciliation;

/// <summary>
/// Channel-neutral reconciliation scope needed by the Instagram module for one exact
/// connected account (M13-013 §13/§14). The composition root implements this port by
/// querying the Automations module — Automations never references Instagram, and
/// Instagram never sees automation message text, actions or definitions: it only
/// learns which media need comment reconciliation and whether ANY active
/// comment-trigger automation exists for the account.
/// </summary>
public interface ICommentReconciliationScopeQuery
{
    /// <summary>
    /// Resolves the comment-reconciliation media scope for the exact account.
    /// <c>null</c> means "no active comment-trigger automation for this account" —
    /// callers MUST perform zero provider traffic in that case.
    /// </summary>
    Task<CommentReconciliationScope?> ResolveAsync(
        Guid workspaceId,
        Guid connectedAccountId,
        CancellationToken cancellationToken = default);
}

/// <summary>Result of the scope query: has any active comment automation, plus the
/// union of specific source media ids (deduplicated, bounded by the caller).</summary>
public sealed record CommentReconciliationScope(
    bool HasAnySourceCommentAutomation,
    IReadOnlyList<string> SpecificSourceMediaIds)
{
    /// <summary>True when this account needs any comment history traffic at all.</summary>
    public bool HasAnyCommentAutomation => HasAnySourceCommentAutomation || SpecificSourceMediaIds.Count > 0;

    public static CommentReconciliationScope None() => new(false, []);
}
