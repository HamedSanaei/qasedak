namespace Qasedak.Modules.Identity.Domain.Users;

/// <summary>
/// Normalized, validated email address. Comparison is ordinal against the
/// lowercase, whitespace-trimmed canonical form.
/// </summary>
public readonly record struct EmailAddress
{
    public const int MaxLength = 320;

    private EmailAddress(string value)
    {
        Value = value;
    }

    /// <summary>The canonical (trimmed, lowercase) email address.</summary>
    public string Value { get; }

    public override string ToString() => Value;

    public static bool TryCreate(string? input, out EmailAddress emailAddress)
    {
        emailAddress = default;

        var candidate = input?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(candidate)
            || candidate.Length > MaxLength
            || !IsValidShape(candidate))
        {
            return false;
        }

        emailAddress = new EmailAddress(candidate);
        return true;
    }

    public static EmailAddress Create(string input) =>
        TryCreate(input, out var emailAddress)
            ? emailAddress
            : throw new DomainRuleViolationException(
                "user.email.invalid",
                "Email address is missing, malformed, or exceeds the maximum length.");

    /// <summary>
    /// Deliberately conservative structural check: exactly one '@' separating a non-empty local
    /// part from a domain with at least one dot-separated label and a letters TLD of 2+ chars.
    /// Full deliverability is a verification concern, not a construction concern.
    /// </summary>
    private static bool IsValidShape(string candidate)
    {
        var at = candidate.IndexOf('@');
        if (at <= 0 || at == candidate.Length - 1 || candidate.IndexOf('@', at + 1) >= 0)
        {
            return false;
        }

        var domain = candidate[(at + 1)..];
        var lastDot = domain.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == domain.Length - 1)
        {
            return false;
        }

        foreach (var label in domain.Split('.'))
        {
            if (label.Length == 0 || label.StartsWith('-') || label.EndsWith('-'))
            {
                return false;
            }
        }

        var tld = domain[(lastDot + 1)..];
        return tld.Length >= 2 && tld.All(char.IsLetter);
    }
}
