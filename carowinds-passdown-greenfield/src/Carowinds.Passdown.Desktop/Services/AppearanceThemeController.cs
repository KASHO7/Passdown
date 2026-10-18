using Carowinds.Passdown.Application.Preferences;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Desktop.Services;

/// <summary>
/// Phase 23C: the one owner of the app's light/dark choice for the interactive session. It loads the
/// local preference once at startup (before the first window), shows the matching existing Phase 22A
/// palette through <see cref="IThemeVariantTarget"/>, follows operating-system changes only while the
/// preference is System, and saves a new choice to the local appearance file only.
/// <para>
/// Changing the appearance never saves a draft, never starts an official request and never touches
/// Personal Sync, the database or any other setting — this type has no such dependency.
/// </para>
/// <para>
/// Must be used on the UI thread. <see cref="ISystemAppearanceSource"/> raises its change event there.
/// </para>
/// </summary>
public sealed class AppearanceThemeController : IDisposable
{
    private readonly IAppearancePreferenceStore _store;
    private readonly ISystemAppearanceSource _systemAppearance;
    private readonly IThemeVariantTarget _target;
    private readonly ILogger<AppearanceThemeController>? _logger;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private bool _initialized;

    public AppearanceThemeController(
        IAppearancePreferenceStore store,
        ISystemAppearanceSource systemAppearance,
        IThemeVariantTarget target,
        ILogger<AppearanceThemeController>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(systemAppearance);
        ArgumentNullException.ThrowIfNull(target);

        _store = store;
        _systemAppearance = systemAppearance;
        _target = target;
        _logger = logger;
    }

    /// <summary>The user's choice. System until <see cref="Initialize"/> loads the saved one.</summary>
    public AppearancePreference Preference { get; private set; } = AppearancePreference.System;

    /// <summary>The palette currently shown.</summary>
    public EffectiveAppearance EffectiveAppearance { get; private set; } = EffectiveAppearance.Light;

    /// <summary>The operating system's current appearance (what System mode would show).</summary>
    public EffectiveAppearance SystemAppearance => _systemAppearance.Current;

    /// <summary>Raised after the preference or the shown palette changes.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Loads the saved preference and applies it. Called once from <c>App.OnFrameworkInitializationCompleted</c>
    /// before the main window exists, so a saved Light or Dark is the first theme ever drawn. Later calls do
    /// nothing. Never throws for a bad settings file (the store falls back to System).
    /// </summary>
    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        Preference = AppearanceResolver.Sanitize(_store.Load());
        _systemAppearance.Changed += OnSystemAppearanceChanged;
        Apply();
    }

    /// <summary>
    /// Applies <paramref name="preference"/> immediately (no restart, no Apply button), then saves it. Rapid
    /// changes are written one at a time and each write stores the latest choice, so the file always ends
    /// on the last option picked. Returns false when the choice could not be saved; it still applies for
    /// this session.
    /// </summary>
    public Task<bool> SetPreferenceAsync(AppearancePreference preference)
    {
        Initialize();
        preference = AppearanceResolver.Sanitize(preference);
        if (preference == Preference)
        {
            return Task.FromResult(true);
        }

        Preference = preference;
        Apply();
        return SaveLatestAsync();
    }

    private async Task<bool> SaveLatestAsync()
    {
        await _saveLock.WaitAsync().ConfigureAwait(true);
        try
        {
            await _store.SaveAsync(Preference).ConfigureAwait(true);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Appearance preference could not be saved ({ExceptionType}); it applies until the app closes.", ex.GetType().Name);
            return false;
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private void OnSystemAppearanceChanged(object? sender, EventArgs e)
    {
        // Light and Dark ignore the operating system until the user picks System again.
        if (Preference == AppearancePreference.System)
        {
            Apply();
        }
        else
        {
            Changed?.Invoke(this, EventArgs.Empty); // the "Windows is set to …" hint still updates
        }
    }

    private void Apply()
    {
        var effective = AppearanceResolver.Resolve(Preference, _systemAppearance.Current);
        _target.Apply(effective);
        EffectiveAppearance = effective;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _systemAppearance.Changed -= OnSystemAppearanceChanged;
        _saveLock.Dispose();
    }

    /// <summary>
    /// A controller that changes nothing outside itself: in-memory preference, light operating system, no
    /// theme target. The default for view models built without the composition root (tests, fixtures), so
    /// they can never change the running application's theme or write a settings file.
    /// </summary>
    public static AppearanceThemeController CreateDetached() =>
        new(new InMemoryAppearancePreferenceStore(), new FixedSystemAppearanceSource(), new NoThemeVariantTarget());

    private sealed class InMemoryAppearancePreferenceStore : IAppearancePreferenceStore
    {
        private AppearancePreference _value;

        public AppearancePreference Load() => _value;

        public Task SaveAsync(AppearancePreference preference, CancellationToken cancellationToken = default)
        {
            _value = preference;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedSystemAppearanceSource : ISystemAppearanceSource
    {
        public EffectiveAppearance Current => EffectiveAppearance.Light;

        public event EventHandler? Changed { add { } remove { } }
    }

    private sealed class NoThemeVariantTarget : IThemeVariantTarget
    {
        public void Apply(EffectiveAppearance appearance)
        {
        }
    }
}
