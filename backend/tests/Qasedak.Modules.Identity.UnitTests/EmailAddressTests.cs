using Qasedak.Modules.Identity.Domain;
using Qasedak.Modules.Identity.Domain.Users;
using Xunit;

namespace Qasedak.Modules.Identity.UnitTests;

public sealed class EmailAddressTests
{
    [Theory]
    [InlineData("Alice@Example.COM ", "alice@example.com")]
    [InlineData("  ops+qasedak@sub.qasedak.io", "ops+qasedak@sub.qasedak.io")]
    public void CreateNormalizesCaseAndWhitespace(string input, string expected)
    {
        Assert.Equal(expected, EmailAddress.Create(input).Value);
    }

    [Fact]
    public void EqualModuloCaseAndWhitespaceAreEqual()
    {
        var a = EmailAddress.Create("Team@Qasedak.io");
        var b = EmailAddress.Create("   team@qasedak.IO");

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("missing-at.example.com")]
    [InlineData("@no-local.example.com")]
    [InlineData("two@@example.com")]
    [InlineData("user@nodot")]
    [InlineData("user@-badlabel.example.com")]
    [InlineData("user@example.badlabellabel.")]
    [InlineData("user@example.c")]
    [InlineData("user@example.123")]
    public void RejectsMalformedAddresses(string? input)
    {
        Assert.False(EmailAddress.TryCreate(input, out _));
        Assert.Throws<DomainRuleViolationException>(() => EmailAddress.Create(input!));
    }

    [Fact]
    public void RejectsOverlongAddress()
    {
        var local = new string('a', 310);
        var input = $"{local}@example.com";

        Assert.True(input.Length > EmailAddress.MaxLength);
        Assert.False(EmailAddress.TryCreate(input, out _));
    }
}
