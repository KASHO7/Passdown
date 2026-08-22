using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage;

/// <summary>
/// Phase 7D Part F: <c>carowinds-passdown.db</c> existing but not being a valid database this
/// build can open (a stray text file, a previous crash's partial write, disk corruption) must
/// never be silently deleted, overwritten, or replaced with a fresh blank database — the file is
/// the user's only copy of their data, and the safest thing this build can do is fail loudly and
/// leave it exactly as found for diagnosis.
/// </summary>
public class CorruptDatabaseTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-corrupt-tests-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Fact]
    public void Run_DatabaseFileIsNotValidSqlite_ThrowsAndLeavesFileByteForByteUnchanged()
    {
        var originalBytes = "This is not a SQLite database. Just some arbitrary text content."u8.ToArray();
        File.WriteAllBytes(_databasePath, originalBytes);

        var runner = new MigrationRunner(
            new SqliteConnectionFactory(_databasePath),
            [new Migration0001CreateSchemaVersionTable()],
            NullLogger<MigrationRunner>.Instance);

        Assert.ThrowsAny<SqliteException>(() => runner.Run());

        SqliteConnection.ClearAllPools();
        var bytesAfter = File.ReadAllBytes(_databasePath);
        Assert.Equal(originalBytes, bytesAfter);
    }

    [Fact]
    public void Run_DatabaseFileIsNotValidSqlite_DoesNotCreateWalOrShmFiles()
    {
        File.WriteAllBytes(_databasePath, "garbage"u8.ToArray());

        var runner = new MigrationRunner(
            new SqliteConnectionFactory(_databasePath),
            [new Migration0001CreateSchemaVersionTable()],
            NullLogger<MigrationRunner>.Instance);

        Assert.ThrowsAny<SqliteException>(() => runner.Run());

        SqliteConnection.ClearAllPools();
        Assert.False(File.Exists(_databasePath + "-wal"));
        Assert.False(File.Exists(_databasePath + "-shm"));
    }
}
