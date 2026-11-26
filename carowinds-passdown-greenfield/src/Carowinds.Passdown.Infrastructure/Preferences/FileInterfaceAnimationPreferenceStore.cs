using System.Text.Json;
using System.Text.Json.Serialization;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Infrastructure.PersonalSync;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Infrastructure.Preferences;

/// <summary>
/// Phase 35A: <see cref="IInterfaceAnimationPreferenceStore"/> backed by one small local JSON file
/// (<c>interface-animations.json</c>, e.g. <c>{ "InterfaceAnimations": "Off" }</c>) under
/// <see cref="AppPaths.LocalApplicationDataDirectory"/> — the same flat-file convention and atomic writer as
/// <see cref="FileAppearancePreferenceStore"/>; no SQLite table, no migration, outside the Personal Sync folder
/// and never exported. A missing file means On (the default for a fresh profile). A malformed, unreadable or
/// unrecognized value also means On, with one non-sensitive warning; the bad file is left alone until the user
/// picks an option.
/// </summary>
public sealed class FileInterfaceAnimationPreferenceStore(string configFilePath, ILogger<FileInterfaceAnimationPreferenceStore>? logger = null) : IInterfaceAnimationPreferenceStore
{
    public static string DefaultPath { get; } = Path.Combine(AppPaths.LocalApplicationDataDirectory, "interface-animations.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    public string ConfigFilePath { get; } = configFilePath;

    public InterfaceAnimationPreference Load()
    {
        if (!File.Exists(ConfigFilePath))
        {
            return InterfaceAnimationPreference.On;
        }

        try
        {
            var file = JsonSerializer.Deserialize<InterfaceAnimationsFile>(File.ReadAllText(ConfigFilePath), Options);
            if (file is null)
            {
                logger?.LogWarning("Interface animations preference file is empty; using On.");
                return InterfaceAnimationPreference.On;
            }

            var sanitized = InterfaceMotionResolver.Sanitize(file.InterfaceAnimations);
            if (sanitized != file.InterfaceAnimations)
            {
                logger?.LogWarning("Interface animations preference value is not recognized; using On.");
            }

            return sanitized;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger?.LogWarning("Interface animations preference could not be read ({ExceptionType}); using On.", ex.GetType().Name);
            return InterfaceAnimationPreference.On;
        }
    }

    public async Task SaveAsync(InterfaceAnimationPreference preference, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(preference))
        {
            throw new ArgumentOutOfRangeException(nameof(preference), preference, "Unknown interface animations preference.");
        }

        var json = JsonSerializer.Serialize(new InterfaceAnimationsFile(preference), Options);
        await AtomicFileWriter.WriteTextAsync(ConfigFilePath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The file's shape. A missing "InterfaceAnimations" property deserializes as On (the enum's zero value).</summary>
    private sealed record InterfaceAnimationsFile(InterfaceAnimationPreference InterfaceAnimations);
}
