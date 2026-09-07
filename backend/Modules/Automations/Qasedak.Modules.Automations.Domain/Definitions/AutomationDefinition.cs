namespace Qasedak.Modules.Automations.Domain.Definitions;

/// <summary>
/// What kind of inbound event can fire the automation. Expressed as a channel-neutral
/// concept — mapping normalized Instagram integration events onto these kinds happens in
/// a composition-root adapter, never inside this module.
///
/// Numeric values are historical persisted contract (M13-012 §8): never renumber, only
/// append.
/// </summary>
public enum TriggerKind
{
    /// <summary>A comment was created on media owned by the connected account.</summary>
    CommentCreated = 1,

    /// <summary>An inbound direct message from a participant to the connected account.</summary>
    InboundDirectMessage = 2,
}

/// <summary>
/// How the trigger's text filter is interpreted. The legacy (v1) convention — an empty
/// keyword list matches every event, a non-empty list is ANY-of case-insensitive
/// substring — is made explicit here instead of hiding in an empty-array convention.
/// </summary>
public enum TextMatchMode
{
    /// <summary>Every event of the trigger kind matches; keywords are ignored.</summary>
    EveryEvent = 1,

    /// <summary>ANY-of keyword matching over the event text (case-insensitive).</summary>
    Keywords = 2,
}

/// <summary>
/// Which source (media) a comment trigger applies to. Direct-message triggers have no
/// media scope (NotApplicable is expressed as AnySource + no media id and enforced at
/// authoring time).
/// </summary>
public enum SourceScope
{
    /// <summary>Any eligible source under the exact bound connected account.</summary>
    AnySource = 1,

    /// <summary>Only comments on the configured provider media identity.</summary>
    SpecificSource = 2,
}

/// <summary>
/// Immutable trigger description. Keyword filters are matched by the evaluator according
/// to <see cref="TextMatch"/>; an EveryEvent trigger matches every event of the kind.
/// <see cref="WholeWord"/> (only valid with <see cref="TextMatchMode.Keywords"/>) uses the
/// deterministic Unicode-aware boundary algorithm in <see cref="WholeWordMatcher"/>.
/// </summary>
public sealed record AutomationTrigger(
    TriggerKind Kind,
    IReadOnlyList<string> KeywordFilters,
    TextMatchMode TextMatch = TextMatchMode.EveryEvent,
    bool WholeWord = false,
    SourceScope Source = SourceScope.AnySource,
    string? SourceMediaId = null)
{
    /// <summary>Comment trigger preserving the legacy convention: keywords present ⇒ ANY-of substring matching.</summary>
    public static AutomationTrigger CommentCreated(params string[] keywordFilters) =>
        new(TriggerKind.CommentCreated, keywordFilters, keywordFilters.Length > 0 ? TextMatchMode.Keywords : TextMatchMode.EveryEvent);

    public static AutomationTrigger InboundDirectMessage(params string[] keywordFilters) =>
        new(TriggerKind.InboundDirectMessage, keywordFilters, keywordFilters.Length > 0 ? TextMatchMode.Keywords : TextMatchMode.EveryEvent);
}

public enum ConditionField
{
    /// <summary>The textual content of the triggering event (comment text or DM text).</summary>
    CommentText = 1,

    /// <summary>The provider identity of the event's author.</summary>
    SenderId = 2,
}

public enum ConditionOperator
{
    /// <summary>Case-insensitive substring match.</summary>
    Contains = 1,

    /// <summary>Exact equality after trimming.</summary>
    Equals = 2,
}

/// <summary>Single predicate row: field/operator/expected value.</summary>
public sealed record AutomationCondition(ConditionField Field, ConditionOperator Operator, string ExpectedValue)
{
    public static AutomationCondition TextContains(string fragment) => new(ConditionField.CommentText, ConditionOperator.Contains, fragment);

    public static AutomationCondition TextEquals(string value) => new(ConditionField.CommentText, ConditionOperator.Equals, value);
}

