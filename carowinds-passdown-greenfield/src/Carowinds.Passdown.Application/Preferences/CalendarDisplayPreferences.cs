namespace Carowinds.Passdown.Application.Preferences;

/// <summary>
/// Phase 32: Calendar display preferences — currently whether Pass Perks markers appear on the Calendar.
/// A device preference (like appearance and Passdown defaults), saved on this computer only and never part
/// of Personal Sync. Turning markers off hides them; no Pass Perks data is deleted and the Products list is
/// unaffected.
/// </summary>
public sealed record CalendarDisplayPreferences(bool ShowPassPerks)
{
    /// <summary>Built-in default: Pass Perks markers shown.</summary>
    public static CalendarDisplayPreferences BuiltIn { get; } = new(ShowPassPerks: true);
}

/// <summary>Phase 32: local persistence for <see cref="CalendarDisplayPreferences"/> (one small file outside any sync folder).</summary>
public interface ICalendarDisplayPreferenceStore
{
    /// <summary>Never throws: a missing or unreadable file, or a missing value, returns the built-in default for that value.</summary>
    Task<CalendarDisplayPreferences> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes atomically; may throw on an I/O failure (the caller reports it).</summary>
    Task SaveAsync(CalendarDisplayPreferences preferences, CancellationToken cancellationToken = default);
}
