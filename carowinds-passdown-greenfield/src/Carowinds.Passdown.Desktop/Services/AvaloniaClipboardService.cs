using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace Carowinds.Passdown.Desktop.Services;

/// <summary>
/// <see cref="IClipboardService"/> backed by the main window's Avalonia clipboard (the Windows system
/// clipboard in production). Plain text only — no rich/HTML clipboard formats.
/// </summary>
public sealed class AvaloniaClipboardService : IClipboardService
{
    public async Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var clipboard = GetTopLevel()?.Clipboard
            ?? throw new InvalidOperationException("No application window clipboard is available.");

        await clipboard.SetTextAsync(text);
    }

    private static TopLevel? GetTopLevel() =>
        global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
}
