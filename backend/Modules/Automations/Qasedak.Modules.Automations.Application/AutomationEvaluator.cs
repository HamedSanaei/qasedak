using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;

namespace Qasedak.Modules.Automations.Application;

/// <summary>
/// Channel-neutral description of an inbound triggering event. <see cref="EventId"/> is
/// the producer's idempotent identity (the webhook inbox event id); adapters must supply
/// it verbatim so redeliveries collapse onto one execution record.
/// </summary>
public sealed record TriggerContext(
    string EventId,
    TriggerKind Kind,
    string CommentId,
    string? SenderId,
    string? CommentText,
    DateTimeOffset OccurredAtUtc);

/// <summary>Result of evaluating a definition against a trigger context.</summary>
public sealed record RuleEvaluation(
    bool Matched,
    string? NonMatchReason,
    IReadOnlyList<AutomationAction> OrderedActions)
{
    public static RuleEvaluation Match(IReadOnlyList<AutomationAction> actions) => new(true, null, actions);

    public static RuleEvaluation NoMatch(string reason) => new(false, reason, []);
}

/// <summary>
/// Deterministic rule evaluation: a pure function of (definition, context). Same inputs
/// always produce the same verdict in the same action order — no clock, randomness or I/O.
///
/// Semantics:
/// - the trigger kind must equal the definition's kind;
/// - keyword filters are ANY-of, matched case-insensitively as substrings of the comment
///   text (an empty filter list matches every event of the kind);
/// - every condition must hold (AND): Contains is a case-insensitive substring check,
///   Equals trims then compares ordinally;
/// - on a match, actions are returned exactly in declaration order.
/// </summary>
public static class AutomationEvaluator
{
    public static RuleEvaluation Evaluate(AutomationDefinition definition, TriggerContext context)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(context.EventId))
        {
            throw new AutomationsDomainException("trigger.eventIdRequired", "Trigger events require a producer event id.");
        }

        if (context.Kind != definition.Trigger.Kind)
        {
            return RuleEvaluation.NoMatch("trigger.kindMismatch");
        }

        var text = context.CommentText ?? string.Empty;

        if (definition.Trigger.KeywordFilters.Count > 0
            && !definition.Trigger.KeywordFilters.Any(keyword =>
                text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return RuleEvaluation.NoMatch("trigger.keywordFilter");
        }

        foreach (var condition in definition.Conditions)
        {
            if (!Holds(condition, context))
            {
                return RuleEvaluation.NoMatch($"condition.{condition.Field}.{condition.Operator}");
            }
        }

        return RuleEvaluation.Match(definition.Actions);
    }

    private static bool Holds(AutomationCondition condition, TriggerContext context)
    {
        var actual = condition.Field switch
        {
            ConditionField.CommentText => context.CommentText ?? string.Empty,
            ConditionField.SenderId => context.SenderId ?? string.Empty,
            _ => string.Empty,
        };

        return condition.Operator switch
        {
            ConditionOperator.Contains => actual.Contains(condition.ExpectedValue, StringComparison.OrdinalIgnoreCase),
            ConditionOperator.Equals => actual.Trim().Equals(condition.ExpectedValue.Trim(), StringComparison.Ordinal),
            _ => false,
        };
    }
}
