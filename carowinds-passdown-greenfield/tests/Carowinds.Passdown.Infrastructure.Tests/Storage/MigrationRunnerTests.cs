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
}
