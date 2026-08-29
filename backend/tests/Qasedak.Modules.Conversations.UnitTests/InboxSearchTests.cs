using Qasedak.Modules.Conversations.Application.Conversations;
using Xunit;

namespace Qasedak.Modules.Conversations.UnitTests;

/// <summary>
/// M12-001 — inbox search pattern normalization: blank terms are removed, and LIKE
/// wildcards (% / _ / \) in user input are escaped so they match literally instead of
/// widening the query.
/// </summary>
public sealed class InboxSearchTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankTermsProduceNoFilter(string? term)
    {
        Assert.Null(SearchPattern.Build(term));
    }

    [Fact]
    public void PlainTermsAreTrimmedAndWrappedInContainsPattern()
    {
        Assert.Equal("%hello%", SearchPattern.Build("  hello  "));
        Assert.Equal("%قیمت%", SearchPattern.Build("قیمت"));
    }

    [Fact]
    public void PercentIsEscapedSoItNeverMatchesEverything()
    {
        Assert.Equal("%\\%%", SearchPattern.Build("%"));
        Assert.Equal("%100\\%%", SearchPattern.Build("100%"));
    }

    [Fact]
    public void UnderscoreAndBackslashAreEscaped()
    {
        Assert.Equal("%a\\_b%", SearchPattern.Build("a_b"));
        Assert.Equal("%a\\\\b%", SearchPattern.Build("a\\b"));
    }

    [Fact]
    public void EscapedPatternIsCaseInsensitiveOnTheDatabaseSide()
    {
        // Build only normalizes/escapes; case-insensitivity comes from ILIKE at the query
        // layer. The pattern must preserve the original letters verbatim.
        Assert.Equal("%WORLD%", SearchPattern.Build("WORLD"));
    }

    [Fact]
    public void CombinedWildcardsAreAllEscaped()
    {
        Assert.Equal("%100\\%\\_off\\\\%", SearchPattern.Build("100%_off\\"));
    }
}
