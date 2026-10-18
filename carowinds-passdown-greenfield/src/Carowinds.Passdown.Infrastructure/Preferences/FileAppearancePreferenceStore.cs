using System.Text.Json;
using System.Text.Json.Serialization;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Infrastructure.PersonalSync;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Infrastructure.Preferences;

/// <summary>
/// Phase 23C: <see cref="IAppearancePreferenceStore"/> backed by one small local JSON file
/// (<c>appearance.json</c>, e.g. <c>{ "Appearance": "Dark" }</c>) under
/// <see cref="AppPaths.LocalApplicationDataDirectory"/> — the same flat-file convention and atomic writer as
/// <see cref="FileAssociatePassdownTemplatePreferenceStore"/>; no SQLite table, no migration. The file is
/// outside the Personal Sync folder and is never exported, so the choice stays on this computer.
/// A missing file means System (the RC.7 behavior). A malformed, unreadable or unrecognized value also
/// means System, with one non-sensitive warning (exception type or the rejected kind only, never the path
/// or file contents); the bad file is left alone until the user picks an option.
/// </summary>
public sealed class FileAppearancePreferenceStore(string configFilePath, ILogger<FileAppearancePreferenceStore>? logger = null) : IAppearancePreferenceStore
{
    public static string DefaultPath { get; } = Path.Combine(AppPaths.LocalApplicationDataDirectory, "appearance.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        // Names only: a bare number such as 7 is rejected instead of being cast to an undefined option.
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    public string ConfigFilePath { get; } = configFilePath;

    public AppearancePreference Load()
    {
        if (!File.Exists(ConfigFilePath))
        {
            return AppearancePreference.System;
        }

        try
        {
            var json = File.ReadAllText(ConfigFilePath);
            var file = JsonSerializer.Deserialize<AppearanceFile>(json, Options);
            if (file is null)
            {
                logger?.LogWarning("Appearance preference file is empty; using System.");
                return AppearancePreference.System;
            }

            var sanitized = AppearanceResolver.Sanitize(file.Appearance);
            if (sanitized != file.Appearance)
            {
                logger?.LogWarning("Appearance preference value is not recognized; using System.");
            }

            return sanitized;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger?.LogWarning("Appearance preference could not be read ({ExceptionType}); using System.", ex.GetType().Name);
            return AppearancePreference.System;
        }
    }

    public async Task SaveAsync(AppearancePreference preference, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(preference))
        {
            throw new ArgumentOutOfRangeException(nameof(preference), preference, "Unknown appearance preference.");
        }

        var json = JsonSerializer.Serialize(new AppearanceFile(preference), Options);
        await AtomicFileWriter.WriteTextAsync(ConfigFilePath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The file's shape. A missing "Appearance" property deserializes as System (the enum's zero value).</summary>
    private sealed record AppearanceFile(AppearancePreference Appearance);
}
