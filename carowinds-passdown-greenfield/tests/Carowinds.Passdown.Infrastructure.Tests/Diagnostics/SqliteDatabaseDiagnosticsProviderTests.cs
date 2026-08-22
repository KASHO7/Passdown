using Carowinds.Passdown.Infrastructure.Diagnostics;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Infrastructure.Tests.Diagnostics;

/// <summary>
/// Phase 7F Part J: <see cref="SqliteDatabaseDiagnosticsProvider"/> must report metadata only —
/// never table contents — and must never create, write to, or migrate the database as a side
/// effect of merely inspecting it (a diagnostic export on a machine that has never launched the app
/// must not itself create <c>carowinds-passdown.db</c>).
/// </summary>
public class SqliteDatabaseDiagnosticsProviderTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-diag-tests-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Fact]
    public async Task GetAsync_DatabaseFileDoesNotExist_ReportsNotExistingWithoutCreatingTheFile()
    {
        var sut = new SqliteDatabaseDiagnosticsProvider(new SqliteConnectionFactory(_databasePath));

        var result = await sut.GetAsync();

        Assert.False(result.Exists);
        Assert.Null(result.SchemaVersion);
        Assert.Null(result.FileSizeBytes);
        Assert.False(result.WalPresent);
        Assert.False(File.Exists(_databasePath), "Inspecting a non-existent database must never create one.");
    }

    [Fact]
    public async Task GetAsync_ExistingMigratedDatabase_ReportsSchemaVersionAndSize()
    {
        var connectionFactory = new SqliteConnectionFactory(_databasePath);
        var migrations = new IMigration[]
        {
            new Migration0001CreateSchemaVersionTable(),
            new Migration0002CreateSourceStateTable(),
        };
        new MigrationRunner(connectionFactory, migrations, NullLogger<MigrationRunner>.Instance).Run();

        var sut = new SqliteDatabaseDiagnosticsProvider(connectionFactory);

        var result = await sut.GetAsync();

        Assert.True(result.Exists);
        Assert.Equal(2, result.SchemaVersion);
        Assert.True(result.FileSizeBytes > 0);
        Assert.Null(result.Note);
    }

    [Fact]
    public async Task GetAsync_CorruptDatabaseFile_ReportsNoteInsteadOfThrowing()
    {
        await File.WriteAllTextAsync(_databasePath, "THIS-IS-NOT-A-VALID-SQLITE-DATABASE-FILE");

        var sut = new SqliteDatabaseDiagnosticsProvider(new SqliteConnectionFactory(_databasePath));

        var result = await sut.GetAsync();

        Assert.True(result.Exists);
        Assert.Null(result.SchemaVersion);
        Assert.NotNull(result.Note);
    }

    [Fact]
    public async Task GetAsync_WalFilePresent_ReportsWalPresentTrue()
    {
        await File.WriteAllTextAsync(_databasePath, string.Empty);
        await File.WriteAllTextAsync(_databasePath + "-wal", string.Empty);
        try
        {
            var sut = new SqliteDatabaseDiagnosticsProvider(new SqliteConnectionFactory(_databasePath));

            var result = await sut.GetAsync();

            Assert.True(result.WalPresent);
        }
        finally
        {
            File.Delete(_databasePath + "-wal");
        }
    }
}
