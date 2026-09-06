namespace Qasedak.Modules.Instagram.Application.Effects;

/// <summary>
/// Result of reading one comment reference. Only the creation timestamp is needed —
/// this is the smallest focused read for authoritative 7-day policy enforcement, NOT a
/// general comments synchronization client (M13-013 owns reconciliation).
/// </summary>
public abstract record CommentReferenceReadResult
{
    /// <summary>Authoritative ISO 8601 comment creation time (official IG Comment field).</summary>
    public sealed record Found(DateTimeOffset CreatedAtUtc) : CommentReferenceReadResult;

    /// <summary>Meta answered but the comment does not exist (deleted or never readable).</summary>
    public sealed record NotFound : CommentReferenceReadResult;

    /// <summary>Transport/error/cancellation — the exact creation time could not be read.</summary>
    public sealed record Unavailable : CommentReferenceReadResult;
}

/// <summary>
/// Focused port for the official IG Comment reference read:
/// GET {graph}/{version}/{comment_id}?fields=timestamp (Bearer IG User token,
/// instagram_business_basic + instagram_business_manage_comments). Used only after
/// durable inbox acceptance, never inside the webhook HTTP endpoint. Token material
/// never appears in results.
/// </summary>
public interface ICommentReferenceReader
{
    Task<CommentReferenceReadResult> ReadCreatedAtUtcAsync(
        string accessToken,
        string commentId,
        CancellationToken cancellationToken = default);
}