/// <summary>
/// Outbound action kinds. Numeric values are historical persisted contract (M13-012 §8):
/// never renumber, only append.
///
/// <see cref="SendDirectMessage"/> (value 1) is the LEGACY origin-aware action: fired by a
/// comment trigger it routes through the one-shot Private Reply operation (M13-009
/// correction), fired by a DM trigger it routes to a normal Direct Message. Newer
/// definitions should use the explicit kinds below.
/// </summary>
public enum ActionKind
{
    /// <summary>Legacy origin-aware action (comment ⇒ Private Reply, DM ⇒ Direct Message).</summary>
    SendDirectMessage = 1,

    /// <summary>Explicit one-shot Private Reply (comment triggers only).</summary>
    SendPrivateReply = 2,

    /// <summary>Explicit normal Direct Message (DM triggers only).</summary>
    DirectMessage = 3,

    /// <summary>Start the M13-011 interactive reveal/opening flow (comment triggers only).</summary>
    StartRevealFlow = 4,

    /// <summary>Explicit one-shot public comment reply (comment triggers only).</summary>
    SendPublicReply = 5,

    /// <summary>Durable delayed Direct Message follow-up via scheduled work.</summary>
    ScheduleFollowUp = 6,
}

/// <summary>
/// Channel-neutral follow-gate mode carried by a reveal action. The composition root maps
/// it onto the Instagram-owned capability (M13-011). Values mirror that contract so the
/// mapping is identity.
/// </summary>
public enum RevealFollowGateMode
{
    /// <summary>No follow check; the provider-independent postback/reveal core always works.</summary>
    Disabled = 0,

    /// <summary>Check follow state only when a proven consent basis exists; unknown never reveals.</summary>
    EnabledWhenSupported = 1,
}

/// <summary>
/// Stable extra payload for a reveal action (M13-011 content mapping). All texts are
/// bounded at authoring time; token material never belongs here.
/// </summary>
public sealed record RevealActionContent(
    string GatePromptText,
    string PostbackButtonTitle,
    string RevealText,
    string? FollowUrl = null,
    string? FollowButtonTitle = null,
    RevealFollowGateMode FollowGateMode = RevealFollowGateMode.Disabled);

/// <summary>
/// Optional per-action payload, interpreted only by the kinds that own it
/// (ScheduleFollowUp ⇒ <see cref="Delay"/>; StartRevealFlow ⇒ <see cref="Reveal"/>).
/// </summary>
public sealed record ActionExtras(
    TimeSpan? Delay = null,
    RevealActionContent? Reveal = null);

/// <summary>
/// Single outbound action. The text template is plain content (≤1000 chars); template
/// substitution is deterministic and evaluator-owned. For StartRevealFlow the text is the
/// opening Private Reply content; the remaining reveal content lives in <see cref="Extras"/>.
/// </summary>
public sealed record AutomationAction(ActionKind Kind, string MessageText, ActionExtras? Extras = null)
{
    public const int MaxMessageLength = 1000;

    public const int MaxButtonTitleLength = 40;

    public const int MaxFollowUrlLength = 2048;

    /// <summary>Product-owned scheduling bounds — not an inferred Meta limit.</summary>
    public static readonly TimeSpan FollowUpDelayMin = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan FollowUpDelayMax = TimeSpan.FromDays(7);

    public const int MaxSourceMediaIdLength = 64;
}

