namespace Qasedak.Modules.Conversations.Domain.Conversations;

/// <summary>Rule-code-carrying violation inside the Conversations module.</summary>
public sealed class ConversationsDomainException(string ruleCode, string message)
    : Exception($"{ruleCode}: {message}")
{
    public string RuleCode { get; } = ruleCode;
}
