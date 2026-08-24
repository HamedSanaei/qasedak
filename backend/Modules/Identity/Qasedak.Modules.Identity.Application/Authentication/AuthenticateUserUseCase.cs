using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Application.Auditing;
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
/// Token lifetime/clock concerns live inside the token issuer adapter. Login attempts are
/// audited when an audit trail is bound: failures record only an email fingerprint and a
/// reason code — never credentials.
/// </summary>
public sealed class AuthenticateUserUseCase(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    ISecurityTokenIssuer tokenIssuer,
    IAuditTrail? audit = null)
{
    private readonly Lazy<string> _dummyHash =
        new(() => passwordHasher.Hash("qasedak-timing-equalizer-dummy"), LazyThreadSafetyMode.ExecutionAndPublication);

    public async Task<AuthenticateUserResult> HandleAsync(
        AuthenticateUserCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!EmailAddress.TryCreate(command.Email, out var email))
        {
            await AuditFailureAsync(command.Email, AuthenticationFailures.InvalidCredentials, cancellationToken);
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
            await AuditFailureAsync(email.Value, AuthenticationFailures.InvalidCredentials, cancellationToken);
            return AuthenticateUserResult.Fail(AuthenticationFailures.InvalidCredentials);
        }

        var token = tokenIssuer.Issue(user.Id, user.Email);
        if (audit is not null)
        {
            await audit.RecordAsync(AuditEntry.New(
                "auth.login.succeeded",
                DateTimeOffset.UtcNow,
                actorUserId: user.Id,
                targetType: "user",
                targetId: user.Id.ToString()), cancellationToken);
        }

        return AuthenticateUserResult.Ok(user, token);
    }

    private async Task AuditFailureAsync(string attemptedEmail, string failureCode, CancellationToken cancellationToken)
    {
        if (audit is null)
        {
            return;
        }

        // Privacy: the email is fingerprinted, never stored verbatim on failures.
        await audit.RecordAsync(AuditEntry.New(
            "auth.login.failed",
            DateTimeOffset.UtcNow,
            detailsJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                emailFingerprint = AuditRedaction.Fingerprint(attemptedEmail),
                reason = failureCode,
            })), cancellationToken);
    }
}
