namespace Carowinds.Passdown.Infrastructure.Storage;

/// <summary>
/// Thrown by <see cref="MigrationRunner"/> when a database's <c>PRAGMA user_version</c> is higher
/// than any migration this build knows about (Phase 7D Part G) — e.g. an older portable build
/// pointed at data a newer build already migrated. Migrations are forward-only, so there is no
/// safe way to "reinterpret" a schema this build has never seen; proceeding could silently
/// misread or overwrite columns/tables added by a version-ahead migration. The database file
/// itself is never touched when this is thrown — the failure happens before
/// <see cref="MigrationRunner.Run"/> attempts any read or write beyond the version check.
/// </summary>
public sealed class UnsupportedSchemaVersionException(int databaseVersion, int highestKnownVersion)
    : Exception(
        $"Database schema version {databaseVersion} is newer than this application supports " +
        $"(highest known migration version: {highestKnownVersion}). Refusing to proceed to avoid " +
        "misinterpreting or downgrading the database. Install a newer build of the app, or restore " +
        "a compatible database backup.")
{
    public int DatabaseVersion { get; } = databaseVersion;

    public int HighestKnownVersion { get; } = highestKnownVersion;
}
