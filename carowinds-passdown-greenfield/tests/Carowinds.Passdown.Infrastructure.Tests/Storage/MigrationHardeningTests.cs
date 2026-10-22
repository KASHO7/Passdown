using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage;

/// <summary>
/// Phase 18A (docs/phase18-comprehensive-audit.md §8, REL-001): upgrade-path matrix from several
/// historical schema versions straight to the current one, failure atomicity of a single migration,
/// and the interactive-app vs --scheduled-refresh concurrent-migration race.
/// </summary>
public class MigrationHardeningTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-migration-18a-{Guid.NewGuid():N}");
    private string DatabasePath => Path.Combine(_tempDirectory, "carowinds-passdown.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private static IMigration[] AllMigrations() =>
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
        new Migration0010CreateOperationalNotesTable(),
        new Migration0011CreateDeepRefreshStatusTable(),
        new Migration0012CreateSyncEntityStateTable(),
        new Migration0013CreateSyncConflictsTable(),
        new Migration0014CreateMonthlyErtConfigurationsTable(),
        new Migration0015CreateErtDateOverridesTable(),
        new Migration0016CreateSeasonPriceHistoryTable(),
        new Migration0017CreateSyncedPriceObservationsTable(),
    ];

    private const int CurrentSchemaVersion = 17;

    private int RunMigrations(IReadOnlyList<IMigration> migrations) =>
        new MigrationRunner(new SqliteConnectionFactory(DatabasePath), migrations, NullLogger<MigrationRunner>.Instance).Run();

    private void Execute(string sql)
    {
        using var connection = new SqliteConnectionFactory(DatabasePath).OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private long Scalar(string sql)
    {
        using var connection = new SqliteConnectionFactory(DatabasePath).OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(11)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    public async Task HistoricalSchema_UpgradesStraightToCurrent_PreservingDataAndFabricatingNothing(int startVersion)
    {
        var all = AllMigrations();
        if (startVersion > 0)
        {
            Assert.Equal(startVersion, RunMigrations(all.Take(startVersion).ToArray()));

            // A representative user-authored row that existed at every one of these start versions.
            Execute("""
                INSERT INTO associate_drafts (operating_date, schema_version, payload_json, updated_at_utc)
                VALUES ('2026-07-04', 3, '{"ProjectedAttendance":31337,"OperationalNotes":[]}', '2026-07-04T10:00:00.0000000+00:00');
                """);
        }

        Assert.Equal(CurrentSchemaVersion, RunMigrations(all));

        Assert.Equal(CurrentSchemaVersion, Scalar("PRAGMA user_version;"));
        Assert.Equal(CurrentSchemaVersion, Scalar("SELECT COUNT(*) FROM schema_version;"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM monthly_ert_configurations;"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM ert_date_overrides;"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM sync_entity_state;"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM finalized_passdowns;"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM season_price_history;")); // nothing to backfill from, nothing fabricated

        var draft = await new SqliteAssociateDraftRepository(new SqliteConnectionFactory(DatabasePath)).GetAsync(new DateOnly(2026, 7, 4));
        if (startVersion > 0)
        {
            Assert.NotNull(draft);
            Assert.Contains("31337", draft.PayloadJson);
        }
        else
        {
            Assert.Null(draft);
        }
    }

    [Fact]
    public void FailingMigration_RollsBackOnlyThatStep_AndARetrySucceeds()
    {
        var all = AllMigrations();
        Assert.Equal(13, RunMigrations(all.Take(13).ToArray()));
        Execute("INSERT INTO associate_drafts (operating_date, schema_version, payload_json, updated_at_utc) VALUES ('2026-07-04', 4, '{}', '2026-07-04T10:00:00.0000000+00:00');");

        // Migration 14 partially applies (creates its table), then fails before commit.
        var failing = all.Take(13).Append(new FailAfterApplyMigration(all[13])).ToArray();
        Assert.Throws<InvalidOperationException>(() => RunMigrations(failing));

        Assert.Equal(13, Scalar("PRAGMA user_version;"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'monthly_ert_configurations';"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM associate_drafts;"));

        Assert.Equal(CurrentSchemaVersion, RunMigrations(all));
    }

    /// <summary>
    /// REL-001: runner A reads user_version = 8, then — before A starts migration 9 — runner B (the
    /// other process) migrates the same file all the way to the current version. Pre-18A, A then
    /// re-applied Migration0009's non-idempotent ALTER TABLE ADD COLUMN and threw "duplicate column
    /// name", aborting startup of whichever process lost the race.
    /// </summary>
    [Fact]
    public void ConcurrentRunner_CommitsAheadOfThisOne_IsNeverReapplied()
    {
        var all = AllMigrations();
        var otherProcessRan = false;
        var armed = false;

        var raceMigrations = all.ToArray();
        raceMigrations[7] = new CallbackOnApplyMigration(all[7], () => armed = true);
        raceMigrations[8] = new CallbackOnVersionReadMigration(all[8], () =>
        {
            if (armed && !otherProcessRan)
            {
                otherProcessRan = true;
                Assert.Equal(CurrentSchemaVersion, RunMigrations(AllMigrations()));
            }
        });

        var finalVersion = RunMigrations(raceMigrations);

        Assert.True(otherProcessRan);
        Assert.Equal(CurrentSchemaVersion, finalVersion);
        Assert.Equal(CurrentSchemaVersion, Scalar("PRAGMA user_version;"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM pragma_table_info('finalized_passdowns') WHERE name = 'pdf_bytes';"));
    }

    private sealed class FailAfterApplyMigration(IMigration inner) : IMigration
    {
        public int Version => inner.Version;
        public string Description => inner.Description;

        public void Apply(SqliteConnection connection)
        {
            inner.Apply(connection);
            throw new InvalidOperationException("Simulated failure after a partial migration step.");
        }
    }

    private sealed class CallbackOnApplyMigration(IMigration inner, Action afterApply) : IMigration
    {
        public int Version => inner.Version;
        public string Description => inner.Description;

        public void Apply(SqliteConnection connection)
        {
            inner.Apply(connection);
            afterApply();
        }
    }

    private sealed class CallbackOnVersionReadMigration(IMigration inner, Action onVersionRead) : IMigration
    {
        public int Version
        {
            get
            {
                onVersionRead();
                return inner.Version;
            }
        }

        public string Description => inner.Description;

        public void Apply(SqliteConnection connection) => inner.Apply(connection);
    }
}
