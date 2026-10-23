using System.Text;

namespace Carowinds.Passdown.Infrastructure.Clipboard;

/// <summary>
/// Phase 29C: the exact bytes <see cref="WindowsNativeClipboard"/> places in each clipboard memory block.
/// Pure byte building — no Windows API — so it is unit-testable on any host.
/// </summary>
public static class WindowsClipboardPayload
{
    /// <summary>CF_UNICODETEXT: UTF-16LE text followed by one UTF-16 NUL, as the format requires.</summary>
    public static byte[] UnicodeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var bytes = new byte[Encoding.Unicode.GetByteCount(text) + 2];
        Encoding.Unicode.GetBytes(text, 0, text.Length, bytes, 0);
        return bytes;
    }

    /// <summary>
    /// "HTML Format": the complete CF_HTML payload (UTF-8, byte offsets already final) followed by one NUL byte.
    /// The terminator lies after EndHTML, so no offset changes; readers that treat CF_HTML as a C string stop
    /// there instead of running into whatever follows in the memory block.
    /// </summary>
    public static byte[] Html(byte[] cfHtml)
    {
        ArgumentNullException.ThrowIfNull(cfHtml);

        var bytes = new byte[cfHtml.Length + 1];
        cfHtml.CopyTo(bytes, 0);
        return bytes;
    }
}
