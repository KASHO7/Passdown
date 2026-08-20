using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage;

/// <summary>
/// A single versioned, forward-only change to the local SQLite schema.
/// Versions gate application via <c>PRAGMA user_version</c>, so each
/// migration must be safe to apply exactly once, in ascending version order.
/// </summary>
public interface IMigration
{
    int Version { get; }

    string Description { get; }

    void Apply(SqliteConnection connection);
}
