using Carowinds.Passdown.Application.Preferences;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Desktop.Services;

/// <summary>
/// Phase 35A: the one owner of whether cosmetic interface motion is shown. It loads the local
/// "Interface animations" choice once at startup (On when nothing is saved), reads the operating system's
/// reduced-motion setting, and exposes <see cref="IsMotionEffective"/> = choice On and the OS not requesting
/// reduced motion. The OS can only switch motion off; it never rewrites the saved choice, so removing the
/// OS setting restores motion when the choice is still On.
/// <para>
/// The OS setting is re-read by <see cref="RefreshSystemSetting"/> (called when the main window is activated —
/// changing the Windows setting means leaving this app). There is no polling and no timer.
/// </para>
/// <para>
/// Motion is purely cosmetic: nothing in this type saves drafts, starts refreshes or touches Personal Sync,
/// and no command or data path waits for it. Must be used on the UI thread.
/// </para>
/// </summary>
public sealed class InterfaceMotionController : IDisposable
{
    private readonly IInterfaceAnimationPreferenceStore _store;
    private readonly ISystemReducedMotionReader _systemReader;
    private readonly ILogger<InterfaceMotionController>? _logger;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private bool _initialized;

    public InterfaceMotionController(
        IInterfaceAnimationPreferenceStore store,
        ISystemReducedMotionReader systemReader,
        ILogger<InterfaceMotionController>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(systemReader);

        _store = store;
        _systemReader = systemReader;
        _logger = logger;
    }

    /// <summary>The user's saved choice. On until <see cref="Initialize"/> loads it.</summary>
    public InterfaceAnimationPreference Preference { get; private set; } = InterfaceAnimationPreference.On;

    /// <summary>The operating system's reduced-motion setting as last read.</summary>
    public SystemReducedMotion SystemReducedMotion { get; private set; } = SystemReducedMotion.NotDetected;

    /// <summary>True when cosmetic interface transitions should run.</summary>
    public bool IsMotionEffective => InterfaceMotionResolver.IsMotionEffective(Preference, SystemReducedMotion);

    /// <summary>Raised after the choice, the OS setting or the effective state changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Loads the saved choice and reads the OS setting. Later calls do nothing. Never throws for a bad settings file.</summary>
    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        Preference = InterfaceMotionResolver.Sanitize(_store.Load());
        SystemReducedMotion = ReadSystem();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Applies <paramref name="preference"/> immediately, then saves it. Rapid changes are written one at a time and
    /// each write stores the latest choice. Returns false when it could not be saved; it still applies for this session.
    /// </summary>
    public Task<bool> SetPreferenceAsync(InterfaceAnimationPreference preference)
    {
        Initialize();
        preference = InterfaceMotionResolver.Sanitize(preference);
        if (preference == Preference)
        {
            return Task.FromResult(true);
        }

        Preference = preference;
        Changed?.Invoke(this, EventArgs.Empty);
        return SaveLatestAsync();
    }

    /// <summary>Re-reads the OS reduced-motion setting; raises <see cref="Changed"/> only when it differs.</summary>
    public void RefreshSystemSetting()
    {
        Initialize();
        var current = ReadSystem();
        if (current == SystemReducedMotion)
        {
            return;
        }

        SystemReducedMotion = current;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private SystemReducedMotion ReadSystem()
    {
        try
        {
            return _systemReader.Read();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Reduced-motion setting could not be read ({ExceptionType}); the in-app choice decides.", ex.GetType().Name);
            return SystemReducedMotion.NotDetected;
        }
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
            _logger?.LogWarning("Interface animations preference could not be saved ({ExceptionType}); it applies until the app closes.", ex.GetType().Name);
            return false;
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public void Dispose() => _saveLock.Dispose();

    /// <summary>
    /// A controller that changes nothing outside itself: in-memory choice (Off unless given), OS setting not
    /// detected. The default for view models built without the composition root (tests, fixtures), so headless
    /// layout assertions never observe a mid-transition value and nothing writes a settings file.
    /// </summary>
    public static InterfaceMotionController CreateDetached(InterfaceAnimationPreference initial = InterfaceAnimationPreference.Off) =>
        new(new InMemoryStore(initial), new NotDetectedReader());

    private sealed class InMemoryStore(InterfaceAnimationPreference value) : IInterfaceAnimationPreferenceStore
    {
        private InterfaceAnimationPreference _value = value;

        public InterfaceAnimationPreference Load() => _value;

        public Task SaveAsync(InterfaceAnimationPreference preference, CancellationToken cancellationToken = default)
        {
            _value = preference;
            return Task.CompletedTask;
        }
    }

    private sealed class NotDetectedReader : ISystemReducedMotionReader
    {
        public SystemReducedMotion Read() => SystemReducedMotion.NotDetected;
    }
}
