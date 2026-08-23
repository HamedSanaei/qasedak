using Qasedak.Modules.Identity.Domain.Users;

namespace Qasedak.Modules.Identity.Application.Authentication;

/// <summary>
/// Registers a user with a unique, canonicalized email and a policy-compliant password.
/// Only the password hash is persisted; plaintext material never leaves the hasher boundary.
/// </summary>
public sealed class RegisterUserUseCase(IUserRepository users, IPasswordHasher passwordHasher)
{
    public async Task<RegisterUserResult> HandleAsync(
        RegisterUserCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!EmailAddress.TryCreate(command.Email, out var email))
        {
            return RegisterUserResult.Fail(AuthenticationFailures.InvalidEmail);
        }

        var weakPassword = PasswordPolicy.Validate(command.Password);
        if (weakPassword is not null)
        {
            return RegisterUserResult.Fail(weakPassword);
        }

        if (string.IsNullOrWhiteSpace(command.DisplayName) || command.DisplayName.Trim().Length > 128)
        {
            return RegisterUserResult.Fail(AuthenticationFailures.InvalidDisplayName);
        }

        var existing = await users.FindByEmailAsync(email, cancellationToken);
        if (existing is not null)
        {
            return RegisterUserResult.Fail(AuthenticationFailures.EmailTaken);
        }

        var user = User.Create(email, command.DisplayName);
        var passwordHash = passwordHasher.Hash(command.Password);

        await users.AddAsync(user, passwordHash, cancellationToken);
        await users.SaveChangesAsync(cancellationToken);

        return RegisterUserResult.Ok(user.Id);
    }
}
