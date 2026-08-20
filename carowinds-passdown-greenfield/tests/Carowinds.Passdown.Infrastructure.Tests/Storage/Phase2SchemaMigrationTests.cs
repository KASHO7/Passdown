using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage;

/// <summary>
/// Covers the Phase 2B schema (source_state, dated_results, catalog_products,
/// product_history, associate_drafts, manager_drafts) added on top of the Phase 1
/// migration foundation exercised by <see cref="MigrationRunnerTests"/>.
/// </summary>
public class Phase2SchemaMigrationTests : IDisposable
{
    private readonly string _databasePath;

    public Phase2SchemaMigrationTests()
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

    private static IReadOnlyList<IMigration> AllMigrations() =>
    [
        new Migration0001CreateSchemaVersionTable(),
        new Migration0002CreateSourceStateTable(),
        new Migration0003CreateDatedResultsTable(),
        new Migration0004CreateCatalogProductsTable(),
        new Migration0005CreateProductHistoryTable(),
        new Migration0006CreateAssociateDraftsTable(),
        new Migration0007CreateManagerDraftsTable(),
    ];

    private MigrationRunner CreateRunner(IReadOnlyList<IMigration> migrations) =>
        new(new SqliteConnectionFactory(_databasePath), migrations, NullLogger<MigrationRunner>.Instance);

    private SqliteConnection OpenConnection() => new SqliteConnectionFactory(_databasePath).OpenConnection();

    [Fact]
    public void Run_OnFreshDatabase_AppliesAllMigrationsAndAdvancesToLatestVersion()
    {
        var appliedVersion = CreateRunner(AllMigrations()).Run();

        Assert.Equal(7, appliedVersion);
    }

    [Fact]
    public void Run_CalledTwice_IsIdempotent()
    {
        var runner = CreateRunner(AllMigrations());

        var firstRun = runner.Run();
        var secondRun = runner.Run();

        Assert.Equal(7, firstRun);
        Assert.Equal(7, secondRun);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_version;";
        var rowCount = Convert.ToInt64(command.ExecuteScalar());

        Assert.Equal(7, rowCount);
    }

    [Theory]
    [InlineData("source_state")]
    [InlineData("dated_results")]
    [InlineData("catalog_products")]
    [InlineData("product_history")]
    [InlineData("associate_drafts")]
    [InlineData("manager_drafts")]
    public void Run_CreatesExpectedTable(string tableName)
    {
        CreateRunner(AllMigrations()).Run();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);

