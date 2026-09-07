using System.Globalization;
using System.Text;

namespace Qasedak.Modules.Automations.Domain.Definitions;

/// <summary>
/// Deterministic, Unicode-aware, culture-independent whole-word matching (M13-012 §23).
///
/// Boundary algorithm (documented contract):
/// - a WORD character is a letter (Lu/Ll/Lt/Lm/Lo), a number (Nd/Nl), a combining mark
///   (Mn/Mc) or a connector punctuation (Pc) — decided per code point, so surrogate pairs
///   (emoji, rare scripts) are handled as single characters;
/// - everything else — spaces, punctuation, symbols (including emoji), format marks
///   (zero-width joiners/space, ZWNJ) and separators — is a boundary separator;
/// - a keyword K matches the text T at an occurrence iff the characters immediately
///   before and after the occurrence are NOT word characters (start/end of string count
///   as boundaries);
/// - comparison is OrdinalIgnoreCase — no Unicode normalization, no script-specific
///   substitutions, no culture. Persian/Arabic letters and digits are word characters
///   like Latin ones.
///
/// No network, clock, randomness or culture state is consulted.
/// </summary>
public static class WholeWordMatcher
{
    /// <summary>True when <paramref name="word"/> occurs as a whole word inside <paramref name="text"/>.</summary>
    public static bool ContainsWholeWord(string text, string word)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(word))
        {
            return false;
        }

        var searchFrom = 0;
        while (searchFrom <= text.Length - word.Length)
        {
            var index = text.IndexOf(word, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            if (HasBoundaryBefore(text, index) && HasBoundaryAfter(text, index + word.Length))
            {
                return true;
            }

            searchFrom = index + 1;
        }

        return false;
    }

    /// <summary>The occurrence's first character must not be preceded by a word character.</summary>
    private static bool HasBoundaryBefore(string text, int index) => index == 0 || !IsWordCharAt(text, index - 1);

    /// <summary>The occurrence's last character must not be followed by a word character.</summary>
    private static bool HasBoundaryAfter(string text, int index) => index >= text.Length || !IsWordCharAt(text, index);

    private static bool IsWordCharAt(string text, int index)
    {
        var category = GetUnicodeCategory(text, index);
        return IsWordCategory(category);
    }

    private static bool IsWordCategory(UnicodeCategory category) => category switch
    {
        UnicodeCategory.UppercaseLetter or
        UnicodeCategory.LowercaseLetter or
        UnicodeCategory.TitlecaseLetter or
        UnicodeCategory.ModifierLetter or
        UnicodeCategory.OtherLetter or
        UnicodeCategory.DecimalDigitNumber or
        UnicodeCategory.LetterNumber or
        UnicodeCategory.NonSpacingMark or
        UnicodeCategory.SpacingCombiningMark or
        UnicodeCategory.ConnectorPunctuation => true,
        _ => false,
    };

    private static UnicodeCategory GetUnicodeCategory(string text, int index)
    {
        var first = text[index];
        if (char.IsHighSurrogate(first) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            var rune = new Rune(first, text[index + 1]);
            return Rune.GetUnicodeCategory(rune);
        }

        return char.GetUnicodeCategory(first);
    }
}
