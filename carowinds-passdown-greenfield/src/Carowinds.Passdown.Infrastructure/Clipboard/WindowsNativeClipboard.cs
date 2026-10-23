using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Carowinds.Passdown.Infrastructure.Clipboard;

/// <summary>
/// Phase 29C: writes formatted text to the Windows clipboard with the plain Win32 clipboard API
/// (<c>OpenClipboard</c> / <c>EmptyClipboard</c> / <c>SetClipboardData</c>), rendering both formats immediately.
/// <para>
/// Why not Avalonia's <c>DataTransfer</c>: Avalonia 12.1.1 publishes an OLE <c>IDataObject</c>
/// (<c>OleSetClipboard</c>, never flushed) whose <c>QueryGetData</c>/<c>GetData</c> accept a request only when
/// its <c>tymed</c> is exactly <c>TYMED_HGLOBAL</c> (<c>tymed</c> is a bit mask under the OLE contract). An OLE
/// consumer that asks with a combined mask (for example <c>TYMED_HGLOBAL | TYMED_ISTREAM</c>) gets
/// <c>DV_E_TYMED</c> and treats "HTML Format" as unavailable — consistent with Word's Paste Special not
/// listing it — while raw <c>GetClipboardData</c> readers (the Phase 29A inspector) still see valid CF_HTML.
/// With the data placed directly on the clipboard, Windows' own OLE clipboard object serves every consumer.
/// </para>
/// <para>
/// Formats are set richest first: "HTML Format", then CF_UNICODETEXT (Windows synthesizes CF_TEXT,
/// CF_OEMTEXT and CF_LOCALE from it). Windows-only; flag for Windows VM testing.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsNativeClipboard
{
    public const string HtmlFormatName = "HTML Format";

    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;
    private const int OpenAttempts = 10;
    private const int OpenRetryDelayMilliseconds = 50;

    /// <summary>
    /// Replaces the clipboard with <paramref name="cfHtml"/> (a complete CF_HTML payload) and its plain-text
    /// equivalent. <paramref name="ownerWindow"/> is the application's top-level window handle; it becomes
    /// the clipboard owner. Throws <see cref="Win32Exception"/> on any failure; nothing is left half-owned
    /// in memory (blocks the system did not take are freed).
    /// </summary>
    public static void SetTextAndHtml(nint ownerWindow, string text, byte[] cfHtml)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(cfHtml);

        var htmlFormat = RegisterClipboardFormatW(HtmlFormatName);
        if (htmlFormat == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "RegisterClipboardFormat(\"HTML Format\") failed.");
        }

        var html = WindowsClipboardPayload.Html(cfHtml);
        var unicode = WindowsClipboardPayload.UnicodeText(text);

        Open(ownerWindow);
        try
        {
            if (!EmptyClipboard())
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "EmptyClipboard failed.");
            }

            Set(htmlFormat, html);
            Set(CfUnicodeText, unicode);
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static void Open(nint ownerWindow)
    {
        for (var attempt = 1; ; attempt++)
        {
            if (OpenClipboard(ownerWindow))
            {
                return;
            }

            if (attempt == OpenAttempts)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "The clipboard is in use by another program.");
            }

            Thread.Sleep(OpenRetryDelayMilliseconds);
        }
    }

    private static void Set(uint format, byte[] bytes)
    {
        var handle = GlobalAlloc(GmemMoveable, (nuint)bytes.Length);
        if (handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "GlobalAlloc failed.");
        }

        try
        {
            var pointer = GlobalLock(handle);
            if (pointer == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "GlobalLock failed.");
            }

            try
            {
                Marshal.Copy(bytes, 0, pointer, bytes.Length);
            }
            finally
            {
                GlobalUnlock(handle);
            }

            if (SetClipboardData(format, handle) == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), $"SetClipboardData({format}) failed.");
            }

            handle = 0; // the system owns the block now
        }
        finally
        {
            if (handle != 0)
            {
                GlobalFree(handle);
            }
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(nint newOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint format, nint memory);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormatW(string formatName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalFree(nint memory);
}
