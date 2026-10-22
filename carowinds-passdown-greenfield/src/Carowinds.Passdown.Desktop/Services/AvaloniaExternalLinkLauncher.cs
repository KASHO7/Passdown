using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Carowinds.Passdown.Application.Links;

namespace Carowinds.Passdown.Desktop.Services;

/// <summary>
/// <see cref="IExternalLinkLauncher"/> backed by Avalonia's platform launcher
/// (<see cref="TopLevel.Launcher"/>), which hands the address to the operating system's default
/// browser (ShellExecute on Windows) as a URI — never as a command line. Re-checks the allowlist
/// itself so no caller can route an unvetted address through it. Mirrors <see cref="AvaloniaSyncFolderPicker"/>.
/// </summary>
public sealed class AvaloniaExternalLinkLauncher : IExternalLinkLauncher
{
    public async Task<bool> OpenAsync(Uri officialPage, CancellationToken cancellationToken = default)
    {
        if (!OfficialSourceLinkPolicy.TryValidate(officialPage, out var safe))
        {
            return false;
        }

        var topLevel = global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
        if (topLevel?.Launcher is not { } launcher)
        {
            return false;
        }

        try
        {
            return await launcher.LaunchUriAsync(safe);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
