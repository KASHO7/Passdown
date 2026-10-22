using System.Globalization;
using System.Text;

namespace Carowinds.Passdown.Documents.Teams;

/// <summary>
/// Phase 29: the Windows "HTML Format" clipboard payload (CF_HTML,
/// https://learn.microsoft.com/en-us/windows/win32/dataxchg/html-clipboard-format). Pure byte building —
/// no Windows API — so it is unit-testable on any host; the Desktop clipboard service places the bytes on
/// the clipboard under the registered "HTML Format" name next to the plain text.
/// <para>
/// Layout: an ASCII header ("Version:0.9", StartHTML, EndHTML, StartFragment, EndFragment), then
/// <c>&lt;html&gt;&lt;body&gt;&lt;!--StartFragment--&gt;…&lt;!--EndFragment--&gt;&lt;/body&gt;&lt;/html&gt;</c>.
/// Every offset is a byte offset into the whole UTF-8 payload, written as ten zero-padded digits so the
/// header's own length is fixed before the offsets are known. No byte-order mark and no trailing NUL.
/// </para>
/// </summary>
public static class CfHtml
{
    /// <summary>The registered clipboard format name Windows applications (Teams, Edge, Office) read.</summary>
    public const string WindowsFormatName = "HTML Format";

    public const string StartFragmentMarker = "<!--StartFragment-->";

    public const string EndFragmentMarker = "<!--EndFragment-->";

    private const string HeaderTemplate =
        "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";

    private const string DocumentPrefix = "<html>\r\n<body>\r\n" + StartFragmentMarker;

    private const string DocumentSuffix = EndFragmentMarker + "\r\n</body>\r\n</html>";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Wraps <paramref name="htmlFragment"/> (already-safe markup, e.g. from <see cref="TeamsHtmlRenderer"/>) in a complete CF_HTML payload.</summary>
    public static byte[] Build(string htmlFragment)
    {
        ArgumentNullException.ThrowIfNull(htmlFragment);

        var headerLength = Utf8NoBom.GetByteCount(string.Format(CultureInfo.InvariantCulture, HeaderTemplate, 0, 0, 0, 0));
        var startHtml = headerLength;
        var startFragment = startHtml + Utf8NoBom.GetByteCount(DocumentPrefix);
        var endFragment = startFragment + Utf8NoBom.GetByteCount(htmlFragment);
        var endHtml = endFragment + Utf8NoBom.GetByteCount(DocumentSuffix);

        var header = string.Format(CultureInfo.InvariantCulture, HeaderTemplate, startHtml, endHtml, startFragment, endFragment);
        var bytes = Utf8NoBom.GetBytes(header + DocumentPrefix + htmlFragment + DocumentSuffix);

        if (bytes.Length != endHtml)
        {
            throw new InvalidOperationException("CF_HTML offsets do not match the payload length.");
        }

        return bytes;
    }

    /// <summary>The header's offsets and the fragment they delimit — used to validate a payload (tests, diagnostics).</summary>
    public sealed record Parsed(string Version, int StartHtml, int EndHtml, int StartFragment, int EndFragment, string Html, string Fragment);

    /// <summary>Reads a CF_HTML payload back, throwing <see cref="FormatException"/> when the header or offsets are invalid.</summary>
    public static Parsed Parse(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var headerText = Encoding.ASCII.GetString(payload, 0, Math.Min(payload.Length, 256));
        string Value(string name)
        {
            var start = headerText.IndexOf(name + ":", StringComparison.Ordinal);
            if (start < 0)
            {
                throw new FormatException($"CF_HTML header is missing {name}.");
            }

            start += name.Length + 1;
            var end = headerText.IndexOf("\r\n", start, StringComparison.Ordinal);
            return end < 0 ? throw new FormatException($"CF_HTML header {name} is not terminated.") : headerText[start..end];
        }

        int Offset(string name) =>
            int.TryParse(Value(name), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= 0 && value <= payload.Length
                ? value
                : throw new FormatException($"CF_HTML {name} is not a valid offset.");

        var version = Value("Version");
        var startHtml = Offset("StartHTML");
        var endHtml = Offset("EndHTML");
        var startFragment = Offset("StartFragment");
        var endFragment = Offset("EndFragment");

        if (!(startHtml <= startFragment && startFragment <= endFragment && endFragment <= endHtml && endHtml == payload.Length))
        {
            throw new FormatException("CF_HTML offsets are out of order or do not match the payload length.");
        }

        var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
        return new Parsed(
            version, startHtml, endHtml, startFragment, endFragment,
            strict.GetString(payload, startHtml, endHtml - startHtml),
            strict.GetString(payload, startFragment, endFragment - startFragment));
    }
}
