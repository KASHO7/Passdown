namespace Carowinds.Passdown.Application.SpellCheck;

/// <summary>One candidate word in a piece of text: its position (UTF-16 offsets) and its text.</summary>
public sealed record SpellCheckToken(int Start, int Length, string Word)
{
    public int End => Start + Length;
}

/// <summary>
/// Phase 20A: splits free text into words worth spell checking. Pure and allocation-light so it can
/// run on every debounced edit. Deliberately conservative — it skips anything a spell checker would
/// only flag as noise:
/// <list type="bullet">
/// <item>tokens containing digits ("Fury325", "2027", "6:00", "$105.00");</item>
/// <item>all-caps acronyms of two or more letters ("ERT", "DAP", "VIP");</item>
/// <item>single letters;</item>
/// <item>URLs, e-mail addresses and file-ish tokens (anything joined by "@", "/", "\\", "_", or
/// containing "://" or a dotted run such as "sixflags.com").</item>
/// </list>
/// Hyphenated compounds are checked part by part ("Bring-A-Friend" → "Bring", "Friend"); a word's
/// internal apostrophe is kept ("don't", "Carowinds'"), surrounding punctuation is not.
/// </summary>
public static class SpellCheckTokenizer
{
    public static IReadOnlyList<SpellCheckToken> Tokenize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var tokens = new List<SpellCheckToken>();
        var index = 0;
        while (index < text.Length)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                index++;
                continue;
            }

            // A whitespace-delimited chunk, e.g. "(Carowinds'," or "https://example.com".
            var chunkStart = index;
            while (index < text.Length && !char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            AddWordsFromChunk(text, chunkStart, index, tokens);
        }

        return tokens;
    }

    private static void AddWordsFromChunk(string text, int start, int end, List<SpellCheckToken> tokens)
    {
        var chunk = text.AsSpan(start, end - start);
        if (LooksLikeAddress(chunk))
        {
            return;
        }

        var index = start;
        while (index < end)
        {
            if (!IsWordChar(text[index]))
            {
                index++;
                continue;
            }

            var wordStart = index;
            while (index < end && (IsWordChar(text[index]) || IsInternalApostrophe(text, index, end)))
            {
                index++;
            }

            var word = text.Substring(wordStart, index - wordStart);
            if (ShouldCheck(text, wordStart, index, end, word))
            {
                tokens.Add(new SpellCheckToken(wordStart, word.Length, word));
            }
        }
    }

    private static bool ShouldCheck(string text, int wordStart, int wordEnd, int chunkEnd, string word)
    {
        if (word.Length < 2)
        {
            return false;
        }

        // A letter run glued to a digit ("Fury325", "2nd") is an identifier, not a word.
        var touchesDigitBefore = wordStart > 0 && char.IsDigit(text[wordStart - 1]);
        var touchesDigitAfter = wordEnd < chunkEnd && char.IsDigit(text[wordEnd]);
        if (touchesDigitBefore || touchesDigitAfter)
        {
            return false;
        }

        var letters = 0;
        var upper = 0;
        foreach (var c in word)
        {
            if (char.IsLetter(c))
            {
                letters++;
                if (char.IsUpper(c))
                {
                    upper++;
                }
            }
        }

        return !(letters >= 2 && upper == letters); // all-caps acronym
    }

    private static bool IsWordChar(char c) => char.IsLetter(c);

    private static bool IsInternalApostrophe(string text, int index, int end) =>
        (text[index] == '\'' || text[index] == '’') &&
        index > 0 && char.IsLetter(text[index - 1]) &&
        index + 1 < end && char.IsLetter(text[index + 1]);

    private static bool LooksLikeAddress(ReadOnlySpan<char> chunk)
    {
        if (chunk.Contains("://", StringComparison.Ordinal) ||
            chunk.IndexOfAny('@', '/', '\\') >= 0 ||
            chunk.Contains('_'))
        {
            return true;
        }

        // "sixflags.com", "www.example" — a dot between two letters (sentence-ending dots are trailing).
        for (var i = 1; i < chunk.Length - 1; i++)
        {
            if (chunk[i] == '.' && char.IsLetter(chunk[i - 1]) && char.IsLetter(chunk[i + 1]))
            {
                return true;
            }
        }

        return false;
    }
}
