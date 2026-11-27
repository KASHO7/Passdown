namespace Carowinds.Passdown.Application.Preferences;

/// <summary>
/// Phase 37F follow-up: whether newly generated Associate Passdown PDFs include the compact Weather Outlook. A device
/// preference (like the PDF style and Calendar display), saved on this computer only — never part of a draft, Personal
/// Sync, the database or a finalized snapshot. Off omits weather from every template with the original layout.
/// </summary>
public sealed record PassdownWeatherOutlookPreferences(bool IncludeWeatherOutlook)
{
    /// <summary>Built-in default when nothing is saved: the outlook is included.</summary>
    public static PassdownWeatherOutlookPreferences BuiltIn { get; } = new(IncludeWeatherOutlook: true);
}

/// <summary>Local persistence for <see cref="PassdownWeatherOutlookPreferences"/> (one small file outside any sync folder).</summary>
public interface IPassdownWeatherOutlookPreferenceStore
{
    /// <summary>Never throws: a missing or unreadable file, or a missing value, returns the built-in default (included).</summary>
    Task<PassdownWeatherOutlookPreferences> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes atomically; may throw on an I/O failure (the caller logs it).</summary>
    Task SaveAsync(PassdownWeatherOutlookPreferences preferences, CancellationToken cancellationToken = default);
}

/// <summary>In-memory store (tests, and the fallback when no store is composed). Never touches disk.</summary>
public sealed class InMemoryPassdownWeatherOutlookPreferenceStore(PassdownWeatherOutlookPreferences? initial = null) : IPassdownWeatherOutlookPreferenceStore
{
    public PassdownWeatherOutlookPreferences Current { get; private set; } = initial ?? PassdownWeatherOutlookPreferences.BuiltIn;

    public int SaveCount { get; private set; }

    public Task<PassdownWeatherOutlookPreferences> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);

    public Task SaveAsync(PassdownWeatherOutlookPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        Current = preferences;
        SaveCount++;
        return Task.CompletedTask;
    }
}
