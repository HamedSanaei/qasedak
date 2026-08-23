using Qasedak.Modules.Identity.Domain;
using Qasedak.Modules.Identity.Domain.Users;
using Xunit;

namespace Qasedak.Modules.Identity.UnitTests;

public sealed class UserTests
{
    [Fact]
    public void CreateAssignsVersion7IdAndCanonicalEmail()
    {
        var user = User.Create(EmailAddress.Create("Ada@Example.com"), "  Ada Lovelace  ");

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal(7, user.Id.Version);
        Assert.Equal("ada@example.com", user.Email.Value);
        Assert.Equal("Ada Lovelace", user.DisplayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("     ")]
    public void RejectsBlankDisplayName(string displayName)
    {
        Assert.Throws<DomainRuleViolationException>(
            () => User.Create(EmailAddress.Create("ada@example.com"), displayName));
    }

    [Fact]
    public void RejectsOverlongDisplayName()
    {
        var longName = new string('x', 129);

        Assert.Throws<DomainRuleViolationException>(() => User.Create(EmailAddress.Create("ada@example.com"), longName));
    }

    [Fact]
    public void FromStatePreservesPersistedIdentity()
    {
        var id = Guid.CreateVersion7();

        var user = User.FromState(id, EmailAddress.Create("restored@example.com"), "Restored");

        Assert.Equal(id, user.Id);
        Assert.Equal("restored@example.com", user.Email.Value);
    }
}
