using System.Diagnostics.Metrics;
using Qasedak.Modules.Instagram.Application.Effects;

namespace Qasedak.Modules.Instagram.Infrastructure.Effects;

/// <summary>
/// Comment-effect observability: one meter with low-cardinality counters. Dimensions are
/// effect type, outcome and failure category ONLY — never CommentId, AccountId,
/// AutomationId, username or message text. Implements the Application observability port.
/// </summary>
public sealed class CommentEffectMetrics : ICommentEffectObservability, IDisposable
{
    public const string MeterName = "Qasedak.Instagram.CommentEffects";

    private readonly Meter _meter = new(MeterName);

    /// <summary>Private/public reply claims acquired, tagged by effect.</summary>
    private readonly Counter<long> _claimsAcquired;

    /// <summary>Claim conflicts (another operation owns the effect), tagged by effect.</summary>
    private readonly Counter<long> _claimsConflicted;

    /// <summary>Local policy rejections before any claim, tagged by effect + outcome.</summary>
    private readonly Counter<long> _policyRejected;

    /// <summary>Irreversible attempt markers followed by a provider call, tagged by effect.</summary>
    private readonly Counter<long> _attempted;

    /// <summary>Provider-confirmed successes, tagged by effect.</summary>
    private readonly Counter<long> _succeeded;

    /// <summary>Terminal provider failures, tagged by effect + category.</summary>
    private readonly Counter<long> _failed;

    /// <summary>Uncertain outcomes (attempt begun, result unknown), tagged by effect.</summary>
    private readonly Counter<long> _uncertain;

    public CommentEffectMetrics()
    {
        _claimsAcquired = _meter.CreateCounter<long>(
            "qasedak.instagram.comment_effect.claim_acquired",
            unit: "{claim}",
            description: "Semantic effect claims acquired by effect type");
        _claimsConflicted = _meter.CreateCounter<long>(
            "qasedak.instagram.comment_effect.claim_conflict",
            unit: "{claim}",
            description: "Semantic effect claims already owned by another operation");
        _policyRejected = _meter.CreateCounter<long>(
            "qasedak.instagram.comment_effect.policy_rejected",
            unit: "{effect}",
            description: "Deterministic policy rejections before any claim by effect and outcome");
        _attempted = _meter.CreateCounter<long>(
            "qasedak.instagram.comment_effect.attempted",
            unit: "{effect}",
            description: "Provider mutations issued after the durable attempt marker");
        _succeeded = _meter.CreateCounter<long>(
            "qasedak.instagram.comment_effect.succeeded",
            unit: "{effect}",
            description: "Provider-confirmed successes by effect type");
        _failed = _meter.CreateCounter<long>(
            "qasedak.instagram.comment_effect.failed",
            unit: "{effect}",
            description: "Terminal provider failures by effect type and category");
        _uncertain = _meter.CreateCounter<long>(
            "qasedak.instagram.comment_effect.uncertain",
            unit: "{effect}",
            description: "Uncertain outcomes — attempt begun, result unknown, never re-attempted");
    }

    public void ClaimAcquired(InstagramEffectType effectType) =>
        _claimsAcquired.Add(1, new KeyValuePair<string, object?>("effect", EffectTag(effectType)));

    public void ClaimConflict(InstagramEffectType effectType) =>
        _claimsConflicted.Add(1, new KeyValuePair<string, object?>("effect", EffectTag(effectType)));

    public void PolicyRejected(InstagramEffectType effectType, string outcome) =>
        _policyRejected.Add(1,
            new KeyValuePair<string, object?>("effect", EffectTag(effectType)),
            new KeyValuePair<string, object?>("outcome", outcome));

    public void Attempted(InstagramEffectType effectType) =>
        _attempted.Add(1, new KeyValuePair<string, object?>("effect", EffectTag(effectType)));

    public void Succeeded(InstagramEffectType effectType) =>
        _succeeded.Add(1, new KeyValuePair<string, object?>("effect", EffectTag(effectType)));

    public void Failed(InstagramEffectType effectType, string failureCategory) =>
        _failed.Add(1,
            new KeyValuePair<string, object?>("effect", EffectTag(effectType)),
            new KeyValuePair<string, object?>("category", failureCategory));

    public void Uncertain(InstagramEffectType effectType) =>
        _uncertain.Add(1, new KeyValuePair<string, object?>("effect", EffectTag(effectType)));

    private static string EffectTag(InstagramEffectType effectType) => effectType switch
    {
        InstagramEffectType.PrivateReply => "private_reply",
        InstagramEffectType.PublicCommentReply => "public_reply",
        _ => "unknown",
    };

    public void Dispose() => _meter.Dispose();
}
