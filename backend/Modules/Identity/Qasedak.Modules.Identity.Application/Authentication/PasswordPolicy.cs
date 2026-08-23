namespace Qasedak.Modules.Identity.Application.Authentication;

/// <summary>Stable failure codes surfaced by authentication use cases.</summary>
public static class AuthenticationFailures
{
    public const string InvalidEmail = "auth.invalidEmail";

    public const string InvalidDisplayName = "auth.invalidDisplayName";

    public const string EmailTaken = "auth.emailTaken";

    public const string WeakPassword = "auth.weakPassword";

    /// <summary>Deliberately identical for unknown email and wrong password.</summary>
    public const string InvalidCredentials = "auth.invalidCredentials";
}

/// <summary>Password strength rules enforced before any persistence work.</summary>
public static class PasswordPolicy
{
    public const int MinLength = 10;

    public const int MaxLength = 128;

    /// <summary>Returns null when the password satisfies policy, else a stable failure code.</summary>
    public static string? Validate(string password)
    {
        if (string.IsNullOrWhiteSpace(password)
            || password.Length < MinLength
            || password.Length > MaxLength
            || password.All(char.IsLetterOrDigit))
        {
            return AuthenticationFailures.WeakPassword;
        }

        return null;
    }
}

/// <summary>Command to register a new user account.</summary>
public sealed record RegisterUserCommand(string Email, string DisplayName, string Password);

/// <summary>Outcome of user registration.</summary>
public readonly record struct RegisterUserResult(bool Success, Guid UserId, string? FailureCode)
{
    public static RegisterUserResult Ok(Guid userId) => new(true, userId, null);

    public static RegisterUserResult Fail(string failureCode) => new(false, Guid.Empty, failureCode);
}
