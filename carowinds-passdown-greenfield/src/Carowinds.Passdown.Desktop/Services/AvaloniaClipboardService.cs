using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Carowinds.Passdown.Documents.Teams;

namespace Carowinds.Passdown.Desktop.Services;

/// <summary>
/// <see cref="IClipboardService"/> backed by the main window's Avalonia clipboard (the Windows system
/// clipboard in production).
/// <para>
/// Phase 29 formatted copy uses Avalonia 12's <see cref="DataTransfer"/>: one item carrying a
/// <em>bytes</em> platform format named "HTML Format" (listed first since Phase 29B) and
/// <see cref="DataFormat.Text"/> (CF_UNICODETEXT). The HTML format holds the UTF-8 CF_HTML payload from
/// <see cref="CfHtml.Build"/>. A bytes format is required — Avalonia's Win32 backend writes a string platform format as UTF-16, but CF_HTML is UTF-8
/// with byte offsets. Other platforms get their native HTML type (UTF-8 bytes) for development only;
/// Windows is the validated target.
/// </para>
/// </summary>
public sealed class AvaloniaClipboardService : IClipboardService
{
    public async Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        await GetClipboard().SetTextAsync(text);
    }

    public async Task SetTextAndHtmlAsync(string text, string htmlFragment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(htmlFragment);

        var clipboard = GetClipboard();

        // Avalonia takes ownership of the DataTransfer; it must not be disposed here.
        await clipboard.SetDataAsync(CreateFormattedData(text, htmlFragment));
    }

    /// <summary>
    /// Phase 29B: the formatted-copy payload, HTML first. Avalonia's Win32 backend advertises formats in
    /// the order they were set (<c>IEnumFORMATETC</c> follows <see cref="DataTransferItem.Formats"/>), and
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

    private static IClipboard GetClipboard() =>
        GetTopLevel()?.Clipboard ?? throw new InvalidOperationException("No application window clipboard is available.");

    private static TopLevel? GetTopLevel() =>
        global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
}
