using System.Text.Json;
using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Infrastructure.PersonalSync;

/// <summary>
/// <see cref="ISyncConfigurationStore"/> backed by one small local JSON file (Phase 16B §2, §18) —
/// deliberately not a SQLite table, matching CLAUDE.md's "prefer no SQLite schema change" and the
/// same flat-file-under-<see cref="AppPaths.LocalApplicationDataDirectory"/> convention as
/// <see cref="FileDeviceIdProvider"/>. A missing or unreadable file is treated as
/// <see cref="SyncConfiguration.Disabled"/>, never a thrown exception — a corrupt local settings
/// file must never block normal app startup or normal (non-sync) app functionality.
/// </summary>
public sealed class FileSyncConfigurationStore(string configFilePath) : ISyncConfigurationStore
{
    public static string DefaultPath { get; } = Path.Combine(AppPaths.LocalApplicationDataDirectory, "sync-config.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    public string ConfigFilePath { get; } = configFilePath;

    public async Task<SyncConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ConfigFilePath))
        {
            return SyncConfiguration.Disabled;
        }

        try
        {
            var json = await File.ReadAllTextAsync(ConfigFilePath, cancellationToken).ConfigureAwait(false);
            var configuration = JsonSerializer.Deserialize<SyncConfiguration>(json, Options);
            return configuration ?? SyncConfiguration.Disabled;
        }
        catch (JsonException)
        {
            return SyncConfiguration.Disabled;
        }
        catch (IOException)
        {
            return SyncConfiguration.Disabled;
        }
        catch (UnauthorizedAccessException)
        {
            return SyncConfiguration.Disabled;
        }
    }

    public async Task SaveAsync(SyncConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var json = JsonSerializer.Serialize(configuration, Options);
        await AtomicFileWriter.WriteTextAsync(ConfigFilePath, json, cancellationToken).ConfigureAwait(false);
    }
}
