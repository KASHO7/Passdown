namespace Carowinds.Passdown.Application.Preferences;

/// <summary>
/// Phase 35A: Settings → Appearance → Interface animations. A local device preference like
/// <see cref="AppearancePreference"/> — persisted by name ("On"/"Off"), never part of Personal Sync.
/// </summary>
public enum InterfaceAnimationPreference
{
    /// <summary>Short, subtle interface transitions. The default for a fresh profile (no stored value).</summary>
    On = 0,

    /// <summary>No cosmetic interface animation at all.</summary>
    Off = 1,
}

/// <summary>Phase 35A: what the operating system says about reduced motion.</summary>
public enum SystemReducedMotion
{
    /// <summary>The platform setting could not be read (non-Windows host, or the read failed). The in-app choice decides.</summary>
    NotDetected = 0,

    /// <summary>The operating system allows animation (Windows "Animation effects" on).</summary>
    NotRequested = 1,

    /// <summary>The operating system asks apps to reduce motion (Windows "Animation effects" off).</summary>
    Requested = 2,
}

/// <summary>Phase 35A: the one rule that turns the in-app choice plus the operating system setting into whether interface motion is shown.</summary>
public static class InterfaceMotionResolver
{
    /// <summary>
    /// Motion is shown only when the user's choice is On and the operating system does not request reduced
    /// motion. The operating system can only turn motion off; it never changes the saved choice.
    /// </summary>
    public static bool IsMotionEffective(InterfaceAnimationPreference preference, SystemReducedMotion system) =>
        Sanitize(preference) == InterfaceAnimationPreference.On && system != SystemReducedMotion.Requested;

    /// <summary>A stored value is honored only when it names a defined option; anything else falls back to <see cref="InterfaceAnimationPreference.On"/>, the default.</summary>
    public static InterfaceAnimationPreference Sanitize(InterfaceAnimationPreference preference) =>
        Enum.IsDefined(preference) ? preference : InterfaceAnimationPreference.On;
}

/// <summary>Phase 35A: local persistence for <see cref="InterfaceAnimationPreference"/> — one small file on this computer. Never synced.</summary>
public interface IInterfaceAnimationPreferenceStore
{
    /// <summary>
    /// Synchronous so the effective motion state is known before the first window is drawn. Reads one tiny local
    /// file — no database, no network. Never throws: a missing, unreadable or unrecognized value returns
    /// <see cref="InterfaceAnimationPreference.On"/>.
    /// </summary>
    InterfaceAnimationPreference Load();

    /// <summary>Writes only the interface-animations file, atomically.</summary>
    Task SaveAsync(InterfaceAnimationPreference preference, CancellationToken cancellationToken = default);
}

/// <summary>Phase 35A: reads the operating system's reduced-motion setting. Cheap, synchronous, never throws.</summary>
public interface ISystemReducedMotionReader
{
    SystemReducedMotion Read();
}
