using System.Text.Json;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Infrastructure.PersonalSync;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Infrastructure.Preferences;

/// <summary>
/// Phase 32: <see cref="ICalendarDisplayPreferenceStore"/> backed by one small local JSON file
/// (<c>calendar-display.json</c>) under <see cref="AppPaths.LocalApplicationDataDirectory"/> — the same flat-file
/// convention and atomic writer as <see cref="FilePassdownDefaultsPreferenceStore"/>; no SQLite table, no
/// migration. Outside the install folder (updates keep it) and outside the Personal Sync folder (never synced).
/// A missing value falls back to its built-in default (Pass Perks shown), never to "off".
/// </summary>
public sealed class FileCalendarDisplayPreferenceStore(string configFilePath, ILogger<FileCalendarDisplayPreferenceStore>? logger = null) : ICalendarDisplayPreferenceStore
{
    public static string DefaultPath { get; } = Path.Combine(AppPaths.LocalApplicationDataDirectory, "calendar-display.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    public string ConfigFilePath { get; } = configFilePath;

    public async Task<CalendarDisplayPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ConfigFilePath))
        {
            return CalendarDisplayPreferences.BuiltIn;
        }

        try
        {
            var json = await File.ReadAllTextAsync(ConfigFilePath, cancellationToken).ConfigureAwait(false);
            var file = JsonSerializer.Deserialize<PreferencesFile>(json, Options);
            return new CalendarDisplayPreferences(file?.ShowPassPerks ?? CalendarDisplayPreferences.BuiltIn.ShowPassPerks);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger?.LogWarning("Calendar display preferences could not be read ({ExceptionType}); using built-in defaults.", ex.GetType().Name);
            return CalendarDisplayPreferences.BuiltIn;
        }
    }

    public async Task SaveAsync(CalendarDisplayPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var json = JsonSerializer.Serialize(new PreferencesFile(preferences.ShowPassPerks), Options);
        await AtomicFileWriter.WriteTextAsync(ConfigFilePath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The file's shape; nullable so "missing" is distinguishable from an explicit false.</summary>
    private sealed record PreferencesFile(bool? ShowPassPerks);
}
