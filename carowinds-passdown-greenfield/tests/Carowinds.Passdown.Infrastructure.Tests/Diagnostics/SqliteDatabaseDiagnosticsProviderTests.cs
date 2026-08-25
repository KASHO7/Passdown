using Carowinds.Passdown.Application.Diagnostics;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Infrastructure.Diagnostics;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
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

    // Phase 9C Part T: a finalized passdown's PDF bytes and snapshot JSON (which can contain
    // associate-entered operational notes) must never surface through the diagnostic export pipeline.
    // SqliteDatabaseDiagnosticsProvider only ever reports file/schema metadata (Exists, SchemaVersion,
    // FileSizeBytes, WalPresent, Note) and never queries finalized_passdowns row content at all — this
    // seeds a row with distinctive secret content and proves none of it leaks into the reported
    // DatabaseDiagnostics, whose shape structurally cannot carry it.
    [Fact]
    public async Task GetAsync_DatabaseContainsFinalizedPassdownWithSensitiveContent_NeverSurfacesItInDiagnostics()
    {
        var connectionFactory = new SqliteConnectionFactory(_databasePath);
        IMigration[] migrations =
        [
            new Migration0001CreateSchemaVersionTable(),
            new Migration0002CreateSourceStateTable(),
            new Migration0003CreateDatedResultsTable(),
            new Migration0004CreateCatalogProductsTable(),
            new Migration0005CreateProductHistoryTable(),
            new Migration0006CreateAssociateDraftsTable(),
            new Migration0007CreateManagerDraftsTable(),
            new Migration0008CreateFinalizedPassdownsTable(),
            new Migration0009AddFinalizedPassdownPdfColumns(),
        ];
        new MigrationRunner(connectionFactory, migrations, NullLogger<MigrationRunner>.Instance).Run();

        const string secretNoteText = "CONFIDENTIAL-OPERATIONAL-NOTE-98765";
        var secretPdfBytes = System.Text.Encoding.UTF8.GetBytes("%PDF-secret-marker-ABCDE12345");
        await new SqliteFinalizedPassdownRepository(connectionFactory).AddAsync(new FinalizedPassdown(
            Guid.NewGuid(), new DateOnly(2026, 8, 25), DateTimeOffset.UtcNow, FinalizedPassdownSnapshot.CurrentSchemaVersion,
            $$"""{"OperationalNotes":["{{secretNoteText}}"]}""", "hash", FinalizationOrigin.Scheduled,
            secretPdfBytes, "deadbeef", secretPdfBytes.LongLength));

        var sut = new SqliteDatabaseDiagnosticsProvider(connectionFactory);
        var result = await sut.GetAsync();

        var serialized = $"{result.Exists}|{result.SchemaVersion}|{result.FileSizeBytes}|{result.WalPresent}|{result.Note}";
        Assert.DoesNotContain(secretNoteText, serialized);
        Assert.DoesNotContain("secret-marker", serialized);

        // Structural guarantee, not just this test's luck: DatabaseDiagnostics has no property that
        // could even hold PDF bytes or snapshot JSON.
        var propertyNames = typeof(DatabaseDiagnostics).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain(propertyNames, name => name.Contains("Pdf", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Snapshot", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("OperationalNote", StringComparison.OrdinalIgnoreCase));
    }
}
