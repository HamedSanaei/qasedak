using Qasedak.BuildingBlocks.Domain;

namespace Qasedak.Modules.Identity.Domain.Users;

/// <summary>A person that authenticates into Qasedak and holds workspace memberships.</summary>
public sealed class User : Entity<Guid>
{
    private User(Guid id, EmailAddress email, string displayName)
        : base(id)
    {
        Email = email;
        DisplayName = displayName;
    }

    public EmailAddress Email { get; }

    public string DisplayName { get; }

    public static User Create(EmailAddress email, string displayName) =>
        new(Guid.CreateVersion7(), email, NormalizeDisplayName(displayName));

    /// <summary>Rehydrates an existing user from persistence.</summary>
    public static User FromState(Guid id, EmailAddress email, string displayName) =>
        new(id, email, NormalizeDisplayName(displayName));

    private static string NormalizeDisplayName(string displayName)
    {
        var trimmed = displayName.Trim();

        return trimmed.Length switch
        {
            < 1 => throw new DomainRuleViolationException(
                "user.displayName.required", "Display name is required."),
            > 128 => throw new DomainRuleViolationException(
                "user.displayName.tooLong", "Display name must be at most 128 characters."),
            _ => trimmed,
        };
    }
}
