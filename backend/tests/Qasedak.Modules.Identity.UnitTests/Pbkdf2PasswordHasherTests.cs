using Microsoft.Extensions.Options;
using Qasedak.Modules.Identity.Infrastructure.Authentication;
using Xunit;

namespace Qasedak.Modules.Identity.UnitTests;

public sealed class Pbkdf2PasswordHasherTests
{
    private static Pbkdf2PasswordHasher NewHasher(int iterations = 210_000) =>
        new(Options.Create(new IdentityAuthOptions { Pbkdf2Iterations = iterations }));

    [Fact]
    public void HashRoundTripsWithVerify()
    {
        var hasher = NewHasher();
        var hash = hasher.Hash("correct horse battery staple!");

        Assert.True(hasher.Verify("correct horse battery staple!", hash));
        Assert.False(hasher.Verify("correct horse battery staple?", hash));
    }

    [Fact]
    public void SamePasswordYieldsUniqueSalts()
    {
        var hasher = NewHasher();

        var a = hasher.Hash("same-password-123!");
        var b = hasher.Hash("same-password-123!");

        Assert.NotEqual(a, b);
        Assert.True(hasher.Verify("same-password-123!", a));
        Assert.True(hasher.Verify("same-password-123!", b));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-valid-format")]
    [InlineData("md5.100.aaa.bbb")]
    [InlineData("pbkdf2-sha256.abc.aaa.bbb")]
    [InlineData("pbkdf2-sha256.210000.!!!.???")]
    public void MalformedStoredHashesFailVerification(string storedHash)
    {
        var hasher = NewHasher();

        Assert.False(hasher.Verify("anything", storedHash));
    }

    [Fact]
    public void IterationsBelowSafetyFloorAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NewHasher(iterations: 99_999));
    }
}
