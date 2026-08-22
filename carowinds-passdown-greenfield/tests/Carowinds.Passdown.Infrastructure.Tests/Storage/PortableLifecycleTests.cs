using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage;

/// <summary>
/// Phase 7D Parts B/C/E: proves the portable-release data-preservation invariant end to end —
/// "application binaries are disposable, user data is persistent and independent" — using the
/// same full migration list <c>App.axaml.cs</c> registers, against temp-directory databases
/// standing in for a real <c>%LOCALAPPDATA%\Carowinds Passdown</c> (never the developer's actual
/// LocalAppData; CLAUDE.md "Never invent data" applies equally to never touching real user state
/// from a test run).
/// </summary>
public class PortableLifecycleTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-lifecycle-{Guid.NewGuid():N}");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private static IReadOnlyList<IMigration> AllMigrations =>
    [
        new Migration0001CreateSchemaVersionTable(),
        new Migration0002CreateSourceStateTable(),
        new Migration0003CreateDatedResultsTable(),
        new Migration0004CreateCatalogProductsTable(),
        new Migration0005CreateProductHistoryTable(),
        new Migration0006CreateAssociateDraftsTable(),
        new Migration0007CreateManagerDraftsTable(),
    ];

    private const int CurrentSchemaVersion = 7;

    // ------------------------------------------------------------------
    // Part B: clean first run.
    // ------------------------------------------------------------------

    [Fact]
    public void CleanFirstRun_NoExistingLocalAppDataEquivalent_CreatesDirectoryDatabaseAndReachesCurrentVersion()
    {
        Assert.False(Directory.Exists(_tempDirectory));
        var databasePath = Path.Combine(_tempDirectory, "carowinds-passdown.db");
        var runner = new MigrationRunner(new SqliteConnectionFactory(databasePath), AllMigrations, NullLogger<MigrationRunner>.Instance);

        var appliedVersion = runner.Run();

        Assert.True(Directory.Exists(_tempDirectory));
        Assert.True(File.Exists(databasePath));
        Assert.Equal(CurrentSchemaVersion, appliedVersion);
    }

    [Fact]
    public void CleanFirstRun_SecondInitialization_SucceedsWithoutErrorsOrDuplicateHistory()
    {
        var databasePath = Path.Combine(_tempDirectory, "carowinds-passdown.db");
        var connectionFactory = new SqliteConnectionFactory(databasePath);

        var firstVersion = new MigrationRunner(connectionFactory, AllMigrations, NullLogger<MigrationRunner>.Instance).Run();
        var secondVersion = new MigrationRunner(connectionFactory, AllMigrations, NullLogger<MigrationRunner>.Instance).Run();

        Assert.Equal(CurrentSchemaVersion, firstVersion);
        Assert.Equal(CurrentSchemaVersion, secondVersion);

        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_version;";
        Assert.Equal(AllMigrations.Count, Convert.ToInt64(command.ExecuteScalar()));
    }

    [Fact]
    public async Task CleanFirstRun_NoDraftOrCacheAssumesPreexistingRows()
    {
        var databasePath = Path.Combine(_tempDirectory, "carowinds-passdown.db");
        var connectionFactory = new SqliteConnectionFactory(databasePath);
        new MigrationRunner(connectionFactory, AllMigrations, NullLogger<MigrationRunner>.Instance).Run();

        var draftRepository = new SqliteAssociateDraftRepository(connectionFactory);
        var sourceStateRepository = new SqliteSourceStateRepository(connectionFactory);

        Assert.Null(await draftRepository.GetAsync(new DateOnly(2026, 8, 22)));
        Assert.Null(await sourceStateRepository.GetAsync("park-hours"));
    }

    // ------------------------------------------------------------------
    // Part C: existing greenfield data survives restart (reopen + re-run migrations, as
    // App.axaml.cs does on every launch).
    // ------------------------------------------------------------------

    [Fact]
    public async Task Restart_RepresentativeDataAcrossAllTables_SurvivesReopenAndMigrationRerun()
    {
        var databasePath = Path.Combine(_tempDirectory, "carowinds-passdown.db");
        var firstFactory = new SqliteConnectionFactory(databasePath);
        new MigrationRunner(firstFactory, AllMigrations, NullLogger<MigrationRunner>.Instance).Run();

        var operatingDate = new DateOnly(2026, 8, 22);
        var checkedAt = new DateTimeOffset(2026, 8, 22, 6, 0, 0, TimeSpan.Zero);
        var sourceUrl = new Uri("https://www.sixflags.com/carowinds/park-hours");

        await new SqliteAssociateDraftRepository(firstFactory).SaveAsync(
            new AssociateDraft(operatingDate, 1, """{"notes":"Gate crew short-staffed"}""", checkedAt));

        await new SqliteSourceStateRepository(firstFactory).SaveAsync(
            new SourceStateRecord("park-hours", SourceCategory.ParkHours, checkedAt, checkedAt, checkedAt.AddDays(1), "abc123", 0));

        var evidence = new SourceEvidence(
            sourceName: "Park hours",
            sourceUrl: sourceUrl,
            timeChecked: checkedAt,
            status: RetrievalStatus.Verified,
            detail: "Checked successfully.",
            strategy: RetrievalStrategy.Html,
            requestedDate: operatingDate,
            confirmedDate: operatingDate);
        await new SqliteDatedResultRepository(firstFactory).RecordSuccessAsync(
            new DatedResultKey(SourceCategory.ParkHours, operatingDate), evidence, """{"open":"10:00","close":"22:00"}""");

        var productKey = CatalogProductKey.FromSourceAndName(SourceCategory.SeasonPasses, "Season passes", "Gold Pass");
        var catalogEvidence = new SourceEvidence(
            sourceName: "Season passes", sourceUrl: sourceUrl, timeChecked: checkedAt,
            status: RetrievalStatus.Verified, detail: "Checked successfully.", strategy: RetrievalStrategy.Html);
        await new SqliteCatalogProductRepository(firstFactory).UpsertAsync(new CatalogProductUpsert(
            Key: productKey,
            DisplayName: "Gold Pass",
            LifecycleState: ProductLifecycleState.Active,
            FirstSeenAt: checkedAt,
            LastSeenAt: checkedAt,
            LastVerifiedAt: checkedAt,
            ConsecutiveConfirmedAbsenceCount: 0,
            ArchivedAt: null,
            ArchivedReason: null,
            CurrentPriceAmount: 149.99m,
            CurrentDisplayPrice: "$149.99",
            ComparisonPriceAmount: 199.99m,
            PriceQualifier: "Online Price",
            DateSpecific: false,
            ApplicableDate: null,
            LastEvidence: catalogEvidence));

        // Simulate a process exit/relaunch: a fresh connection factory over the same file, then
        // the exact same startup step App.axaml.cs performs on every launch (re-run migrations).
        var secondFactory = new SqliteConnectionFactory(databasePath);
        var reappliedVersion = new MigrationRunner(secondFactory, AllMigrations, NullLogger<MigrationRunner>.Instance).Run();
        Assert.Equal(CurrentSchemaVersion, reappliedVersion);

        var reloadedDraft = await new SqliteAssociateDraftRepository(secondFactory).GetAsync(operatingDate);
        Assert.NotNull(reloadedDraft);
        Assert.Equal("""{"notes":"Gate crew short-staffed"}""", reloadedDraft.PayloadJson);

        var reloadedSourceState = await new SqliteSourceStateRepository(secondFactory).GetAsync("park-hours");
        Assert.NotNull(reloadedSourceState);
        Assert.Equal("abc123", reloadedSourceState.SourceFingerprint);

        var reloadedDatedResult = await new SqliteDatedResultRepository(secondFactory).GetAsync(new DatedResultKey(SourceCategory.ParkHours, operatingDate));
        Assert.NotNull(reloadedDatedResult);
        Assert.NotNull(reloadedDatedResult.SuccessEvidence);
        Assert.Equal(RetrievalStatus.Verified, reloadedDatedResult.SuccessEvidence.Status);

        var reloadedProduct = await new SqliteCatalogProductRepository(secondFactory).GetAsync(productKey);
        Assert.NotNull(reloadedProduct);
        Assert.Equal("$149.99", reloadedProduct.CurrentDisplayPrice);

        // Re-running migrations must never have reset any table: exactly the one row per table.
        using var connection = secondFactory.OpenConnection();
        Assert.Equal(1, CountRows(connection, "associate_drafts"));
        Assert.Equal(1, CountRows(connection, "source_state"));
        Assert.Equal(1, CountRows(connection, "dated_results"));
        Assert.Equal(1, CountRows(connection, "catalog_products"));
    }

    // ------------------------------------------------------------------
    // Part E: a historical (pre-Associate-drafts) schema upgrades forward safely, preserving data
    // that already existed under the older schema.
    // ------------------------------------------------------------------

    [Fact]
    public async Task HistoricalSchema_BeforeAssociateDrafts_UpgradesForwardPreservingExistingData()
    {
        var databasePath = Path.Combine(_tempDirectory, "carowinds-passdown.db");
        var connectionFactory = new SqliteConnectionFactory(databasePath);

        // Step 1: apply only the migrations that existed before associate_drafts/manager_drafts
        // (an actual earlier release's schema), using the real migration classes — not a
        // hand-built fake schema.
        IReadOnlyList<IMigration> earlyMigrations =
        [
            new Migration0001CreateSchemaVersionTable(),
            new Migration0002CreateSourceStateTable(),
            new Migration0003CreateDatedResultsTable(),
            new Migration0004CreateCatalogProductsTable(),
            new Migration0005CreateProductHistoryTable(),
        ];
        var earlyVersion = new MigrationRunner(connectionFactory, earlyMigrations, NullLogger<MigrationRunner>.Instance).Run();
        Assert.Equal(5, earlyVersion);

        // Step 2: insert data valid under that older schema, via the real repositories (proving
        // the older schema was actually usable, not just structurally present).
        var operatingDate = new DateOnly(2026, 8, 22);
        var checkedAt = new DateTimeOffset(2026, 8, 22, 6, 0, 0, TimeSpan.Zero);
        await new SqliteSourceStateRepository(connectionFactory).SaveAsync(
            new SourceStateRecord("park-hours", SourceCategory.ParkHours, checkedAt, checkedAt, checkedAt.AddDays(1), "pre-upgrade-fingerprint", 0));

        var evidence = new SourceEvidence(
            sourceName: "Park hours",
            sourceUrl: new Uri("https://www.sixflags.com/carowinds/park-hours"),
            timeChecked: checkedAt,
            status: RetrievalStatus.Verified,
            detail: "Checked successfully.",
            strategy: RetrievalStrategy.Html,
            requestedDate: operatingDate,
            confirmedDate: operatingDate);
        await new SqliteDatedResultRepository(connectionFactory).RecordSuccessAsync(
            new DatedResultKey(SourceCategory.ParkHours, operatingDate), evidence, """{"open":"10:00","close":"22:00"}""");

        // Step 3: a later build runs with the full current migration list — the real upgrade path.
        var upgradedVersion = new MigrationRunner(connectionFactory, AllMigrations, NullLogger<MigrationRunner>.Instance).Run();

        Assert.Equal(CurrentSchemaVersion, upgradedVersion);

        var preservedSourceState = await new SqliteSourceStateRepository(connectionFactory).GetAsync("park-hours");
        Assert.NotNull(preservedSourceState);
        Assert.Equal("pre-upgrade-fingerprint", preservedSourceState.SourceFingerprint);

        var preservedDatedResult = await new SqliteDatedResultRepository(connectionFactory).GetAsync(new DatedResultKey(SourceCategory.ParkHours, operatingDate));
        Assert.NotNull(preservedDatedResult);
        Assert.NotNull(preservedDatedResult.SuccessEvidence);

        // The newly-available table from the upgrade is now usable.
        await new SqliteAssociateDraftRepository(connectionFactory).SaveAsync(
            new AssociateDraft(operatingDate, 1, "{}", checkedAt));
        Assert.NotNull(await new SqliteAssociateDraftRepository(connectionFactory).GetAsync(operatingDate));

        // schema_version history recorded every migration in order, exactly once each.
        using (var connection = connectionFactory.OpenConnection())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT version FROM schema_version ORDER BY version;";
            using var reader = command.ExecuteReader();
            var versions = new List<long>();
            while (reader.Read())
            {
                versions.Add(reader.GetInt64(0));
            }
            Assert.Equal([1L, 2L, 3L, 4L, 5L, 6L, 7L], versions);
        }

        // Re-running the current runner again is idempotent.
        var reappliedVersion = new MigrationRunner(connectionFactory, AllMigrations, NullLogger<MigrationRunner>.Instance).Run();
        Assert.Equal(CurrentSchemaVersion, reappliedVersion);
        using var finalConnection = connectionFactory.OpenConnection();
        Assert.Equal(AllMigrations.Count, CountRows(finalConnection, "schema_version"));
    }

    private static long CountRows(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        return Convert.ToInt64(command.ExecuteScalar());
    }
}
