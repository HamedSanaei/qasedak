namespace Qasedak.Modules.Instagram.Domain;

/// <summary>
/// Thrown when code attempts to push a connected-account aggregate into a state that
/// violates a documented lifecycle rule. Carries a stable machine-readable rule code.
/// </summary>
public sealed class InstagramDomainException(string ruleCode, string message)
    : Exception(message)
{
    /// <summary>Stable identifier of the violated rule (e.g. "account.disconnected").</summary>
    public string RuleCode { get; } = ruleCode;
}
