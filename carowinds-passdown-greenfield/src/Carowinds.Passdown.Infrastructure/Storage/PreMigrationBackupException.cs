namespace Carowinds.Passdown.Infrastructure.Storage;

/// <summary>
/// Phase 27: thrown by <see cref="MigrationRunner"/> when the verified backup it must make before changing an
/// existing database's format could not be made. Thrown inside the migration's transaction, before the migration
/// runs, so the database is left exactly as it was (still readable by the version that wrote it).
/// </summary>
public sealed class PreMigrationBackupException(int fromVersion, int toVersion, DatabaseBackupStatus status)
    : Exception(
        $"The database could not be backed up ({status}) before upgrading it from schema {fromVersion} to {toVersion}. " +
        "Nothing was changed. Free some disk space or close other copies of the app, then start it again.")
{
    public int FromVersion { get; } = fromVersion;

    public int ToVersion { get; } = toVersion;

    public DatabaseBackupStatus Status { get; } = status;
}

/// <summary>
/// Phase 27: thrown by <see cref="MigrationRunner"/> when <c>PRAGMA integrity_check</c> fails after migrations
/// were applied. Startup stops; the database and its pre-migration backup are both kept for recovery.
/// </summary>
public sealed class DatabaseIntegrityException(int schemaVersion)
    : Exception($"The database failed its integrity check after being upgraded to schema {schemaVersion}. It was not used; the pre-upgrade backup was kept.")
{
    public int SchemaVersion { get; } = schemaVersion;
}
