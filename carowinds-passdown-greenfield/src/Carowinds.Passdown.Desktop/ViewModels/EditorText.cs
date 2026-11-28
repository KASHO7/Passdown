namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// The free-text normalization shared by the Associate workspace and its Early Ride Time editor (Phase 36C moved
/// these two rules here, unchanged, so the extracted editor uses exactly the same parsing as the draft fields).
/// </summary>
internal static class EditorText
{
    /// <summary>Blank or whitespace-only text is "no value"; anything else is trimmed.</summary>
    public static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>One entry per non-blank line, each trimmed; CRLF and LF line endings are equivalent.</summary>
    public static IReadOnlyList<string> ParseLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }
}
