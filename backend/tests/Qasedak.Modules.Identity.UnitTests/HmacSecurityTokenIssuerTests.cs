using System.Text;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Identity.Domain.Users;
using Qasedak.Modules.Identity.Infrastructure.Authentication;
using Qasedak.Modules.Identity.UnitTests.TestSupport;
using Xunit;

namespace Qasedak.Modules.Identity.UnitTests;

public sealed class HmacSecurityTokenIssuerTests
{
    private const string SigningKey = "unit-test-signing-key-0123456789abcdef-XYZ";

    private static HmacSecurityTokenIssuer NewIssuer(int lifetimeHours = 12, string? key = null) =>
        new(
            new FixedOptionsMonitor<IdentityAuthOptions>(new IdentityAuthOptions
            {
                TokenSigningKey = key ?? SigningKey,
                TokenLifetimeHours = lifetimeHours,
            }),
            FixedClock.Default);

    [Fact]
    public void IssuedTokenValidatesBackToTheBoundIdentity()
    {
        var issuer = NewIssuer();
        var userId = Guid.CreateVersion7();
        var email = EmailAddress.Create("ada@example.com");

        var token = issuer.Issue(userId, email);
        var result = issuer.Validate(token.Value);

        Assert.True(result.IsValid);
        Assert.Equal(userId, result.UserId);
        Assert.Equal("ada@example.com", result.Email);
        Assert.True(token.ExpiresAtUtc > FixedClock.Default.UtcNow);
    }

    [Fact]
    public void TamperedPayloadIsRejected()
    {
        var issuer = NewIssuer();
        var token = issuer.Issue(Guid.CreateVersion7(), EmailAddress.Create("eve@example.com"));
        var parts = token.Value.Split('.');

        var payload = Base64UrlDecode(parts[0]);
        payload[^1] ^= 0x01;
        var forged = Base64UrlEncode(payload) + "." + parts[1];

        Assert.False(issuer.Validate(forged).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData(".")]
    [InlineData("onlypayload.")]
    [InlineData(".onlysignature")]
    public void GarbageTokensAreRejectedWithoutThrowing(string token)
    {
        Assert.False(NewIssuer().Validate(token).IsValid);
    }

    [Fact]
    public void ExpiredTokensAreRejected()
    {
        var issuer = NewIssuer(lifetimeHours: 12);
        var token = issuer.Issue(Guid.CreateVersion7(), EmailAddress.Create("ada@example.com"));

        // Validate with a clock beyond expiry.
        var lateClock = new FixedClock(FixedClock.Default.UtcNow.AddHours(13));
        var validator = new HmacSecurityTokenIssuer(
            new FixedOptionsMonitor<IdentityAuthOptions>(new IdentityAuthOptions { TokenSigningKey = SigningKey, TokenLifetimeHours = 12 }),
            lateClock);

        Assert.False(validator.Validate(token.Value).IsValid);
    }

    [Fact]
    public void WrongSigningKeyRejectsForeignTokens()
    {
        var issuer = NewIssuer();
        var token = issuer.Issue(Guid.CreateVersion7(), EmailAddress.Create("ada@example.com"));

        var otherIssuer = NewIssuer(key: "a-completely-different-signing-key-value-123456");

        Assert.False(otherIssuer.Validate(token.Value).IsValid);
    }

    [Fact]
    public void ShortSigningKeyFailsOnFirstTokenOperation()
    {
        // Configuration is resolved per use: an unconfigured host boots, but the first
        // token operation must fail loudly instead of issuing weakly-signed tokens.
        var issuer = NewIssuer(key: "too-short");

        Assert.Throws<InvalidOperationException>(
            () => issuer.Issue(Guid.CreateVersion7(), EmailAddress.Create("ada@example.com")));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String((padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            _ => padded,
        });
    }
}
