using Qasedak.Modules.Instagram.Application.Messaging;

namespace Qasedak.Modules.Instagram.Application.RevealFlow;

/// <summary>
/// Durable lifecycle of one Instagram reveal continuation (M13-011). The provider-correct
/// sequence is: comment → PlainText Private Reply (M13-009 one-shot effect) → the user
/// replies → consent + 24h Direct window proven → Direct button-template gate prompt →
/// user taps the postback → exact correlation validated → optional follow-state check →
/// single Direct reveal message.
///
/// Transition rules (enforced by the store adapter with PostgreSQL, not by read-before-
/// write code alone): every provider mutation is preceded by a durable Attempting-style
/// marker; after that marker NO automatic second provider call ever occurs — timeouts,
/// crashes and redeliveries replay or fail closed.
/// </summary>
public enum RevealFlowState
{
    /// <summary>Flow row created; the opening Private Reply has not been attempted yet.</summary>
    Starting = 1,

    /// <summary>Opening Private Reply attempt is in flight (replay of an ambiguous attempt stays here).</summary>
    OpeningAttempted = 2,

    /// <summary>Opening delivered; waiting for the user's first reply (the consent/window hinge).</summary>
    AwaitingUserResponse = 3,

    /// <summary>User responded; the gate prompt attempt is in flight or its outcome is ambiguous.</summary>
    PreparingGatePrompt = 4,

    /// <summary>Gate prompt delivered; waiting for a valid correlated postback.</summary>
    AwaitingPostback = 5,

    /// <summary>Postback validated and reveal authority obtained; the reveal attempt is in flight.</summary>
    Revealing = 6,

    /// <summary>The single reveal message was confirmed delivered.</summary>
    Revealed = 7,

    /// <summary>The 24h Direct window lapsed (or will never open) before reveal; terminal, zero sends.</summary>
    Expired = 8,

    /// <summary>Terminal failure (provider rejection, local conflict, policy rejection).</summary>
    TerminalFailed = 9,

    /// <summary>Ambiguous outcome after an attempt marker; never re-attempted.</summary>
    Uncertain = 10,
}

/// <summary>Durable status of the one gate-prompt Direct send (at-most-once semantics).</summary>
public enum RevealGatePromptStatus
{
    NotAttempted = 0,
    Attempting = 1,
    Succeeded = 2,
    TerminalFailed = 3,
    Uncertain = 4,
}

/// <summary>Durable status of the one reveal Direct send (single-reveal semantics).</summary>
public enum RevealSendStatus
{
    NotAttempted = 0,
    Attempting = 1,
    Succeeded = 2,
    TerminalFailed = 3,
    Uncertain = 4,
}

/// <summary>Provider follow-relationship state (tri-state; a missing/error never becomes false).</summary>
public enum FollowState
{
    /// <summary>The business's is_user_follow_business field read true.</summary>
    Follows = 1,

    /// <summary>The business's is_user_follow_business field read false.</summary>
    DoesNotFollow = 2,

    /// <summary>No authoritative data (provider error/consent/malformed) — never fabricated.</summary>
    UnknownUnavailable = 3,
}

/// <summary>Bounded reason behind <see cref="FollowState.UnknownUnavailable"/>.</summary>
public enum FollowStateUnavailableReason
{
    ConsentUnavailable = 1,
    PermissionUnavailable = 2,
    Transient = 3,
    Malformed = 4,
}

/// <summary>Follow-gate capability mode (startup/config or current contract decides; never inferred by probing).</summary>
public enum FollowGateMode
{
    /// <summary>No follow check; the provider-independent postback/reveal core always works.</summary>
    Disabled = 0,

    /// <summary>Check is_user_follow_business only when a proven consent basis exists; unknown never reveals.</summary>
    EnabledWhenSupported = 1,
}

/// <summary>
/// Invocation-owned content for one reveal flow (M13-011). M13-012 will later map stable
/// automation configuration into these values; M13-011 never persists them into
/// AutomationDefinition. All text is validated with <see cref="MessageValidationPolicy"/>
/// before any durable row or provider call.
/// </summary>
public sealed record RevealFlowContent(
    string OpeningPrivateReplyText,
    string GatePromptText,
    string PostbackButtonTitle,
    string? FollowUrl,
    string? FollowButtonTitle,
    string RevealText);

