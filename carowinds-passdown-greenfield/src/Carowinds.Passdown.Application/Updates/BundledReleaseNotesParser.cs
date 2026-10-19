using System.Globalization;
using Carowinds.Passdown.Domain.Updates;

namespace Carowinds.Passdown.Application.Updates;

/// <summary>
/// Phase 23D: reads the release-notes file bundled in the application. Format: a short header of
/// <c>Version:</c>, <c>Title:</c> and optional <c>Date:</c> (yyyy-MM-dd) lines, a line containing only
/// <c>---</c>, then the notes in the safe Markdown subset <see cref="ReleaseNotesDocument"/> understands.
/// Returns null when the header is missing or names an invalid version, so a broken file shows "notes
/// unavailable" instead of wrong notes.
/// </summary>
public static class BundledReleaseNotesParser
{
    public static ReleaseNotesContent? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var separator = Array.FindIndex(lines, line => line.Trim() == "---");
        if (separator < 0)
        {
            return null;
        }

        string? version = null;
        string? title = null;
        DateOnly? date = null;
        foreach (var line in lines[..separator])
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            switch (key)
            {
                case "Version":
                    version = value;
                    break;
                case "Title":
                    title = value;
                    break;
                case "Date" when DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed):
                    date = parsed;
                    break;
            }
        }

        if (!SemanticVersion.TryParse(version, out var semanticVersion) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var body = string.Join('\n', lines[(separator + 1)..]);
        return new ReleaseNotesContent(semanticVersion.Normalized, title, date, ReleaseNotesDocument.Parse(body));
    }
}
