namespace Qasedak.Modules.Identity.Domain.Workspaces;

/// <summary>Human-readable workspace name with normalization and bounded length.</summary>
public readonly record struct WorkspaceName
{
    public const int MinLength = 3;

    public const int MaxLength = 64;

    private WorkspaceName(string value)
    {
        Value = value;
    }

    /// <summary>The canonical (whitespace-trimmed) name.</summary>
    public string Value { get; }

    public override string ToString() => Value;

    public static bool TryCreate(string? input, out WorkspaceName workspaceName)
    {
        workspaceName = default;

        var candidate = input?.Trim();

        if (candidate is null || candidate.Length < MinLength || candidate.Length > MaxLength)
        {
            return false;
        }

        workspaceName = new WorkspaceName(candidate);
        return true;
    }

    public static WorkspaceName Create(string input) =>
        TryCreate(input, out var workspaceName)
            ? workspaceName
            : throw new DomainRuleViolationException(
                "workspace.name.invalid",
                $"Workspace name must be between {MinLength} and {MaxLength} characters.");
}
