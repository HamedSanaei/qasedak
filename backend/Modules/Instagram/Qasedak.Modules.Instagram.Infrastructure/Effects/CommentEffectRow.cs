using Qasedak.Modules.Instagram.Application.Effects;

namespace Qasedak.Modules.Instagram.Infrastructure.Effects;

/// <summary>
/// Durable row for one global semantic effect claim (M13-009). NEVER stores tokens or raw
/// provider response bodies — only bounded provider success identity (recipient_id /
/// message_id) and stable failure codes.
/// </summary>
public sealed class CommentEffectRow
{
    public Guid Id { get; set; }

    public Guid ConnectedAccountId { get; set; }

    public string ProviderCommentId { get; set; } = string.Empty;

    public InstagramEffectType EffectType { get; set; }

    /// <summary>Deterministic logical owner: {automationId}|{version}|{triggerEventId}|{actionIndex}.</summary>
    public string OwnerOperationId { get; set; } = string.Empty;

    public InstagramEffectStatus Status { get; set; }

    public DateTimeOffset? AttemptedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>Provider success identity (official response recipient_id); bounded, no token.</summary>
    public string? ProviderRecipientId { get; set; }

    /// <summary>Provider success identity (official response message_id); bounded, no token.</summary>
    public string? ProviderMessageId { get; set; }

    public string? FailureCode { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
