using Carowinds.Passdown.Application.SpellCheck;

namespace Carowinds.Passdown.Desktop.Spelling;

/// <summary>What the spell-check context menu offers for the misspelled word under the pointer/caret.</summary>
public sealed record SpellCheckMenuModel(SpellingIssue Issue, IReadOnlyList<string> Suggestions, bool CanAddToDictionary)
{
    /// <summary>
    /// The menu for the issue containing <paramref name="textIndex"/> (inclusive of the word's end, so a
    /// caret just after the word still counts), or <see langword="null"/> when the index is not inside
    /// a misspelled word — the TextBox's normal Cut/Copy/Paste menu is then used unchanged.
    /// </summary>
    public static SpellCheckMenuModel? Build(IReadOnlyList<SpellingIssue> issues, int textIndex, ISpellCheckService service)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(service);

        var issue = issues.FirstOrDefault(i => textIndex >= i.Start && textIndex <= i.End);
        return issue is null ? null : new SpellCheckMenuModel(issue, service.Suggest(issue.Word), service.CanAddToDictionary);
    }

    /// <summary>
    /// Replaces the misspelled word with <paramref name="replacement"/>. Returns <see langword="null"/>
    /// (no change) when the text no longer contains the word at that position or the result would
    /// exceed <paramref name="maxLength"/> (0 = unlimited).
    /// </summary>
    public static (string Text, int CaretIndex)? ApplyReplacement(string? text, SpellingIssue issue, string replacement, int maxLength = 0)
    {
        ArgumentNullException.ThrowIfNull(issue);
        ArgumentNullException.ThrowIfNull(replacement);

        if (text is null || issue.End > text.Length ||
            !string.Equals(text.Substring(issue.Start, issue.Length), issue.Word, StringComparison.Ordinal))
        {
            return null;
        }

        var result = string.Concat(text.AsSpan(0, issue.Start), replacement, text.AsSpan(issue.End));
        if (maxLength > 0 && result.Length > maxLength)
        {
            return null;
        }

        return (result, issue.Start + replacement.Length);
    }
}
