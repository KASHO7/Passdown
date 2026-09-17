using UglyToad.PdfPig;

namespace Carowinds.Passdown.Documents.Tests.Associate.TestSupport;

/// <summary>Test-only PDF page-count/text-extraction helper (PdfPig), so assertions can inspect real rendered content instead of merely checking non-empty bytes (spec Phase 4E Part J). Never referenced by the Documents project itself.</summary>
internal static class PdfInspector
{
    public static int CountPages(byte[] pdfBytes)
    {
        using var document = PdfDocument.Open(pdfBytes);
        return document.NumberOfPages;
    }

    public static string ExtractAllText(byte[] pdfBytes)
    {
        using var document = PdfDocument.Open(pdfBytes);
        return string.Join("\n", document.GetPages().Select(page => page.Text));
    }

    public static string ExtractPageText(byte[] pdfBytes, int pageNumber)
    {
        using var document = PdfDocument.Open(pdfBytes);
        return document.GetPage(pageNumber).Text;
    }

    /// <summary>The vertical (PDF-space) position of the first word on <paramref name="pageNumber"/> whose text exactly matches <paramref name="wordText"/> (ordinal, case-sensitive) — used to prove composition (e.g. two section headings sharing one row vs. one strictly above the other) from real glyph positions rather than from linear extracted-text order alone.</summary>
    public static double? FindWordTop(byte[] pdfBytes, int pageNumber, string wordText)
    {
        using var document = PdfDocument.Open(pdfBytes);
        var word = document.GetPage(pageNumber).GetWords()
            .FirstOrDefault(candidate => string.Equals(candidate.Text, wordText, StringComparison.Ordinal));
        return word?.BoundingBox.Top;
    }
}
