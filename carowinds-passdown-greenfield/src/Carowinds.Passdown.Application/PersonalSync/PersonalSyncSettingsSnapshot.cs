namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>The small set of states the Personal Sync settings screen shows (Phase 16B §14) — deliberately not "last sync time"/"sync now"/conflict count/device list, all of which belong to a later phase.</summary>
public enum PersonalSyncStatus
{
    Off,
    NotConfigured,
    Ready,
    FolderUnavailable,
    ConfigurationError,
}

/// <summary>Everything the Personal Sync settings screen needs to render, computed fresh on every load/change — never cached longer than one call.</summary>
public sealed record PersonalSyncSettingsSnapshot(
    bool PersonalSyncEnabled,
    string? SyncRootPath,
    PersonalSyncStatus Status,
    string? StatusDetail,
    IReadOnlyList<string> SuggestedRoots,
    Guid DeviceId);

/// <summary>
/// Orchestrates the local sync configuration, folder validation, OneDrive suggestions, and device ID
/// behind the Personal Sync settings screen (Phase 16B §4/§14). Never exports/imports a draft,
/// Operational Note, or finalized passdown — that begins in 16C.
/// </summary>
public interface IPersonalSyncSettingsService
{
    Task<PersonalSyncSettingsSnapshot> LoadAsync(CancellationToken cancellationToken = default);

    Task<PersonalSyncSettingsSnapshot> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);

    Task<PersonalSyncSettingsSnapshot> SetSyncRootAsync(string syncRootPath, CancellationToken cancellationToken = default);
}
