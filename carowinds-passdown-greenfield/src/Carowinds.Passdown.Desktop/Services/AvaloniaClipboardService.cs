using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Carowinds.Passdown.Documents.Teams;
using Carowinds.Passdown.Infrastructure.Clipboard;

namespace Carowinds.Passdown.Desktop.Services;

/// <summary>
/// <see cref="IClipboardService"/> backed by the main window's Avalonia clipboard (the Windows system
/// clipboard in production).
/// <para>
/// Phase 29C: on Windows, formatted copy bypasses Avalonia and goes through
/// <see cref="WindowsNativeClipboard"/> — "HTML Format" (the UTF-8 CF_HTML payload from
/// <see cref="CfHtml.Build"/>) and CF_UNICODETEXT written directly with the Win32 clipboard API. Avalonia
/// 12.1.1's OLE data object rejects any request whose <c>tymed</c> is not exactly <c>TYMED_HGLOBAL</c>, so OLE
/// consumers (Word, and the Teams desktop paste) could not retrieve the HTML even though raw clipboard
/// readers saw it (docs/phase29c-windows-clipboard-interop.md).
/// </para>
/// <para>
/// Plain-text copy, images, and formatted copy on other platforms (development only) still use Avalonia's
/// clipboard; there the HTML goes out as a <em>bytes</em> platform format, since Avalonia writes a string
/// platform format as UTF-16. A failed formatted copy throws, and the caller falls back to plain text through
/// the independent Avalonia path.
/// </para>
/// </summary>
public sealed class AvaloniaClipboardService : IClipboardService
{
    private readonly Action<nint, string, byte[]>? _windowsHtmlWriter;
    private readonly Func<nint?> _ownerWindow;

    public AvaloniaClipboardService()
        : this(OperatingSystem.IsWindows() ? WindowsNativeClipboard.SetTextAndHtml : null, MainWindowHandle)
    {
    }

    /// <summary>Test seam: <paramref name="windowsHtmlWriter"/> replaces the native writer (null = Avalonia path).</summary>
    internal AvaloniaClipboardService(Action<nint, string, byte[]>? windowsHtmlWriter, Func<nint?> ownerWindow)
    {
        _windowsHtmlWriter = windowsHtmlWriter;
        _ownerWindow = ownerWindow ?? throw new ArgumentNullException(nameof(ownerWindow));
    }

    /// <summary>True when formatted copy goes through the native Windows writer rather than Avalonia.</summary>
    internal bool UsesNativeWindowsHtml => _windowsHtmlWriter is not null;

    public async Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        await GetClipboard().SetTextAsync(text);
    }

    public async Task SetTextAndHtmlAsync(string text, string htmlFragment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(htmlFragment);

        if (_windowsHtmlWriter is not null)
        {
            var owner = _ownerWindow() ?? throw new InvalidOperationException("No application window is available to own the clipboard.");
            _windowsHtmlWriter(owner, text, CfHtml.Build(htmlFragment));
            return;
        }

        var clipboard = GetClipboard();

        // Avalonia takes ownership of the DataTransfer; it must not be disposed here.
        await clipboard.SetDataAsync(CreateFormattedData(text, htmlFragment));
    }

    /// <summary>
    /// Phase 29B: the formatted-copy payload for the Avalonia path (non-Windows since Phase 29C), HTML first.
    /// Avalonia's Win32 backend advertises formats in the order they were set (<c>IEnumFORMATETC</c> follows <see cref="DataTransferItem.Formats"/>), and
    /// Windows clipboard convention is richest format first — readers that take the first format they
    /// understand otherwise pick CF_UNICODETEXT and never see the HTML. The plain text stays alongside.
    /// </summary>
    internal static DataTransfer CreateFormattedData(string text, string htmlFragment)
    {
        var item = new DataTransferItem();
        item.Set(HtmlFormat(), HtmlPayload(htmlFragment));
        item.Set(DataFormat.Text, text);

        var data = new DataTransfer();
        data.Add(item);
        return data;
    }

    public async Task SetImageFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var clipboard = GetClipboard();
        await using var stream = File.OpenRead(filePath);
        var bitmap = new Bitmap(stream);
        await clipboard.SetBitmapAsync(bitmap);
    }

    /// <summary>The platform's HTML clipboard type. Format names per https://docs.avaloniaui.net/docs/services/clipboard.</summary>
    internal static DataFormat<byte[]> HtmlFormat() =>
        OperatingSystem.IsWindows() ? DataFormat.CreateBytesPlatformFormat(CfHtml.WindowsFormatName)
        : OperatingSystem.IsMacOS() ? DataFormat.CreateBytesPlatformFormat("public.html")
        : DataFormat.CreateBytesPlatformFormat("text/html");

    /// <summary>CF_HTML on Windows; elsewhere the bare fragment as UTF-8 (those platforms take plain HTML).</summary>
    internal static byte[] HtmlPayload(string htmlFragment) =>
        OperatingSystem.IsWindows() ? CfHtml.Build(htmlFragment) : System.Text.Encoding.UTF8.GetBytes(htmlFragment);

    private static nint? MainWindowHandle() => GetTopLevel()?.TryGetPlatformHandle()?.Handle;

    private static IClipboard GetClipboard() =>
        GetTopLevel()?.Clipboard ?? throw new InvalidOperationException("No application window clipboard is available.");

    private static TopLevel? GetTopLevel() =>
        global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
}
