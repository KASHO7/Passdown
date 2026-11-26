using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Carowinds.Passdown.Documents.Associate;

/// <summary>
/// Phase 33: a content fingerprint of generated PDF bytes that ignores QuestPDF's wall-clock
/// <c>/CreationDate</c> and <c>/ModDate</c> metadata, so two renders of the same Passdown state are
/// recognized as the same document (the live preview then keeps its page image instead of flickering).
/// The visible "generated at" text inside the page is real content and is not ignored.
/// </summary>
public static partial class PdfContentFingerprint
{
    public static string Compute(byte[] pdfBytes)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        var text = Encoding.Latin1.GetString(pdfBytes);
        var normalized = TimestampPattern().Replace(text, "/$1 ()");
        return Convert.ToHexString(SHA256.HashData(Encoding.Latin1.GetBytes(normalized)));
    }

    [GeneratedRegex(@"/(CreationDate|ModDate)\s*\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampPattern();
}
