using Qasedak.Modules.Instagram.Application.RevealFlow;

namespace Qasedak.Modules.Instagram.Infrastructure.Persistence;

/// <summary>
/// Durable row for one Instagram reveal continuation (M13-011). NEVER stores access
/// tokens, raw webhook bodies, raw provider responses or correlation tokens — only the
/// SHA-256 hash of the postback correlation token. Bounded invocation-owned message
/// content is persisted ONLY because continuation events carry no content: a restart
/// between steps must reproduce the exact same gate/reveal payloads.
/// </summary>
public sealed class RevealFlowRow
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid ConnectedAccountId { get; set; }

    public string ProviderCommentId { get; set; } = string.Empty;

    /// <summary>Participant IGSID; authoritative value comes from the provider-confirmed Private Reply recipient_id when the comment carried none.</summary>
    public string? ParticipantIGSID { get; set; }

    /// <summary>True when the origin comment is a Live comment (M13-009 policy: opening is attempt-once, never 7-day fallback).</summary>
    public bool IsLiveComment { get; set; }

    /// <summary>Notification time of the origin webhook (provider entry.time), used for the one-sided notification guard.</summary>
    public DateTimeOffset NotificationOccurredAtUtc { get; set; }

    /// <summary>Deterministic owner metadata when an automation maps into this flow (M13-012); never part of the Private Reply claim key.</summary>
    public Guid? AutomationId { get; set; }

    public int? AutomationVersionNumber { get; set; }

    public string? TriggerEventId { get; set; }

    public int? ActionIndex { get; set; }

    // Bounded invocation-owned content (validated with MessageValidationPolicy at start).
    public string OpeningPrivateReplyText { get; set; } = string.Empty;

    public string GatePromptText { get; set; } = string.Empty;

    public string PostbackButtonTitle { get; set; } = string.Empty;

    public string? FollowUrl { get; set; }

    public string? FollowButtonTitle { get; set; }

    public string RevealText { get; set; } = string.Empty;

    public FollowGateMode FollowGateMode { get; set; }

    /// <summary>Provider success identity of the opening Private Reply (message_id).</summary>
    public string? OpeningPrivateReplyMessageId { get; set; }

    /// <summary>Provider success identity of the opening Private Reply (recipient_id — the authoritative participant IGSID).</summary>
    public string? OpeningPrivateReplyRecipientId { get; set; }

    public RevealFlowState State { get; set; }

    /// <summary>Consent/window anchor: the latest qualifying inbound user message time (monotonic).</summary>
    public DateTimeOffset? LastUserMessageAtUtc { get; set; }

    public RevealGatePromptStatus GatePromptStatus { get; set; }

    public string? GatePromptProviderMessageId { get; set; }

    public DateTimeOffset? GatePromptAttemptedAtUtc { get; set; }

    public string? GatePromptFailureCode { get; set; }

    /// <summary>SHA-256 hash of the raw postback correlation token — the raw token never persists.</summary>
    public string? CorrelationTokenHash { get; set; }

    public string? CorrelationTokenPurpose { get; set; }

    public RevealSendStatus RevealStatus { get; set; }

    public string? RevealProviderMessageId { get; set; }

    public DateTimeOffset? RevealAttemptedAtUtc { get; set; }

    public DateTimeOffset? RevealCompletedAtUtc { get; set; }

    public string? RevealFailureCode { get; set; }

    public FollowState? LastFollowState { get; set; }

    public FollowStateUnavailableReason? LastFollowUnavailableReason { get; set; }

    public DateTimeOffset? LastFollowCheckAtUtc { get; set; }

    public string? FailureCode { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
