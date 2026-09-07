namespace Qasedak.Modules.Instagram.Application.Reconciliation;

/// <summary>
/// One reconciled provider comment row, mapped into Qasedak-owned fields (M13-013 Phase A).
/// Only fields verified against the current official IG Comment contract appear here;
/// raw Graph DTOs, paging URLs and token material never cross this boundary.
/// Verified contract (developers.facebook.com IG Comment reference, retrieved
/// 2026-09-07): <c>from{id,username}</c>, <c>id</c>, <c>media{id}</c>, <c>parent_id</c>,
/// <c>text</c>, <c>timestamp</c> (ISO 8601 comment CREATION time — never notification
/// time), <c>hidden</c>. <c>username</c> is display metadata only and is never a
/// durable identity.
/// </summary>
public sealed record ProviderCommentRow(
    string CommentId,
    /// <summary>Commenter's IGSID (from.id). Null when the provider omits it — never fabricated.</summary>
    string? FromId,
    /// <summary>Commenter's username (from.username); display metadata, not durable identity.</summary>
    string? Username,
    string? Text,
    /// <summary>Authoritative comment creation time (provider timestamp), not notification time.</summary>
    DateTimeOffset CreatedAtUtc,
    /// <summary>Opaque provider media id the comment was made on (media.id).</summary>
    string MediaId,
    /// <summary>Parent comment id when this comment is a reply to another comment (parent_id).</summary>
    string? ParentCommentId,
    /// <summary>Provider hidden flag (hidden); kept for audit, never used to infer deletion.</summary>
    bool IsHidden);

/// <summary>One mapped provider comments page. The cursor is the bounded opaque provider
/// cursor COMPONENT (never a URL); null means no further pages.</summary>
public sealed record CommentHistoryPage(
    IReadOnlyList<ProviderCommentRow> Comments,
    string? NextAfterCursor,
    bool HasMore);

/// <summary>Outcome of one provider comments page call; never throws for provider behavior.</summary>
public abstract record CommentHistoryResult
{
    public sealed record Ok(CommentHistoryPage Page) : CommentHistoryResult;

    public sealed record Failed(string FailureCode, bool Transient) : CommentHistoryResult;
}

/// <summary>
/// Focused provider-facing port for the official media comments edge (M13-013 Phase A):
/// <c>GET {graph}/{version}/{IG_MEDIA_ID}/comments?fields=…&amp;limit=…[&amp;after=…]</c>
/// (IG Media Comments reference, retrieved 2026-09-07; permissions
/// <c>instagram_business_basic</c> + <c>instagram_business_manage_comments</c>,
/// Bearer IG User token, host <c>graph.instagram.com</c>).
/// Verified limitations honored: reverse-chronological order (Graph &gt;= 3.2), maximum
/// 50 comments per query, only top-level comments (replies require the <c>replies</c>
/// field expansion, which is deliberately NOT requested — replies are reconciled when
/// the parent surfaces as a top-level comment of a scanned media), no timestamp filter,
/// non-organic (ad/boosted) comments NOT available through Instagram Login (Marketing
/// API only), comments on live video only readable during the broadcast.
/// The caller resolves exact-account authorization and the protected token; this port
/// addresses the provider by the professional account's media id.
/// </summary>
public interface IInstagramCommentHistoryClient
{
    Task<CommentHistoryResult> ListCommentsPageAsync(
        string accessToken,
        string providerAccountId,
        string mediaId,
        int limit,
        string? afterCursor,
        CancellationToken cancellationToken = default);
}

/// <summary>Stable failure codes for comment history reads (mirror M13-003 taxonomy).</summary>
public static class CommentHistoryFailures
{
    public const string Unavailable = "comments.unavailable";

    public const string RateLimited = "comments.rateLimited";

    public const string PermissionLoss = "comments.permissionLoss";

    public const string Authentication = "comments.authentication";

    public const string NotFound = "comments.mediaNotFound";

    public const string Malformed = "comments.malformed";

    public const string CursorInvalid = "comments.cursorInvalid";

    public const string CursorOversized = "comments.cursorOversized";

    public const string CursorLoop = "comments.cursorLoop";
}
