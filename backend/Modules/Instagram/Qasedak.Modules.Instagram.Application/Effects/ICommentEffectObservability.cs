namespace Qasedak.Modules.Instagram.Application.Effects;

/// <summary>
/// Observability seam for comment effect outcomes. Implementations MUST keep dimensions
/// low-cardinality (effect type / outcome / failure category only) — never CommentId,
/// AccountId, AutomationId, username or message text.
/// </summary>
public interface ICommentEffectObservability
{
    /// <summary>Global semantic claim acquired by this operation (own claim, Reserved).</summary>
    void ClaimAcquired(InstagramEffectType effectType);

    /// <summary>Claim already held by another operation — zero provider mutation.</summary>
    void ClaimConflict(InstagramEffectType effectType);

    /// <summary>Local policy rejected the effect before any claim/provider call.</summary>
    void PolicyRejected(InstagramEffectType effectType, string outcome);

    /// <summary>Irreversible attempt marker persisted; provider mutation issued.</summary>
    void Attempted(InstagramEffectType effectType);

    /// <summary>Provider confirmed success.</summary>
    void Succeeded(InstagramEffectType effectType);

    /// <summary>Provider explicitly rejected the mutation (terminal).</summary>
    void Failed(InstagramEffectType effectType, string failureCategory);

    /// <summary>Attempt began but the outcome is unknown (terminal; never re-attempted).</summary>
    void Uncertain(InstagramEffectType effectType);
}
