using System.Text.Json;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Infrastructure.PersonalSync;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Infrastructure.Preferences;

/// <summary>
/// Phase 37F follow-up: <see cref="IPassdownWeatherOutlookPreferenceStore"/> backed by one small local JSON file
/// (<c>passdown-weather-outlook.json</c>) under <see cref="AppPaths.LocalApplicationDataDirectory"/> — the same flat-file
/// convention and atomic writer as <see cref="FileCalendarDisplayPreferenceStore"/>; no SQLite table, no migration.
/// Outside the install folder (updates keep it) and outside the Personal Sync folder (never synced). A missing value
/// falls back to the built-in default (included), never to "off".
/// </summary>
public sealed class FilePassdownWeatherOutlookPreferenceStore(string configFilePath, ILogger<FilePassdownWeatherOutlookPreferenceStore>? logger = null) : IPassdownWeatherOutlookPreferenceStore
{
    public static string DefaultPath { get; } = Path.Combine(AppPaths.LocalApplicationDataDirectory, "passdown-weather-outlook.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    public string ConfigFilePath { get; } = configFilePath;

    public async Task<PassdownWeatherOutlookPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ConfigFilePath))
        {
            return PassdownWeatherOutlookPreferences.BuiltIn;
        }

        try
        {
            var json = await File.ReadAllTextAsync(ConfigFilePath, cancellationToken).ConfigureAwait(false);
            var file = JsonSerializer.Deserialize<PreferencesFile>(json, Options);
            return new PassdownWeatherOutlookPreferences(file?.IncludeWeatherOutlook ?? PassdownWeatherOutlookPreferences.BuiltIn.IncludeWeatherOutlook);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger?.LogWarning("Passdown Weather Outlook preference could not be read ({ExceptionType}); using the built-in default.", ex.GetType().Name);
            return PassdownWeatherOutlookPreferences.BuiltIn;
        }
    }

    public async Task SaveAsync(PassdownWeatherOutlookPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var json = JsonSerializer.Serialize(new PreferencesFile(preferences.IncludeWeatherOutlook), Options);
        await AtomicFileWriter.WriteTextAsync(ConfigFilePath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The file's shape; nullable so "missing" is distinguishable from an explicit false.</summary>
    private sealed record PreferencesFile(bool? IncludeWeatherOutlook);
}
