using System.Text.Json;
using System.Text.Json.Serialization;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Infrastructure.PersonalSync;

namespace Carowinds.Passdown.Infrastructure.Preferences;

/// <summary>
/// <see cref="IAssociatePassdownTemplatePreferenceStore"/> backed by one small local JSON file (Phase
/// 17C §15/§39) — deliberately not a SQLite table, matching CLAUDE.md's "prefer no DB migration for
/// template preference" and the same flat-file-under-<see cref="AppPaths.LocalApplicationDataDirectory"/>
/// convention as <see cref="FileSyncConfigurationStore"/>/<see cref="FileDeviceIdProvider"/>. A missing
/// or unreadable file is treated as <see cref="AssociatePassdownTemplatePreference.Default"/> (Classic),
/// never a thrown exception.
/// </summary>
public sealed class FileAssociatePassdownTemplatePreferenceStore(string configFilePath) : IAssociatePassdownTemplatePreferenceStore
{
    public static string DefaultPath { get; } = Path.Combine(AppPaths.LocalApplicationDataDirectory, "associate-passdown-template.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ConfigFilePath { get; } = configFilePath;

    public async Task<AssociatePassdownTemplatePreference> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ConfigFilePath))
        {
            return AssociatePassdownTemplatePreference.Default;
        }

        try
        {
            var json = await File.ReadAllTextAsync(ConfigFilePath, cancellationToken).ConfigureAwait(false);
            var preference = JsonSerializer.Deserialize<AssociatePassdownTemplatePreference>(json, Options);
            return preference ?? AssociatePassdownTemplatePreference.Default;
        }
        catch (JsonException)
        {
            return AssociatePassdownTemplatePreference.Default;
        }
        catch (IOException)
        {
            return AssociatePassdownTemplatePreference.Default;
        }
        catch (UnauthorizedAccessException)
        {
            return AssociatePassdownTemplatePreference.Default;
        }
    }

    public async Task SaveAsync(AssociatePassdownTemplatePreference preference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preference);

        var json = JsonSerializer.Serialize(preference, Options);
        await AtomicFileWriter.WriteTextAsync(ConfigFilePath, json, cancellationToken).ConfigureAwait(false);
    }
}
