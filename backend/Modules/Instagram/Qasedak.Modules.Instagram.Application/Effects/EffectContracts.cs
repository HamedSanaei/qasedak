namespace Qasedak.Modules.Instagram.Application.Effects;

/// <summary>
/// Provider-effect kinds with independent one-shot semantics. The global semantic claim is
/// keyed by (ConnectedAccountId, ProviderCommentId, EffectType): two matching automations,
/// webhook redeliveries and future reconciliation (M13-013) can never consume the same
/// provider effect twice, and Private Reply never collides with Public Comment Reply.
/// </summary>
public enum InstagramEffectType
{
    /// <summary>One Private Reply per comment (recipient.comment_id on the messaging edge).</summary>
    PrivateReply = 1,

    /// <summary>One public comment reply per comment (the comment-replies edge).</summary>
    PublicCommentReply = 2,
}

/// <summary>
/// Durable lifecycle of one semantic effect claim. Transition rules (enforced by the
/// ledger adapter + coordinator, not by read-before-write code alone):
/// Reserved → Attempting is irreversible; after Attempting, NO automatic second provider
/// mutation may ever occur (one-reply compliance across crashes, timeouts and restarts).
/// </summary>
public enum InstagramEffectStatus
{
    /// <summary>Claim acquired; no provider mutation has been marked as attempted.</summary>
    Reserved = 1,

    /// <summary>Irreversible marker: a provider attempt may have begun / is in flight.</summary>
    Attempting = 2,

    /// <summary>Provider confirmed success (recipient_id + message_id persisted).</summary>
    Succeeded = 3,

    /// <summary>Provider explicitly rejected the mutation; never re-attempted.</summary>
    TerminalFailed = 4,

    /// <summary>Attempt began but the outcome is unknown (timeout/crash window); never re-attempted.</summary>
    Uncertain = 5,
}

/// <summary>Global semantic identity of one provider effect.</summary>
public sealed record CommentEffectClaimKey(Guid ConnectedAccountId, string ProviderCommentId, InstagramEffectType EffectType);

/// <summary>Snapshot of one effect claim row (no token, no raw provider body — enforced at the adapter).</summary>
public sealed record CommentEffectClaim(
    Guid Id,
    Guid ConnectedAccountId,
    string ProviderCommentId,
    InstagramEffectType EffectType,
    string OwnerOperationId,
    InstagramEffectStatus Status,
    DateTimeOffset? AttemptedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ProviderRecipientId,
    string? ProviderMessageId,
    string? FailureCode);

/// <summary>
/// Durable global semantic claim store. PostgreSQL enforces uniqueness of
/// (ConnectedAccountId, ProviderCommentId, EffectType); the claim is global across
/// automation ids, webhook redeliveries, process restarts and application instances.
/// </summary>
public interface ICommentEffectLedger
{
    /// <summary>
    /// Acquires (or resumes) the claim for one semantic key. Insert is a conditional
    /// upsert raced against every other candidate; on conflict the existing claim is
    /// returned when and only when the same logical owner already holds it in Reserved
    /// state (resume after crash). Any other state (including another owner) returns
    /// <c>null</c> — the caller must NOT issue a provider mutation.
    /// </summary>
    Task<CommentEffectClaim?> ReserveAsync(
        CommentEffectClaimKey key,
        string ownerOperationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the current claim state (replay path after crash/restart).</summary>
    Task<CommentEffectClaim?> FindAsync(
        CommentEffectClaimKey key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the irreversible attempt marker. Must be durable BEFORE any provider
    /// mutation is sent; after it exists, no automatic second mutation may occur.
    /// </summary>
    Task MarkAttemptingAsync(Guid claimId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    Task RecordSuccessAsync(
        Guid claimId,
        string providerRecipientId,
        string providerMessageId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task RecordTerminalFailureAsync(Guid claimId, string failureCode, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    Task RecordUncertainAsync(Guid claimId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}
