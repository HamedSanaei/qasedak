namespace Qasedak.Modules.Identity.Domain;

/// <summary>
/// Thrown when a domain invariant would be violated. The rule code is stable and
/// machine-readable so application layers can map it to API problem details without
/// parsing exception text.
/// </summary>
public sealed class DomainRuleViolationException(string ruleCode, string message)
    : Exception(message)
{
    /// <summary>Stable identifier of the violated rule, e.g. "workspace.lastOwnerProtected".</summary>
    public string RuleCode { get; } = ruleCode;
}
