namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>
/// Persisted local Personal Sync configuration (Phase 16B §2). Deliberately the only two fields the
/// architecture calls for — never a credential, Microsoft account identifier, or cloud token. The
/// user's explicit folder choice (<see cref="SyncRootPath"/>) is authoritative; nothing here is ever
/// inferred or silently changed by a background process.
/// </summary>
public sealed record SyncConfiguration(bool PersonalSyncEnabled, string? SyncRootPath)
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
