using System.Text.Json;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Infrastructure.PersonalSync;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Infrastructure.Preferences;

/// <summary>
/// Phase 31D: <see cref="IPassdownDefaultsPreferenceStore"/> backed by one small local JSON file
/// (<c>passdown-defaults.json</c>) under <see cref="AppPaths.LocalApplicationDataDirectory"/> — the same
/// flat-file convention and atomic writer as <see cref="FileAppearancePreferenceStore"/>; no SQLite table, no
/// migration. The file lives outside the install folder (so updates keep it) and outside the Personal Sync
/// folder (so it stays on this computer). Each value is stored by name and read independently: a missing value
/// (an older or hand-trimmed file) falls back to its own built-in default, never to "off".
/// </summary>
public sealed class FilePassdownDefaultsPreferenceStore(string configFilePath, ILogger<FilePassdownDefaultsPreferenceStore>? logger = null) : IPassdownDefaultsPreferenceStore
{
    public static string DefaultPath { get; } = Path.Combine(AppPaths.LocalApplicationDataDirectory, "passdown-defaults.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    public string ConfigFilePath { get; } = configFilePath;

    public async Task<PassdownDefaultSelections> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ConfigFilePath))
        {
            return PassdownDefaultSelections.BuiltIn;
        }

        try
        {
            var json = await File.ReadAllTextAsync(ConfigFilePath, cancellationToken).ConfigureAwait(false);
            var file = JsonSerializer.Deserialize<DefaultsFile>(json, Options);
            if (file is null)
            {
                logger?.LogWarning("Passdown defaults file is empty; using built-in defaults.");
                return PassdownDefaultSelections.BuiltIn;
            }

            var builtIn = PassdownDefaultSelections.BuiltIn;
            return new PassdownDefaultSelections(
                file.DailyAdmission ?? builtIn.DailyAdmission,
                file.FastLane ?? builtIn.FastLane,
                file.Promotions ?? builtIn.Promotions,
                file.SeasonPasses ?? builtIn.SeasonPasses,
                file.AddOns ?? builtIn.AddOns,
                file.OperationalNotes ?? builtIn.OperationalNotes,
                file.Shows ?? builtIn.Shows);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger?.LogWarning("Passdown defaults could not be read ({ExceptionType}); using built-in defaults.", ex.GetType().Name);
            return PassdownDefaultSelections.BuiltIn;
        }
    }

    public async Task SaveAsync(PassdownDefaultSelections defaults, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(defaults);

        var file = new DefaultsFile(
            defaults.DailyAdmission, defaults.FastLane, defaults.Promotions, defaults.SeasonPasses,
            defaults.AddOns, defaults.OperationalNotes, defaults.Shows);
        var json = JsonSerializer.Serialize(file, Options);
        await AtomicFileWriter.WriteTextAsync(ConfigFilePath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The file's shape. Every value is nullable so "missing" is distinguishable from an explicit false.</summary>
    private sealed record DefaultsFile(
        bool? DailyAdmission,
        bool? FastLane,
        bool? Promotions,
        bool? SeasonPasses,
        bool? AddOns,
        bool? OperationalNotes,
        bool? Shows);
}