/// <summary>
/// Full immutable automation definition: one trigger, ordered conditions (all must hold),
/// ordered actions (executed in listed order). Ordering is part of the semantics, so the
/// lists are captured in construction order and never reordered.
///
/// <see cref="SchemaVersion"/> is persistence metadata: legacy rows deserialize as version
/// 1 with legacy trigger normalization; new definitions are written as version 2.
/// </summary>
public sealed record AutomationDefinition(
    AutomationTrigger Trigger,
    IReadOnlyList<AutomationCondition> Conditions,
    IReadOnlyList<AutomationAction> Actions,
    int SchemaVersion = 2)
{
    public static readonly int MaxConditions = 10;

    public static readonly int MaxActions = 5;

    public static readonly int MaxKeywordFilters = 20;

    public static AutomationDefinition Create(AutomationTrigger trigger, IEnumerable<AutomationAction> actions)
        => Create(trigger, [], actions);

    public static AutomationDefinition Create(AutomationTrigger trigger, IEnumerable<AutomationCondition> conditions, IEnumerable<AutomationAction> actions)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        var conditionList = conditions.ToArray();
        var actionList = actions.ToArray();

        if (conditionList.Length > MaxConditions)
        {
            throw new AutomationsDomainException("automation.tooManyConditions", $"An automation supports at most {MaxConditions} conditions.");
        }

        if (actionList.Length == 0)
        {
            throw new AutomationsDomainException("automation.actionRequired", "An automation requires at least one action.");
        }

        if (actionList.Length > MaxActions)
        {
            throw new AutomationsDomainException("automation.tooManyActions", $"An automation supports at most {MaxActions} actions.");
        }

        ValidateTrigger(trigger);

        foreach (var action in actionList)
        {
            ValidateAction(trigger.Kind, action);
        }

        ValidatePrivateReplyConflict(trigger.Kind, actionList);

        return new AutomationDefinition(trigger, conditionList, actionList);
    }

    private static void ValidateTrigger(AutomationTrigger trigger)
    {
        if (trigger.KeywordFilters.Count > MaxKeywordFilters)
        {
            throw new AutomationsDomainException("automation.tooManyKeywordFilters", $"An automation supports at most {MaxKeywordFilters} keyword filters.");
        }

        if (trigger.TextMatch == TextMatchMode.Keywords && trigger.KeywordFilters.Count == 0)
        {
            throw new AutomationsDomainException("automation.keywordRequired", "Keywords matching requires at least one keyword.");
        }

        if (trigger.WholeWord && trigger.TextMatch != TextMatchMode.Keywords)
        {
            throw new AutomationsDomainException("automation.wholeWordRequiresKeywords", "Whole-word matching requires keyword matching mode.");
        }

        if (trigger.Source == SourceScope.SpecificSource)
        {
            if (string.IsNullOrWhiteSpace(trigger.SourceMediaId))
            {
                throw new AutomationsDomainException("automation.sourceMediaIdRequired", "Specific source scope requires a provider media id.");
            }

            if (trigger.SourceMediaId.Length > AutomationAction.MaxSourceMediaIdLength)
            {
                throw new AutomationsDomainException("automation.sourceMediaIdTooLong", $"Provider media id exceeds {AutomationAction.MaxSourceMediaIdLength} characters.");
            }
        }
        else if (trigger.SourceMediaId is not null)
        {
            throw new AutomationsDomainException("automation.sourceMediaIdWithoutSpecificScope", "A provider media id requires specific source scope.");
        }

        if (trigger.Kind == TriggerKind.InboundDirectMessage
            && (trigger.Source != SourceScope.AnySource || trigger.SourceMediaId is not null))
        {
            // Direct-message triggers have no media scope — never a fake media id.
            throw new AutomationsDomainException("automation.dmSourceScopeNotApplicable", "Direct-message triggers have no media source scope.");
        }
    }

    private static void ValidateAction(TriggerKind triggerKind, AutomationAction action)
    {
        if (string.IsNullOrWhiteSpace(action.MessageText))
        {
            throw new AutomationsDomainException("automation.actionTextRequired", "Action message text is required.");
        }

        if (action.MessageText.Length > AutomationAction.MaxMessageLength)
        {
            throw new AutomationsDomainException("automation.actionTextTooLong", $"Action message text exceeds {AutomationAction.MaxMessageLength} characters.");
        }

        IReadOnlyList<ActionKind> allowed = triggerKind switch
        {
            TriggerKind.CommentCreated =>
            [
                ActionKind.SendDirectMessage,
                ActionKind.SendPrivateReply,
                ActionKind.StartRevealFlow,
                ActionKind.SendPublicReply,
                ActionKind.ScheduleFollowUp,
            ],
            TriggerKind.InboundDirectMessage =>
            [
                ActionKind.SendDirectMessage,
                ActionKind.DirectMessage,
                ActionKind.ScheduleFollowUp,
            ],
            _ => Array.Empty<ActionKind>(),
        };

        if (!allowed.Contains(action.Kind))
        {
            throw new AutomationsDomainException(
                "automation.actionNotAllowedForTrigger",
                $"Action {action.Kind} is not supported for trigger {triggerKind}.");
        }

        switch (action.Kind)
        {
            case ActionKind.ScheduleFollowUp:
                {
                    var delay = action.Extras?.Delay;
                    if (delay is null || delay.Value < AutomationAction.FollowUpDelayMin || delay.Value > AutomationAction.FollowUpDelayMax)
                    {
                        throw new AutomationsDomainException(
                            "automation.followUpDelayInvalid",
                            $"Follow-up delay must be between {AutomationAction.FollowUpDelayMin} and {AutomationAction.FollowUpDelayMax}.");
                    }

                    if (action.Extras?.Reveal is not null)
                    {
                        throw new AutomationsDomainException("automation.actionExtrasNotApplicable", "Reveal content is not applicable to a follow-up action.");
                    }

                    break;
                }

            case ActionKind.StartRevealFlow:
                {
                    var reveal = action.Extras?.Reveal;
                    if (reveal is null)
                    {
                        throw new AutomationsDomainException("automation.revealContentRequired", "A reveal action requires reveal content.");
                    }

                    if (action.Extras?.Delay is not null)
                    {
                        throw new AutomationsDomainException("automation.actionExtrasNotApplicable", "A delay is not applicable to a reveal action.");
                    }

                    ValidateRevealText(nameof(reveal.GatePromptText), reveal.GatePromptText);
                    ValidateRevealText(nameof(reveal.RevealText), reveal.RevealText);
                    ValidateButtonTitle(nameof(reveal.PostbackButtonTitle), reveal.PostbackButtonTitle);
                    ValidateButtonTitle(nameof(reveal.FollowButtonTitle), reveal.FollowButtonTitle);
                    ValidateFollowUrl(reveal.FollowUrl);
                    break;
                }

            default:
                if (action.Extras is not null)
                {
                    throw new AutomationsDomainException("automation.actionExtrasNotApplicable", $"Action {action.Kind} does not accept extra payload.");
                }

                break;
        }
    }

    private static void ValidateRevealText(string field, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new AutomationsDomainException("automation.revealTextRequired", $"Reveal content field {field} is required.");
        }

        if (value.Length > AutomationAction.MaxMessageLength)
        {
            throw new AutomationsDomainException("automation.revealTextTooLong", $"Reveal content field {field} exceeds {AutomationAction.MaxMessageLength} characters.");
        }
    }

    private static void ValidateButtonTitle(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new AutomationsDomainException("automation.revealButtonTitleRequired", $"Reveal content field {field} is required.");
        }

        if (value.Length > AutomationAction.MaxButtonTitleLength)
        {
            throw new AutomationsDomainException("automation.revealButtonTitleTooLong", $"Reveal content field {field} exceeds {AutomationAction.MaxButtonTitleLength} characters.");
        }
    }

    private static void ValidateFollowUrl(string? value)
    {
        if (value is null)
        {
            return;
        }

        if (value.Length > AutomationAction.MaxFollowUrlLength)
        {
            throw new AutomationsDomainException("automation.revealFollowUrlTooLong", $"Follow url exceeds {AutomationAction.MaxFollowUrlLength} characters.");
        }

        if (!(value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
        {
            throw new AutomationsDomainException("automation.revealFollowUrlScheme", "Follow url must be an absolute http(s) url.");
        }
    }

    /// <summary>
    /// The opening Private Reply allowance is one per comment: the legacy
    /// <see cref="ActionKind.SendDirectMessage"/> on a comment trigger, an explicit
    /// <see cref="ActionKind.SendPrivateReply"/> and a <see cref="ActionKind.StartRevealFlow"/>
    /// opening all consume it, so a definition may contain at most one of them against the
    /// same comment trigger. Across different automations the M13-009 global effect ledger
    /// remains the concurrency backstop.
    /// </summary>
    private static void ValidatePrivateReplyConflict(TriggerKind triggerKind, IReadOnlyList<AutomationAction> actions)
    {
        if (triggerKind != TriggerKind.CommentCreated)
        {
            return;
        }

        var privateReplyConsuming = actions.Count(a => a.Kind is ActionKind.SendDirectMessage or ActionKind.SendPrivateReply or ActionKind.StartRevealFlow);
        if (privateReplyConsuming > 1)
        {
            throw new AutomationsDomainException(
                "automation.conflictingPrivateReplyEffects",
                "A comment-triggered definition may contain at most one Private Reply consuming action (legacy SendDirectMessage, SendPrivateReply or StartRevealFlow).");
        }
    }
}
