using Qasedak.Modules.Automations.Domain.Definitions;
using Xunit;

namespace Qasedak.Modules.Automations.UnitTests;

/// <summary>
/// Deterministic Unicode-aware whole-word boundary algorithm (M13-012 §23): Latin,
/// Persian/Arabic letters, digits, punctuation, emoji adjacency, string edges, multiple
/// spaces, zero-width marks and surrogate pairs.
/// </summary>
public sealed class WholeWordMatcherTests
{
    [Theory]
    [InlineData("hello world", "hello", true)]
    [InlineData("hello world", "world", true)]
    [InlineData("hello world", "ello", false)]
    [InlineData("say hello!", "hello", true)]
    [InlineData("hello, there", "hello", true)]
    [InlineData("xhellox", "hello", false)]
    [InlineData("x hello x", "hello", true)]
    [InlineData("hello", "hello", true)]
    [InlineData("HELLO world", "hello", true)]
    [InlineData("hello", "HELLO", true)]
    public void LatinBoundaries(string text, string word, bool expected)
    {
        Assert.Equal(expected, WholeWordMatcher.ContainsWholeWord(text, word));
    }

    [Theory]
    [InlineData("قیمت چند است", "قیمت", true)]
    [InlineData("چندقیمت", "قیمت", false)]
    [InlineData("قیمت‌گذاری", "قیمت", true)] // ZWNJ (U+200C) is a boundary separator
    [InlineData("فروش ۱۰۰ تایی", "۱۰۰", true)] // Persian digits are word characters
    [InlineData("۱۰۰۰", "۱۰۰", false)]
    [InlineData("قیمت:۵۰۰۰", "قیمت", true)]
    public void PersianArabicBoundaries(string text, string word, bool expected)
    {
        Assert.Equal(expected, WholeWordMatcher.ContainsWholeWord(text, word));
    }

    [Theory]
    [InlineData("get offer 100 now", "100", true)]
    [InlineData("get 1000 now", "100", false)]
    [InlineData("v2.5 is out", "2", false)]
    [InlineData("version 2.5", "2", true)] // '.' is a boundary separator
    [InlineData("id-007 agent", "007", true)]
    public void DigitBoundaries(string text, string word, bool expected)
    {
        Assert.Equal(expected, WholeWordMatcher.ContainsWholeWord(text, word));
    }

    [Theory]
    [InlineData("🎉hello", "hello", true)] // emoji before — symbol, boundary
    [InlineData("hello👋", "hello", true)] // emoji after — symbol, boundary
    [InlineData("👋hello👋", "hello", true)]
    [InlineData("h🚀ello", "hello", false)]
    [InlineData("😀 hi 😀", "hi", true)]
    public void EmojiAdjacency(string text, string word, bool expected)
    {
        Assert.Equal(expected, WholeWordMatcher.ContainsWholeWord(text, word));
    }

    [Theory]
    [InlineData("a  b", "b", true)] // multiple spaces
    [InlineData("a\tb", "a", true)]
    [InlineData("a\nb", "b", true)]
    [InlineData("", "x", false)]
    [InlineData("nothing here", "", false)]
    [InlineData("  spaced  ", "spaced", true)]
    public void WhitespaceAndEdges(string text, string word, bool expected)
    {
        Assert.Equal(expected, WholeWordMatcher.ContainsWholeWord(text, word));
    }

    [Theory]
    [InlineData("café au lait", "café", true)]
    [InlineData("café", "cafe", false)] // no normalization/substitution
    [InlineData("naïve", "naive", false)]
    public void AccentedLetters(string text, string word, bool expected)
    {
        Assert.Equal(expected, WholeWordMatcher.ContainsWholeWord(text, word));
    }

    [Fact]
    public void SurrogatePairWordCharsAreSingleUnits()
    {
        // 𝕏 (U+1D54F) is a LetterNumber; "a𝕏b" embeds the keyword inside word chars.
        Assert.False(WholeWordMatcher.ContainsWholeWord("a𝕏b", "𝕏"));
        Assert.True(WholeWordMatcher.ContainsWholeWord("x 𝕏 y", "𝕏"));
    }

    [Fact]
    public void DeterministicAcrossRepeatedCalls()
    {
        const string text = "قیمت و 100 offer 🎉";
        var first = WholeWordMatcher.ContainsWholeWord(text, "offer");
        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(first, WholeWordMatcher.ContainsWholeWord(text, "offer"));
        }
    }
}