        Assert.Equal(tableName, command.ExecuteScalar());
    }

    [Theory]
    [InlineData("source_state", "ix_source_state_category")]
    [InlineData("dated_results", "ix_dated_results_requested_date")]
    [InlineData("catalog_products", "ix_catalog_products_category_state")]
    [InlineData("product_history", "ix_product_history_product_key")]
    public void Run_CreatesExpectedIndex(string tableName, string indexName)
    {
        CreateRunner(AllMigrations()).Run();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA index_list(" + tableName + ");";
        using var reader = command.ExecuteReader();

        var indexNames = new List<string>();
        while (reader.Read())
        {
            indexNames.Add(reader.GetString(reader.GetOrdinal("name")));
        }

        Assert.Contains(indexName, indexNames);
    }

    [Fact]
    public void SourceState_DuplicateSourceId_ViolatesPrimaryKey()
    {
        CreateRunner(AllMigrations()).Run();
        using var connection = OpenConnection();

        InsertSourceState(connection, "park-hours");

        Assert.Throws<SqliteException>(() => InsertSourceState(connection, "park-hours"));
    }

    [Fact]
    public void DatedResults_DuplicateCategoryAndDate_ViolatesPrimaryKey()
    {
        CreateRunner(AllMigrations()).Run();
        using var connection = OpenConnection();

        InsertDatedResult(connection, "ParkHours", "2026-10-31");

        Assert.Throws<SqliteException>(() => InsertDatedResult(connection, "ParkHours", "2026-10-31"));
    }

    [Fact]
    public void DatedResults_SameCategoryDifferentDate_IsAllowed()
    {
        CreateRunner(AllMigrations()).Run();
        using var connection = OpenConnection();

        InsertDatedResult(connection, "ParkHours", "2026-10-31");
        InsertDatedResult(connection, "ParkHours", "2026-11-01");

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM dated_results;";
        Assert.Equal(2L, Convert.ToInt64(command.ExecuteScalar()));
    }

    [Fact]
    public void CatalogProducts_DuplicateProductKey_ViolatesPrimaryKey()
    {
        CreateRunner(AllMigrations()).Run();
        using var connection = OpenConnection();

        InsertCatalogProduct(connection, "SeasonPasses:name:season passes|gold pass");

        Assert.Throws<SqliteException>(() => InsertCatalogProduct(connection, "SeasonPasses:name:season passes|gold pass"));
    }

    [Fact]
    public void AssociateDrafts_DuplicateOperatingDate_ViolatesPrimaryKey()
    {
        CreateRunner(AllMigrations()).Run();
        using var connection = OpenConnection();

        InsertAssociateDraft(connection, "2026-10-31");

        Assert.Throws<SqliteException>(() => InsertAssociateDraft(connection, "2026-10-31"));
    }

    [Fact]
    public void ManagerDrafts_DuplicateOperatingDateAndShift_ViolatesPrimaryKey()
    {
        CreateRunner(AllMigrations()).Run();
        using var connection = OpenConnection();

        InsertManagerDraft(connection, "2026-10-31", "day");

        Assert.Throws<SqliteException>(() => InsertManagerDraft(connection, "2026-10-31", "day"));
    }

    [Fact]
    public void ManagerDrafts_SameDateDifferentShift_IsAllowed()
    {
        CreateRunner(AllMigrations()).Run();
        using var connection = OpenConnection();

        InsertManagerDraft(connection, "2026-10-31", "day");
        InsertManagerDraft(connection, "2026-10-31", "night");

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM manager_drafts;";
        Assert.Equal(2L, Convert.ToInt64(command.ExecuteScalar()));
    }

    [Fact]
    public void Run_UpgradingFromPhase1Schema_AppliesOnlyTheNewMigrationsAndPreservesExistingHistory()
    {
        // Simulate a database left over from Phase 1, where only migration 1 had shipped.
        CreateRunner([new Migration0001CreateSchemaVersionTable()]).Run();

        var upgradedVersion = CreateRunner(AllMigrations()).Run();

        Assert.Equal(7, upgradedVersion);

        using var connection = OpenConnection();
        using var versionsCommand = connection.CreateCommand();
        versionsCommand.CommandText = "SELECT version FROM schema_version ORDER BY version;";
        using var reader = versionsCommand.ExecuteReader();
        var versions = new List<long>();
        while (reader.Read())
        {
            versions.Add(reader.GetInt64(0));
        }

        Assert.Equal([1L, 2L, 3L, 4L, 5L, 6L, 7L], versions);

        // The Phase 2 tables must exist alongside the Phase 1 schema_version table.
        using var tableCommand = connection.CreateCommand();
        tableCommand.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'catalog_products';";
        Assert.Equal("catalog_products", tableCommand.ExecuteScalar());
    }

    [Fact]
    public void Run_OnMissingDatabaseFile_CreatesFileAndAppliesAllMigrations()
    {
        Assert.False(File.Exists(_databasePath));

        var appliedVersion = CreateRunner(AllMigrations()).Run();

        Assert.True(File.Exists(_databasePath));
        Assert.Equal(7, appliedVersion);
    }

    [Fact]
    public void Run_OnCorruptedDatabaseFile_FailsLoudlyRatherThanSilentlyMisreportingSchemaState()
    {
        File.WriteAllBytes(_databasePath, "this is not a sqlite database"u8.ToArray());

        Assert.Throws<SqliteException>(() => CreateRunner(AllMigrations()).Run());
    }

    private static void InsertSourceState(SqliteConnection connection, string sourceId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO source_state (source_id, category, consecutive_failure_count) " +
            "VALUES ($sourceId, 'ParkHours', 0);";
        command.Parameters.AddWithValue("$sourceId", sourceId);
        command.ExecuteNonQuery();
    }

    private static void InsertDatedResult(SqliteConnection connection, string category, string requestedDate)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO dated_results (
                category, requested_date,
                last_attempt_status, last_attempt_checked_at_utc, last_attempt_strategy,
                last_attempt_detail, last_attempt_source_name, last_attempt_source_url
            ) VALUES (
                $category, $requestedDate,
                'Verified', '2026-08-20T10:00:00Z', 'Html',
                'Checked successfully.', 'Park hours', 'https://www.sixflags.com/carowinds/park-hours'
            );
            """;
        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue("$requestedDate", requestedDate);
        command.ExecuteNonQuery();
    }

    private static void InsertCatalogProduct(SqliteConnection connection, string productKey)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO catalog_products (
                product_key, category, display_name, lifecycle_state,
                first_seen_at_utc, last_seen_at_utc,
                last_evidence_status, last_evidence_checked_at_utc, last_evidence_strategy,
                last_evidence_source_name, last_evidence_source_url, last_evidence_detail
            ) VALUES (
                $productKey, 'SeasonPasses', 'Gold Pass', 'Active',
                '2026-08-20T10:00:00Z', '2026-08-20T10:00:00Z',
                'Verified', '2026-08-20T10:00:00Z', 'Html',
                'Season passes', 'https://www.sixflags.com/carowinds/passes', 'Checked successfully.'
            );
            """;
        command.Parameters.AddWithValue("$productKey", productKey);
        command.ExecuteNonQuery();
    }

    private static void InsertAssociateDraft(SqliteConnection connection, string operatingDate)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO associate_drafts (operating_date, schema_version, payload_json, updated_at_utc) " +
            "VALUES ($operatingDate, 1, '{}', '2026-08-20T10:00:00Z');";
        command.Parameters.AddWithValue("$operatingDate", operatingDate);
        command.ExecuteNonQuery();
    }

    private static void InsertManagerDraft(SqliteConnection connection, string operatingDate, string shift)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO manager_drafts (operating_date, shift, schema_version, payload_json, updated_at_utc) " +
            "VALUES ($operatingDate, $shift, 1, '{}', '2026-08-20T10:00:00Z');";
        command.Parameters.AddWithValue("$operatingDate", operatingDate);
        command.Parameters.AddWithValue("$shift", shift);
        command.ExecuteNonQuery();
    }
}
