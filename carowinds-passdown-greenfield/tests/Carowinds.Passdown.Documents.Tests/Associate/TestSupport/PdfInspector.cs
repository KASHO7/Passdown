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

    /// <summary>Bounding box (left, right, top, PDF space) of the first word on <paramref name="pageNumber"/> exactly matching <paramref name="wordText"/>; with <paramref name="occurrence"/> &gt; 0, the n-th (zero-based) such word. Phase 17F: used to prove price-column alignment and multi-line date/grid composition from real glyph positions.</summary>
    public static (double Left, double Right, double Top)? FindWordBox(byte[] pdfBytes, int pageNumber, string wordText, int occurrence = 0)
    {
        using var document = PdfDocument.Open(pdfBytes);
        var word = document.GetPage(pageNumber).GetWords()
            .Where(candidate => string.Equals(candidate.Text, wordText, StringComparison.Ordinal))
            .Skip(occurrence)
            .FirstOrDefault();
        return word is null ? null : (word.BoundingBox.Left, word.BoundingBox.Right, word.BoundingBox.Top);
    }

    /// <summary>Every word on <paramref name="pageNumber"/>, in PdfPig's reading order.</summary>
    public static IReadOnlyList<string> Words(byte[] pdfBytes, int pageNumber)
    {
        using var document = PdfDocument.Open(pdfBytes);
        return document.GetPage(pageNumber).GetWords().Select(word => word.Text).ToList();
    }
}
