namespace Carowinds.Passdown.Application.Preferences;

/// <summary>
/// Phase 23C: the user's local appearance choice. Persisted by name ("System"/"Light"/"Dark"), never
/// by its display label. A device preference like the PDF template — deliberately not part of
/// Personal Sync, so each computer keeps its own choice.
/// </summary>
public enum AppearancePreference
{
    /// <summary>Follow the operating system's light/dark setting, including later changes. The default for fresh installs and for every RC.7 upgrade (no stored value).</summary>
    System = 0,

    /// <summary>Always the application's light theme, whatever the operating system uses.</summary>
    Light = 1,

    /// <summary>Always the application's dark theme, whatever the operating system uses.</summary>
    Dark = 2,
}

/// <summary>Phase 23C: the theme actually shown — one of the two existing Phase 22A palettes.</summary>
public enum EffectiveAppearance
{
    Light,
    Dark,
}

/// <summary>Phase 23C: the one rule that turns a preference plus the operating system's current appearance into the theme to show.</summary>
public static class AppearanceResolver
{
    /// <summary>Light and Dark override the operating system; System (and any value outside the enum) follows it.</summary>
    public static EffectiveAppearance Resolve(AppearancePreference preference, EffectiveAppearance systemAppearance) => preference switch
    {
        AppearancePreference.Light => EffectiveAppearance.Light,
        AppearancePreference.Dark => EffectiveAppearance.Dark,
        _ => systemAppearance,
    };

    /// <summary>A stored value is honored only when it names a defined option; anything else falls back to <see cref="AppearancePreference.System"/>.</summary>
    public static AppearancePreference Sanitize(AppearancePreference preference) =>
        Enum.IsDefined(preference) ? preference : AppearancePreference.System;
}

/// <summary>
/// Phase 23C: local persistence for <see cref="AppearancePreference"/> — one small settings file on this
/// computer, the same convention as <see cref="IAssociatePassdownTemplatePreferenceStore"/>. Never synced.
/// </summary>
public interface IAppearancePreferenceStore
{
    /// <summary>
    /// Synchronous on purpose: the saved theme must be applied before the first window is created, so the
    /// app never opens in the wrong theme. It reads one tiny local file — no database, no network. Never
    /// throws: a missing, unreadable or unrecognized value returns <see cref="AppearancePreference.System"/>.
    /// </summary>
    AppearancePreference Load();

    /// <summary>Writes only the appearance file; no other setting, database row or sync state is touched.</summary>
    Task SaveAsync(AppearancePreference preference, CancellationToken cancellationToken = default);
}
