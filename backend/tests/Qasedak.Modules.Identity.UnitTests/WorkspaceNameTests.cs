using Qasedak.Modules.Identity.Domain;
using Qasedak.Modules.Identity.Domain.Workspaces;
using Xunit;

namespace Qasedak.Modules.Identity.UnitTests;

public sealed class WorkspaceNameTests
{
    [Fact]
    public void CreateTrimsSurroundingWhitespace()
    {
        Assert.Equal("Acme Corp", WorkspaceName.Create("  Acme Corp\t").Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" a")]
    [InlineData("ab")]
    public void RejectsTooShortNames(string? input)
    {
        Assert.False(WorkspaceName.TryCreate(input, out _));
        Assert.Throws<DomainRuleViolationException>(() => WorkspaceName.Create(input!));
    }

    [Fact]
    public void RejectsTooLongName()
    {
        var longName = new string('x', WorkspaceName.MaxLength + 1);

        Assert.Throws<DomainRuleViolationException>(() => WorkspaceName.Create(longName));
    }
}