/// <summary>Start command for one reveal continuation (opening Private Reply + durable follow-through).</summary>
public sealed record StartRevealFlowCommand(
    Guid WorkspaceId,
    Guid ConnectedAccountId,
    string ProviderCommentId,
    string? ParticipantIGSID,
    bool IsLiveComment,
    DateTimeOffset NotificationOccurredAtUtc,
    RevealFlowContent Content,
    FollowGateMode FollowGateMode,
    /// <summary>Deterministic owner metadata when an automation maps into this flow (M13-012); never used for the Private Reply claim key.</summary>
    Guid? AutomationId = null,
    int? AutomationVersionNumber = null,
    string? TriggerEventId = null,
    int? ActionIndex = null);

/// <summary>
/// Durable snapshot of one reveal flow (no token, no raw provider body). Bounded
/// invocation-owned content is included ONLY because continuation events (user response,
/// postback) carry no content: a restart between steps must reproduce the exact same
/// gate/reveal payloads (deterministic content across restarts). Observability never
/// receives this snapshot.
/// </summary>
public sealed record RevealFlowSnapshot(
    Guid Id,
    Guid WorkspaceId,
    Guid ConnectedAccountId,
    string ProviderCommentId,
    string? ParticipantIGSID,
    RevealFlowState State,
    RevealFlowContent Content,
    FollowGateMode FollowGateMode,
    string? OpeningPrivateReplyMessageId,
    string? OpeningPrivateReplyRecipientId,
    DateTimeOffset? LastUserMessageAtUtc,
    RevealGatePromptStatus GatePromptStatus,
    string? GatePromptProviderMessageId,
    DateTimeOffset? GatePromptAttemptedAtUtc,
    string? CorrelationTokenHash,
    RevealSendStatus RevealStatus,
    string? RevealProviderMessageId,
    DateTimeOffset? RevealAttemptedAtUtc,
    DateTimeOffset? RevealCompletedAtUtc,
    FollowState? LastFollowState,
    FollowStateUnavailableReason? LastFollowUnavailableReason,
    string? FailureCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Durable reveal-flow store. PostgreSQL enforces: one flow per logical origin
/// (ConnectedAccountId + ProviderCommentId), globally unique correlation-token hashes and
/// atomic compare-and-swap transitions so exactly one candidate ever obtains a provider
/// mutation authority. Transaction scope is single short statements — no DB lock is ever
/// held across a provider HTTP call (the coordinator sequence keeps each step short).
/// </summary>
public interface IRevealFlowStore
{
    /// <summary>
    /// Inserts the Starting row. On a unique (ConnectedAccountId, ProviderCommentId)
    /// conflict returns the existing flow instead (the winner owns the opening; zero new
    /// provider effects).
    /// </summary>
    Task<RevealFlowSnapshot> CreateOrGetAsync(
        Guid flowId,
        Guid workspaceId,
        Guid connectedAccountId,
        string providerCommentId,
        string? participantIGSId,
        bool isLiveComment,
        DateTimeOffset notificationOccurredAtUtc,
        FollowGateMode followGateMode,
        RevealFlowContent content,
        Guid? automationId,
        int? automationVersionNumber,
        string? triggerEventId,
        int? actionIndex,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<RevealFlowSnapshot?> GetByIdAsync(Guid flowId, CancellationToken cancellationToken = default);

    /// <summary>Exact reply_to.mid correlation: the user replied to this opening message.</summary>
    Task<RevealFlowSnapshot?> GetByOpeningMessageIdAsync(
        Guid connectedAccountId,
        string openingProviderMessageId,
        string participantIGSId,
        CancellationToken cancellationToken = default);

    /// <summary>Pending continuations for one participant (reply_to-absent fallback candidates).</summary>
    Task<IReadOnlyList<RevealFlowSnapshot>> GetAwaitingResponseAsync(
        Guid connectedAccountId,
        string participantIGSId,
        CancellationToken cancellationToken = default);

    Task<RevealFlowSnapshot?> GetByCorrelationTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Durable opening-attempt marker (Starting → OpeningAttempted): persisted before the
    /// M13-009 one-shot Private Reply is executed so a crash between the marker and the
    /// claim resume continues (same deterministic owner), never duplicates.
    /// </summary>
    Task<bool> TryMarkOpeningAttemptedAsync(Guid flowId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adopts the successful opening into the flow: records the provider identity and the
    /// participant IGSID (provider-confirmed recipient_id is the authoritative identity
    /// when the comment carried none) and enters AwaitingUserResponse.
    /// </summary>
    Task<bool> TryOpenAsync(
        Guid flowId,
        string openingProviderMessageId,
        string openingProviderRecipientId,
        string participantIGSId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Advances the consent/window anchor from AwaitingUserResponse (monotonic).</summary>
    Task<bool> TryAdvanceUserResponseAsync(
        Guid flowId,
        DateTimeOffset lastUserMessageAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically transitions AwaitingUserResponse → PreparingGatePrompt and persists the
    /// durable Attempting marker + correlation-token hash BEFORE any provider call.
    /// </summary>
    Task<bool> TryStartGatePromptAsync(
        Guid flowId,
        string correlationTokenHash,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<bool> RecordGatePromptSuccessAsync(
        Guid flowId,
        string providerMessageId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<bool> RecordGatePromptTerminalAsync(
        Guid flowId,
        string failureCode,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<bool> RecordGatePromptUncertainAsync(
        Guid flowId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomic single-reveal authority: only a flow currently awaiting postback (or
    /// rescuing an ambiguous gate-prompt attempt) may transition to Revealing with the
    /// durable Attempting marker. Exactly one candidate wins; every other observer makes
    /// zero provider calls.
    /// </summary>
    Task<bool> TryMarkRevealingAsync(
        Guid flowId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<bool> RecordRevealSuccessAsync(
        Guid flowId,
        string providerMessageId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<bool> RecordRevealTerminalAsync(
        Guid flowId,
        string failureCode,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<bool> RecordRevealUncertainAsync(
        Guid flowId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Records a follow-state check outcome without changing the flow state (gate blocked / unavailable).</summary>
    Task<bool> RecordFollowCheckAsync(
        Guid flowId,
        FollowState state,
        FollowStateUnavailableReason? unavailableReason,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Terminal transition for any deterministic rejection (policy, expiry, conflict).</summary>
    Task<bool> TryTerminalAsync(
        Guid flowId,
        RevealFlowState terminalState,
        string failureCode,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>Stable failure codes for reveal-flow outcomes (log-safe, provider-neutral).</summary>
public static class RevealFlowFailures
{
    public const string LocalValidation = "reveal.localValidation";

    public const string AccountUnavailable = "reveal.accountUnavailable";

    public const string OpeningPolicyRejected = "reveal.openingPolicyRejected";

    public const string OpeningClaimedElsewhere = "reveal.openingClaimedElsewhere";

    public const string OpeningUncertain = "reveal.openingUncertain";

    public const string OpeningTerminalFailed = "reveal.openingTerminalFailed";

    public const string ParticipantUnknown = "reveal.participantUnknown";

    public const string ParticipantAmbiguous = "reveal.participantAmbiguous";

    public const string GatePromptWindowExpired = "reveal.gatePrompt.windowExpired";

    public const string GatePromptRejected = "reveal.gatePrompt.rejected";

    public const string GatePromptUncertain = "reveal.gatePrompt.uncertain";

    public const string CorrelationInvalid = "reveal.correlation.invalid";

    public const string CorrelationBindingMismatch = "reveal.correlation.bindingMismatch";

    public const string WindowExpired = "reveal.windowExpired";

    public const string FollowGateBlocked = "reveal.followGate.blocked";

    public const string FollowGateUnavailable = "reveal.followGate.unavailable";

    public const string RevealRejected = "reveal.reveal.rejected";

    public const string RevealUncertain = "reveal.reveal.uncertain";
}

/// <summary>Outcome of one reveal-flow operation (log-safe; no content, no token).</summary>
public sealed record RevealFlowOutcome(string Code, bool ProviderMutationAttempted);
