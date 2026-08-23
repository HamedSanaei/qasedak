using Qasedak.BuildingBlocks.Application;
using Qasedak.Modules.Identity.Domain.Users;

namespace Qasedak.Modules.Identity.Application.Authentication;

/// <summary>Command to authenticate with email and password.</summary>
public sealed record AuthenticateUserCommand(string Email, string Password);

/// <summary>Outcome of authentication.</summary>
public readonly record struct AuthenticateUserResult(
    bool Success,
    Guid UserId,
    string Email,
    string DisplayName,
    SecurityToken Token,
    string? FailureCode)
{
    public static AuthenticateUserResult Ok(User user, SecurityToken token) =>
        new(true, user.Id, user.Email.Value, user.DisplayName, token, null);

    public static AuthenticateUserResult Fail(string failureCode) =>
        new(false, Guid.Empty, string.Empty, string.Empty, default, failureCode);
}

/// <summary>
/// Verifies credentials and issues a signed security token. Unknown emails and wrong
/// passwords produce the identical failure code; unknown emails still perform one hash
/// verification against a precomputed dummy hash to blunt user-enumeration timing signals.
/// Token lifetime/clock concerns live inside the token issuer adapter.
/// </summary>
public sealed class AuthenticateUserUseCase(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    ISecurityTokenIssuer tokenIssuer)
{
    private readonly Lazy<string> _dummyHash =
        new(() => passwordHasher.Hash("qasedak-timing-equalizer-dummy"), LazyThreadSafetyMode.ExecutionAndPublication);

    public async Task<AuthenticateUserResult> HandleAsync(
        AuthenticateUserCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!EmailAddress.TryCreate(command.Email, out var email))
        {
            return AuthenticateUserResult.Fail(AuthenticationFailures.InvalidCredentials);
        }

        var user = await users.FindByEmailAsync(email, cancellationToken);
        var storedHash = user is null
            ? null
            : await users.GetPasswordHashAsync(user.Id, cancellationToken);

        var verified = storedHash is not null && passwordHasher.Verify(command.Password, storedHash);
        if (user is null)
        {
            // Burn comparable hashing time when the account does not exist.
            _ = passwordHasher.Verify(command.Password, _dummyHash.Value);
        }

        if (user is null || !verified)
        {
            return AuthenticateUserResult.Fail(AuthenticationFailures.InvalidCredentials);
        }

        var token = tokenIssuer.Issue(user.Id, user.Email);
        return AuthenticateUserResult.Ok(user, token);
    }
}
