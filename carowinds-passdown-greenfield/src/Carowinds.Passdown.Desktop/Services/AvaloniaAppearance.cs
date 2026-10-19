using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Carowinds.Passdown.Application.Preferences;

namespace Carowinds.Passdown.Desktop.Services;

/// <summary>Phase 23C: the operating system's current light/dark appearance, with a change notification. A seam so tests can simulate it.</summary>
public interface ISystemAppearanceSource
{
    EffectiveAppearance Current { get; }

    /// <summary>Raised on the UI thread when the operating system switches between light and dark.</summary>
    event EventHandler? Changed;
}

/// <summary>Phase 23C: where the chosen palette is shown. Production sets the application-wide theme variant.</summary>
public interface IThemeVariantTarget
{
    void Apply(EffectiveAppearance appearance);
}

/// <summary>
/// Phase 23C: reads the operating system appearance through Avalonia's own cross-platform
/// <see cref="IPlatformSettings"/> (Windows: the "app mode" light/dark setting; the same API on the macOS
/// dev host) — the exact source Avalonia used for RC.7's <c>RequestedThemeVariant="Default"</c>. It listens
/// to <see cref="IPlatformSettings.ColorValuesChanged"/>; there is no polling. With no platform settings (some
/// test hosts) it reports Light and never raises <see cref="Changed"/>.
/// </summary>
public sealed class AvaloniaSystemAppearanceSource : ISystemAppearanceSource
{
    private readonly IPlatformSettings? _settings;
    private EventHandler? _changed;

    public AvaloniaSystemAppearanceSource()
        : this(Avalonia.Application.Current?.PlatformSettings)
    {
    }

    public AvaloniaSystemAppearanceSource(IPlatformSettings? settings) => _settings = settings;

    public EffectiveAppearance Current => FromPlatform(_settings?.GetColorValues().ThemeVariant);

    public event EventHandler? Changed
    {
        add
        {
            if (_changed is null && _settings is not null)
            {
                _settings.ColorValuesChanged += OnColorValuesChanged;
            }

            _changed += value;
        }
        remove
        {
            _changed -= value;
            if (_changed is null && _settings is not null)
            {
                _settings.ColorValuesChanged -= OnColorValuesChanged;
            }
        }
    }

    internal static EffectiveAppearance FromPlatform(PlatformThemeVariant? variant) =>
        variant == PlatformThemeVariant.Dark ? EffectiveAppearance.Dark : EffectiveAppearance.Light;

    private void OnColorValuesChanged(object? sender, PlatformColorValues e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _changed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Dispatcher.UIThread.Post(() => _changed?.Invoke(this, EventArgs.Empty));
        }
    }
}

/// <summary>
/// Phase 23C: shows a palette by setting <see cref="Avalonia.Application.RequestedThemeVariant"/> — the
/// supported Avalonia mechanism. Every window, popup, menu and tooltip inherits it, and every token in
/// Themes/PassdownTokens.axaml is a <c>DynamicResource</c> theme dictionary entry, so the switch is live.
/// No palette is defined here.
/// </summary>
public sealed class ApplicationThemeVariantTarget : IThemeVariantTarget
{
    public void Apply(EffectiveAppearance appearance)
    {
        if (Avalonia.Application.Current is { } app)
        {
            app.RequestedThemeVariant = ToThemeVariant(appearance);
        }
    }

    public static ThemeVariant ToThemeVariant(EffectiveAppearance appearance) =>
        appearance == EffectiveAppearance.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
}
