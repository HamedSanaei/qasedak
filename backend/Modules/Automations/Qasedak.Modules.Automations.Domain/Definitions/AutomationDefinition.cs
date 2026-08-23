namespace Qasedak.Modules.Automations.Domain.Definitions;

/// <summary>
/// What kind of inbound event can fire the automation. Expressed as a channel-neutral
/// concept — mapping normalized Instagram integration events onto these kinds happens in
/// a composition-root adapter, never inside this module.
/// </summary>
public enum TriggerKind
{
    /// <summary>A comment was created on media owned by the connected account.</summary>
    CommentCreated = 1,
}

/// <summary>
/// Immutable trigger description. Keyword filters are matched case-insensitively by the
/// evaluator; an empty keyword list matches every event of the kind.
/// </summary>
public sealed record AutomationTrigger(TriggerKind Kind, IReadOnlyList<string> KeywordFilters)
{
    public static AutomationTrigger CommentCreated(params string[] keywordFilters) =>
        new(TriggerKind.CommentCreated, keywordFilters);
}

public enum ConditionField
{
    /// <summary>The textual content of the triggering comment.</summary>
    CommentText = 1,

    /// <summary>The provider identity of the comment's author.</summary>
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

public enum ActionKind
{
    /// <summary>Send a direct message to the author of the triggering comment.</summary>
    SendDirectMessage = 1,
}

/// <summary>
/// Single outbound action. The text template is plain content (≤1000 chars); template
/// substitution is deterministic and evaluator-owned.
/// </summary>
public sealed record AutomationAction(ActionKind Kind, string MessageText)
{
    public const int MaxMessageLength = 1000;
}

/// <summary>
/// Full immutable automation definition: one trigger, ordered conditions (all must hold),
/// ordered actions (executed in listed order). Ordering is part of the semantics, so the
/// lists are captured in construction order and never reordered.
/// </summary>
public sealed record AutomationDefinition(
    AutomationTrigger Trigger,
    IReadOnlyList<AutomationCondition> Conditions,
    IReadOnlyList<AutomationAction> Actions)
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

        foreach (var action in actionList)
        {
            ValidateAction(action);
        }

        return new AutomationDefinition(trigger, conditionList, actionList);
    }

    private static void ValidateAction(AutomationAction action)
    {
        if (string.IsNullOrWhiteSpace(action.MessageText))
        {
            throw new AutomationsDomainException("automation.actionTextRequired", "Action message text is required.");
        }

        if (action.MessageText.Length > AutomationAction.MaxMessageLength)
        {
            throw new AutomationsDomainException("automation.actionTextTooLong", $"Action message text exceeds {AutomationAction.MaxMessageLength} characters.");
        }
    }
}
