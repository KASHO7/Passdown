using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.Time;

namespace Carowinds.Passdown.Infrastructure.PersonalSync;

/// <summary>
/// <see cref="IPersonalSyncSettingsService"/> composing the local configuration store, folder
/// validator, OneDrive suggestions, and device ID provider into the one snapshot the settings screen
/// renders (Phase 16B §4/§14). The manifest is only ever created/validated
/// (<see cref="SyncManifestService.EnsureManifestAsync"/>) when sync is enabled with a folder that
/// already validated — never on a disabled or unconfigured load, matching spec §12.
/// </summary>
public sealed class PersonalSyncSettingsService(
    ISyncConfigurationStore configurationStore,
    ISyncFolderValidator folderValidator,
    IOneDriveLocationSuggestionProvider suggestionProvider,
    IDeviceIdProvider deviceIdProvider,
    IClock clock) : IPersonalSyncSettingsService
{
    public async Task<PersonalSyncSettingsSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await configurationStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        return await BuildSnapshotAsync(configuration, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PersonalSyncSettingsSnapshot> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var current = await configurationStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var updated = current with { PersonalSyncEnabled = enabled };
        await configurationStore.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        return await BuildSnapshotAsync(updated, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PersonalSyncSettingsSnapshot> SetSyncRootAsync(string syncRootPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(syncRootPath);

        var current = await configurationStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var updated = current with { SyncRootPath = syncRootPath };
        await configurationStore.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        return await BuildSnapshotAsync(updated, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PersonalSyncSettingsSnapshot> BuildSnapshotAsync(SyncConfiguration configuration, CancellationToken cancellationToken)
    {
        var deviceId = deviceIdProvider.GetOrCreateDeviceId();
        var suggestions = suggestionProvider.GetSuggestedRoots();

        if (!configuration.PersonalSyncEnabled)
        {
            return new PersonalSyncSettingsSnapshot(false, configuration.SyncRootPath, PersonalSyncStatus.Off, null, suggestions, deviceId);
        }

        if (string.IsNullOrWhiteSpace(configuration.SyncRootPath))
        {
            return new PersonalSyncSettingsSnapshot(true, null, PersonalSyncStatus.NotConfigured, null, suggestions, deviceId);
        }

        var validation = await folderValidator.ValidateAsync(configuration.SyncRootPath, cancellationToken).ConfigureAwait(false);

        if (validation.Outcome == SyncFolderValidationOutcome.Invalid)
        {
            return new PersonalSyncSettingsSnapshot(true, configuration.SyncRootPath, PersonalSyncStatus.ConfigurationError, validation.Message, suggestions, deviceId);
        }

        if (validation.Outcome == SyncFolderValidationOutcome.Unavailable)
        {
            return new PersonalSyncSettingsSnapshot(true, configuration.SyncRootPath, PersonalSyncStatus.FolderUnavailable, validation.Message, suggestions, deviceId);
        }

        var manifestResult = await SyncManifestService.EnsureManifestAsync(configuration.SyncRootPath, clock.UtcNow, cancellationToken).ConfigureAwait(false);

        return manifestResult.Outcome switch
        {
            SyncManifestEnsureOutcome.Ready =>
                new PersonalSyncSettingsSnapshot(true, configuration.SyncRootPath, PersonalSyncStatus.Ready, null, suggestions, deviceId),
            SyncManifestEnsureOutcome.Unavailable =>
                new PersonalSyncSettingsSnapshot(true, configuration.SyncRootPath, PersonalSyncStatus.FolderUnavailable, manifestResult.Message, suggestions, deviceId),
            _ =>
                new PersonalSyncSettingsSnapshot(true, configuration.SyncRootPath, PersonalSyncStatus.ConfigurationError, manifestResult.Message, suggestions, deviceId),
        };
    }
}
