using Microsoft.Extensions.Options;
using Qasedak.Modules.Identity.Application.Authentication;
using Qasedak.Modules.Identity.Infrastructure.Authentication;
using Qasedak.Modules.Identity.UnitTests.TestSupport;
using Xunit;

namespace Qasedak.Modules.Identity.UnitTests;

public sealed class AuthenticationUseCaseTests
{
    private const string SigningKey = "use-case-test-signing-key-0123456789abcdef";

    private static (RegisterUserUseCase Register, AuthenticateUserUseCase Authenticate, InMemoryUserRepository Repo) NewSut(
        IPasswordHasher? hasher = null)
    {
        var repo = new InMemoryUserRepository();
        hasher ??= new Pbkdf2PasswordHasher(Options.Create(new IdentityAuthOptions()));
        var issuer = new HmacSecurityTokenIssuer(
            new FixedOptionsMonitor<IdentityAuthOptions>(new IdentityAuthOptions { TokenSigningKey = SigningKey }),
            FixedClock.Default);
        return (new RegisterUserUseCase(repo, hasher), new AuthenticateUserUseCase(repo, hasher, issuer), repo);
    }

    [Fact]
    public async Task RegistrationPersistsCanonicalEmailAndVerifiableCredential()
    {
        var (register, _, repo) = NewSut();

        var result = await register.HandleAsync(new RegisterUserCommand("  Ada@Example.COM ", "Ada", "s3cret-Passphrase!"));

        Assert.True(result.Success);
        var stored = await repo.FindByEmailAsync(Domain.Users.EmailAddress.Create("ada@example.com"));
        Assert.NotNull(stored);
        Assert.Equal("Ada", stored!.DisplayName);
        var hash = await repo.GetPasswordHashAsync(stored.Id);
        Assert.NotNull(hash);
    }

    [Fact]
    public async Task DuplicateEmailRegistrationIsRejectedCaseInsensitively()
    {
        var (register, _, _) = NewSut();
        await register.HandleAsync(new RegisterUserCommand("ada@example.com", "Ada", "s3cret-Passphrase!"));

        var second = await register.HandleAsync(new RegisterUserCommand("ADA@EXAMPLE.COM", "Impostor", "an0ther-Passphrase!"));

        Assert.False(second.Success);
        Assert.Equal(AuthenticationFailures.EmailTaken, second.FailureCode);
    }

    [Theory]
    [InlineData("short1!a")]
    [InlineData("12345678901")]
    [InlineData("abcdefghijk")]
    [InlineData("")]
    [InlineData("          ")]
    public async Task WeakPasswordsAreRejected(string password)
    {
        var (register, _, _) = NewSut();

        var result = await register.HandleAsync(new RegisterUserCommand("ada@example.com", "Ada", password));

        Assert.False(result.Success);
        Assert.Equal(AuthenticationFailures.WeakPassword, result.FailureCode);
    }

    [Fact]
    public async Task OverlongPasswordIsRejected()
    {
        var (register, _, _) = NewSut();
        var password = new string('x', 130) + "!";

        var result = await register.HandleAsync(new RegisterUserCommand("ada@example.com", "Ada", password));

        Assert.False(result.Success);
        Assert.Equal(AuthenticationFailures.WeakPassword, result.FailureCode);
    }

    [Fact]
    public async Task MalformedEmailRegistrationIsRejected()
    {
        var (register, _, _) = NewSut();

        var result = await register.HandleAsync(new RegisterUserCommand("not-an-email", "Ada", "s3cret-Passphrase!"));

        Assert.False(result.Success);
        Assert.Equal(AuthenticationFailures.InvalidEmail, result.FailureCode);
    }

    [Fact]
    public async Task BlankDisplayNameRegistrationIsRejected()
    {
        var (register, _, _) = NewSut();

        var result = await register.HandleAsync(new RegisterUserCommand("ada@example.com", "   ", "s3cret-Passphrase!"));

        Assert.False(result.Success);
        Assert.Equal(AuthenticationFailures.InvalidDisplayName, result.FailureCode);
    }

    [Fact]
    public async Task SuccessfulAuthenticationIssuesValidToken()
    {
        var (register, authenticate, _) = NewSut();
        await register.HandleAsync(new RegisterUserCommand("ada@example.com", "Ada", "s3cret-Passphrase!"));

        var result = await authenticate.HandleAsync(new AuthenticateUserCommand("ADA@Example.com", "s3cret-Passphrase!"));

        Assert.True(result.Success);
        Assert.NotEqual(Guid.Empty, result.UserId);
        Assert.Equal("ada@example.com", result.Email);
        Assert.False(default == result.Token);
    }

    [Fact]
    public async Task WrongPasswordFailsWithInvalidCredentials()
    {
        var (register, authenticate, _) = NewSut();
        await register.HandleAsync(new RegisterUserCommand("ada@example.com", "Ada", "s3cret-Passphrase!"));

        var result = await authenticate.HandleAsync(new AuthenticateUserCommand("ada@example.com", "wrong-password-1!"));

        Assert.False(result.Success);
        Assert.Equal(AuthenticationFailures.InvalidCredentials, result.FailureCode);
    }

    [Fact]
    public async Task UnknownEmailFailsWithIdenticalInvalidCredentials()
    {
        var (register, authenticate, _) = NewSut();
        await register.HandleAsync(new RegisterUserCommand("ada@example.com", "Ada", "s3cret-Passphrase!"));

        var unknown = await authenticate.HandleAsync(new AuthenticateUserCommand("nobody@example.com", "whatever-pass-1!"));
        var wrongPassword = await authenticate.HandleAsync(new AuthenticateUserCommand("ada@example.com", "wrong-password-1!"));

        Assert.False(unknown.Success);
        Assert.Equal(unknown.FailureCode, wrongPassword.FailureCode);
    }

    [Fact]
    public async Task MalformedEmailLoginFailsWithoutEnumeration()
    {
        var (_, authenticate, _) = NewSut();

        var result = await authenticate.HandleAsync(new AuthenticateUserCommand("@nope", "whatever-pass-1!"));

        Assert.False(result.Success);
        Assert.Equal(AuthenticationFailures.InvalidCredentials, result.FailureCode);
    }
}
