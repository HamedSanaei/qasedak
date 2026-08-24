namespace Qasedak.Modules.Billing.Domain;

/// <summary>Rule-coded billing domain exception (stable codes for API mapping).</summary>
public sealed class BillingDomainException(string ruleCode, string message) : Exception(message)
{
    public string RuleCode { get; } = ruleCode;
}
