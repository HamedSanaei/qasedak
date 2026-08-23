namespace Qasedak.Modules.Automations.Domain;

/// <summary>
/// Module-local domain exception with a stable rule code, mirroring the other modules'
/// convention. Codes are part of the module contract; never change them retroactively.
/// </summary>
public sealed class AutomationsDomainException(string ruleCode, string message) : Exception(message)
{
    /// <summary>Stable machine-readable failure code (e.g. "automation.notDraft").</summary>
    public string RuleCode { get; } = ruleCode;
}
