namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>
/// Persisted local Personal Sync configuration (Phase 16B §2). Never a credential, Microsoft account
/// identifier, or cloud token. The user's explicit folder choice (<see cref="SyncRootPath"/>) is
/// authoritative; nothing here is ever inferred or silently changed by a background process.
/// </summary>
/// <param name="LastKnownSyncSpaceId">
/// Phase 16F §19/§20: the <see cref="SyncManifest.SyncSpaceId"/> this device last reconciled
/// <see cref="SyncRootPath"/> against — null until the first successful sync pass against a folder.
/// When a sync pass finds the current folder's manifest carries a *different* <see cref="SyncManifest.SyncSpaceId"/>
/// than this (a folder switch to a different, possibly-empty sync universe — including switching back
/// to an old one that was itself later reset), the pass must not trust any existing
/// <see cref="SyncEntityState.SyncedContentHash"/> as meaning "already shared with this folder"; see
/// <c>PersonalSyncService</c>'s folder-switch reconciliation. Itself local/device state — never
/// synced, exactly like <see cref="SyncRootPath"/> (Phase 16A §A).
/// </param>
public sealed record SyncConfiguration(bool PersonalSyncEnabled, string? SyncRootPath, Guid? LastKnownSyncSpaceId = null)
{
    /// <summary>The default when no configuration has ever been saved — sync stays off until a user explicitly turns it on (spec: "Sync disabled must remain the default").</summary>
    public static SyncConfiguration Disabled { get; } = new(false, null);
}

/// <summary>Local persistence for <see cref="SyncConfiguration"/> — one small settings file, never a synced one (it is itself device/operational state, Phase 16A §A).</summary>
public interface ISyncConfigurationStore
{
    /// <summary>Never throws for a missing or malformed file — returns <see cref="SyncConfiguration.Disabled"/> instead, so a corrupt settings file can never block normal app startup.</summary>
    Task<SyncConfiguration> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(SyncConfiguration configuration, CancellationToken cancellationToken = default);
}
