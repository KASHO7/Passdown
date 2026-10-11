using System.Text.RegularExpressions;

namespace Carowinds.Passdown.Documents.Tests.Associate.Templates.TestSupport;

/// <summary>
/// Whitespace-normalized substring assertion for PdfPig-extracted PDF text. QuestPDF wraps long
/// strings onto multiple visual lines with no explicit space glyph at the wrap point, so PdfPig's
/// per-line text extraction can join two wrapped words with no space between them (e.g. "day in
/// enough" extracts as "day inenough") — a text-extraction artifact of long/wrapping content, not a
/// missing-content bug. Collapsing all whitespace on both sides before comparing makes the assertion
/// robust to exactly where a wrap happened to land, while still failing if the content is genuinely
/// different or missing.
/// </summary>
internal static class PdfTextAssert
{
    private static string Collapse(string value) => Regex.Replace(value, @"\s+", "");

    public static void ContainsIgnoringWhitespace(string expectedSubstring, string actualText) =>
        Assert.Contains(Collapse(expectedSubstring), Collapse(actualText));

    /// <summary>Boolean form of <see cref="ContainsIgnoringWhitespace"/> for callers that need to branch (e.g. scanning which page a snippet landed on) rather than assert directly.</summary>
    public static bool TextContainsIgnoringWhitespace(string actualText, string expectedSubstring) =>
        Collapse(actualText).Contains(Collapse(expectedSubstring));

    /// <summary>
    /// Phase 17F: the operating-date heading is present for <paramref name="template"/> — one line
    /// ("FRIDAY, AUGUST 21, 2026") for Classic/Compact, two lines (weekday over "AUGUST 21, 2026") for
    /// Modern, whose extracted text therefore carries the two parts without the joining comma.
    /// </summary>
    public static void ContainsOperatingDate(Carowinds.Passdown.Application.Preferences.AssociatePassdownTemplate template, string operatingDateText, string actualText)
    {
        var upper = operatingDateText.ToUpperInvariant();
        if (template != Carowinds.Passdown.Application.Preferences.AssociatePassdownTemplate.Modern)
        {
            Assert.Contains(upper, actualText);
            return;
        }

        var split = upper.IndexOf(", ", StringComparison.Ordinal);
        Assert.Contains(upper[..split], actualText);
        Assert.Contains(upper[(split + 2)..], actualText);
    }

    /// <summary>Phase 17F: the per-template Operational Notes heading — Compact's tighter "NOTES", Classic/Modern's "OPERATIONAL NOTES".</summary>
    public static string NotesHeading(Carowinds.Passdown.Application.Preferences.AssociatePassdownTemplate template) =>
        template == Carowinds.Passdown.Application.Preferences.AssociatePassdownTemplate.Compact ? "NOTES" : "OPERATIONAL NOTES";
}
