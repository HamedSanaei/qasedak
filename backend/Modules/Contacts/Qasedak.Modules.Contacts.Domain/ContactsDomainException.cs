namespace Qasedak.Modules.Contacts.Domain;

/// <summary>Rule-code-carrying violation inside the Contacts module.</summary>
public sealed class ContactsDomainException(string ruleCode, string message)
    : Exception($"{ruleCode}: {message}")
{
    public string RuleCode { get; } = ruleCode;
}
