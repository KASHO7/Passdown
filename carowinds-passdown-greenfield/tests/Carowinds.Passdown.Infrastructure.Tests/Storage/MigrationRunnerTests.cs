using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage;

public class MigrationRunnerTests : IDisposable
{
    private readonly string _databasePath;

    public MigrationRunnerTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-tests-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private MigrationRunner CreateRunner() =>
        new(
            new SqliteConnectionFactory(_databasePath),
            [new Migration0001CreateSchemaVersionTable()],
            NullLogger<MigrationRunner>.Instance);

    [Fact]
    public void Run_OnMissingDatabaseFile_CreatesFileAndAppliesMigrations()
    {
        Assert.False(File.Exists(_databasePath));

        var appliedVersion = CreateRunner().Run();

        Assert.True(File.Exists(_databasePath));
        Assert.Equal(1, appliedVersion);
    }

    [Fact]
    public void Run_CreatesSchemaVersionHistoryRow()
    {
        CreateRunner().Run();

        using var connection = new SqliteConnectionFactory(_databasePath).OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT version, description FROM schema_version WHERE version = 1;";
        using var reader = command.ExecuteReader();

        Assert.True(reader.Read());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal("Create schema_version history table", reader.GetString(1));
    }

    [Fact]
    public void Run_CalledTwice_IsIdempotent()
    {
        var runner = CreateRunner();

        var firstRun = runner.Run();
        var secondRun = runner.Run();

        Assert.Equal(1, firstRun);
        Assert.Equal(1, secondRun);

        using var connection = new SqliteConnectionFactory(_databasePath).OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_version;";
        var rowCount = Convert.ToInt64(command.ExecuteScalar());

        Assert.Equal(1, rowCount);
    }

    // Phase 7D Part G (#8): a database whose schema is newer than any migration this build knows
    // about (e.g. this build is an older portable release re-run against data a newer release
    // already migrated) must fail safely rather than silently downgrade/reinterpret it.
    [Fact]
    public void Run_DatabaseVersionNewerThanAnyKnownMigration_ThrowsAndLeavesVersionUnchanged()
    {
        SetUserVersion(_databasePath, 99);

        var exception = Assert.Throws<UnsupportedSchemaVersionException>(() => CreateRunner().Run());

        Assert.Equal(99, exception.DatabaseVersion);
        Assert.Equal(1, exception.HighestKnownVersion); // CreateRunner() only registers Migration0001.
        Assert.Equal(99, ReadUserVersionDirect(_databasePath)); // untouched, not downgraded.
    }

    // The guard is exact at the boundary: a database already at the current build's highest
    // known version is a normal, already-up-to-date state, not an error.
    [Fact]
    public void Run_DatabaseVersionEqualToHighestKnownMigration_DoesNotThrow()
    {
        SetUserVersion(_databasePath, 1);

        var appliedVersion = CreateRunner().Run();

        Assert.Equal(1, appliedVersion);
    }

    private static void SetUserVersion(string databasePath, int version)
    {
        using var connection = new SqliteConnectionFactory(databasePath).OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {version};";
        command.ExecuteNonQuery();
    }

    private static int ReadUserVersionDirect(string databasePath)
    {
        using var connection = new SqliteConnectionFactory(databasePath).OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
