using Avalonia.Controls.ApplicationLifetimes;
using Carowinds.Passdown.Application.Links;

namespace Carowinds.Passdown.Desktop.Services;

/// <summary>
/// Phase 37C: opens a National Weather Service page in the default browser. Separate from
/// <see cref="IExternalLinkLauncher"/>, whose allowlist stays Carowinds/Six Flags only (spec §6.1): this one accepts
/// only addresses that pass <see cref="WeatherSourceLinkPolicy"/>. Returns false when it could not open the page.
/// </summary>
public interface IWeatherLinkLauncher
{
    Task<bool> OpenAsync(Uri weatherPage, CancellationToken cancellationToken = default);
}

/// <summary><see cref="IWeatherLinkLauncher"/> through Avalonia's platform launcher, re-checking the NWS allowlist itself.</summary>
public sealed class AvaloniaWeatherLinkLauncher : IWeatherLinkLauncher
{
    public async Task<bool> OpenAsync(Uri weatherPage, CancellationToken cancellationToken = default)
    {
        if (!WeatherSourceLinkPolicy.TryValidate(weatherPage, out var safe))
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
