using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;

namespace Qasedak.Modules.Automations.Application;

/// <summary>
/// Channel-neutral description of an inbound triggering event (M13-012 §13).
/// <see cref="EventId"/> is the idempotency identity the run ledger keys on: it is the
/// provider SEMANTIC identity (comment id for comments, provider message id for inbound
/// DMs), never a transport/fragment id, so duplicates, re-enveloped deliveries and future
/// provider-history imports converge on one logical trigger. <see cref="ProviderEventIdentity"/>
/// carries the same semantic identity explicitly for dispatchers. Adapters must fail closed
/// (no trigger) when the semantic identity is unavailable.
/// </summary>
public sealed record TriggerContext(
    string EventId,
    TriggerKind Kind,
    string ProviderEventIdentity,
    string? SenderId,
    string? Text,
    DateTimeOffset OccurredAtUtc,
    string? MediaId = null,
    string? OriginalMediaId = null,
    bool IsLiveComment = false)
{
    /// <summary>Compatibility helper: the provider comment id when this is a comment event.</summary>
    public string? CommentId => Kind == TriggerKind.CommentCreated ? ProviderEventIdentity : null;

    /// <summary>Compatibility helper: the provider message id when this is a DM event.</summary>
    public string? ProviderMessageId => Kind == TriggerKind.InboundDirectMessage ? ProviderEventIdentity : null;
}

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
/// always produce the same verdict in the same action order — no clock, randomness,
/// network or storage.
///
/// Semantics:
/// - the trigger kind must equal the definition's kind;
/// - source scope: AnySource matches; SpecificSource requires the comment's media id OR
///   original media id to equal the configured provider media id (original-media awareness
///   for ad/boosted comments) — never both fields merged;
/// - text matching: EveryEvent matches; Keywords is ANY-of, case-insensitive, either
///   substring (legacy/v1 and default for keywords) or whole-word (opt-in, Unicode-aware);
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
            throw new AutomationsDomainException("trigger.eventIdRequired", "Trigger events require a provider semantic event id.");
        }

        if (context.Kind != definition.Trigger.Kind)
        {
            return RuleEvaluation.NoMatch("trigger.kindMismatch");
        }

        if (!SourceMatches(definition.Trigger, context))
        {
            return RuleEvaluation.NoMatch("trigger.sourceScope");
        }

        if (!TextMatches(definition.Trigger, context.Text ?? string.Empty))
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

    private static bool SourceMatches(AutomationTrigger trigger, TriggerContext context)
    {
        if (trigger.Source != SourceScope.SpecificSource)
        {
            return true;
        }

        if (context.Kind != TriggerKind.CommentCreated)
        {
            return false;
        }

        var configured = trigger.SourceMediaId;
        if (string.IsNullOrEmpty(configured))
        {
            return false;
        }

        return string.Equals(context.MediaId, configured, StringComparison.Ordinal)
            || (context.OriginalMediaId is not null && string.Equals(context.OriginalMediaId, configured, StringComparison.Ordinal));
    }

    private static bool TextMatches(AutomationTrigger trigger, string text)
    {
        if (trigger.TextMatch == TextMatchMode.EveryEvent)
        {
            return true;
        }

        return trigger.WholeWord
            ? trigger.KeywordFilters.Any(keyword => WholeWordMatcher.ContainsWholeWord(text, keyword))
            : trigger.KeywordFilters.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool Holds(AutomationCondition condition, TriggerContext context)
    {
        var actual = condition.Field switch
        {
            ConditionField.CommentText => context.Text ?? string.Empty,
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
