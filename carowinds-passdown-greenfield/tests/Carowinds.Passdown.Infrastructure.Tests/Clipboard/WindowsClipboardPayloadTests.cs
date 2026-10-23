using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using Carowinds.Passdown.Infrastructure.Clipboard;

namespace Carowinds.Passdown.Infrastructure.Tests.Clipboard;

/// <summary>
/// Phase 29C: the bytes the native Windows clipboard writer places in each memory block. The Win32 calls
/// themselves run only on Windows (Windows VM checklist); these tests pin the representation.
/// </summary>
public sealed class WindowsClipboardPayloadTests
{
    [Fact]
    public void UnicodeText_IsUtf16LittleEndian_WithOneUtf16Terminator()
    {
        var text = "Today's Operations:\r\nCafé — 10 min";
        var bytes = WindowsClipboardPayload.UnicodeText(text);

        Assert.Equal(Encoding.Unicode.GetByteCount(text) + 2, bytes.Length);
        Assert.Equal(text, Encoding.Unicode.GetString(bytes, 0, bytes.Length - 2));
        Assert.Equal(new byte[] { 0, 0 }, bytes[^2..]);
        Assert.Equal((byte)'T', bytes[0]);
        Assert.Equal(0, bytes[1]);
    }

    [Fact]
    public void UnicodeText_Empty_IsJustTheTerminator()
    {
        Assert.Equal(new byte[] { 0, 0 }, WindowsClipboardPayload.UnicodeText(string.Empty));
    }

    [Fact]
    public void Html_IsTheCfHtmlPayloadUnchanged_PlusOneNulAfterEndHtml()
    {
        var cfHtml = Encoding.UTF8.GetBytes("Version:0.9\r\nStartHTML:0000000105\r\n<html>Café</html>");
        var bytes = WindowsClipboardPayload.Html(cfHtml);

        Assert.Equal(cfHtml.Length + 1, bytes.Length);
        Assert.Equal(cfHtml, bytes[..^1]);
        Assert.Equal(0, bytes[^1]);
        Assert.NotEqual(0xEF, bytes[0]); // no byte-order mark added
    }

    [Fact]
    public void NativeWriter_IsWindowsOnly_AndUsesTheRegisteredHtmlFormatName()
    {
        var platform = typeof(WindowsNativeClipboard).GetCustomAttribute<SupportedOSPlatformAttribute>();

        Assert.Equal("windows", platform?.PlatformName);
        Assert.Equal("HTML Format", typeof(WindowsNativeClipboard).GetField("HtmlFormatName")!.GetRawConstantValue());
    }
}
