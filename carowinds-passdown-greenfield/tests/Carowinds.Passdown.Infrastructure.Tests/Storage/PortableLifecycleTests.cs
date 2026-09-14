using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.OperationalNotes;
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
        new Migration0008CreateFinalizedPassdownsTable(),
        new Migration0009AddFinalizedPassdownPdfColumns(),
        new Migration0010CreateOperationalNotesTable(),
        new Migration0011CreateDeepRefreshStatusTable(),
        new Migration0012CreateSyncEntityStateTable(),
        new Migration0013CreateSyncConflictsTable(),
        new Migration0014CreateMonthlyErtConfigurationsTable(),
        new Migration0015CreateErtDateOverridesTable(),
    ];

    private const int CurrentSchemaVersion = 15;

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
            Assert.Equal([1L, 2L, 3L, 4L, 5L, 6L, 7L, 8L, 9L, 10L, 11L, 12L, 13L, 14L, 15L], versions);
        }

        // Re-running the current runner again is idempotent.
        var reappliedVersion = new MigrationRunner(connectionFactory, AllMigrations, NullLogger<MigrationRunner>.Instance).Run();
        Assert.Equal(CurrentSchemaVersion, reappliedVersion);
        using var finalConnection = connectionFactory.OpenConnection();
        Assert.Equal(AllMigrations.Count, CountRows(finalConnection, "schema_version"));
    }

    // ------------------------------------------------------------------
    // Phase 9C Part X: schema 8 -> 9 (frozen PDF columns) preserves an existing Phase 9A/9B finalized
    // row, reading back safely with null PDF columns rather than failing or fabricating a PDF.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Schema8ToSchema9_ExistingFinalizedRow_SurvivesWithNullPdfColumns()
    {
        var databasePath = Path.Combine(_tempDirectory, "carowinds-passdown.db");
        var connectionFactory = new SqliteConnectionFactory(databasePath);

        IReadOnlyList<IMigration> schema8Migrations =
        [
            new Migration0001CreateSchemaVersionTable(),
            new Migration0002CreateSourceStateTable(),
            new Migration0003CreateDatedResultsTable(),
            new Migration0004CreateCatalogProductsTable(),
            new Migration0005CreateProductHistoryTable(),
            new Migration0006CreateAssociateDraftsTable(),
            new Migration0007CreateManagerDraftsTable(),
            new Migration0008CreateFinalizedPassdownsTable(),
        ];
        var schema8Version = new MigrationRunner(connectionFactory, schema8Migrations, NullLogger<MigrationRunner>.Instance).Run();
        Assert.Equal(8, schema8Version);

        // Inserted with a raw statement matching the actual pre-9C 7-column table shape — the
        // *current* SqliteFinalizedPassdownRepository.AddAsync always targets the post-9C columns, so
        // it cannot be used here to simulate a row that predates this migration.
        var passdownDate = new DateOnly(2026, 8, 20);
        using (var seedConnection = connectionFactory.OpenConnection())
        using (var seedCommand = seedConnection.CreateCommand())
        {
            seedCommand.CommandText =
                """
                INSERT INTO finalized_passdowns (id, passdown_date, finalized_at_utc, snapshot_schema_version, snapshot_json, content_hash, origin)
                VALUES ($id, $passdownDate, $finalizedAt, $schemaVersion, $snapshotJson, $contentHash, $origin);
                """;
            seedCommand.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            seedCommand.Parameters.AddWithValue("$passdownDate", passdownDate.ToString("yyyy-MM-dd"));
            seedCommand.Parameters.AddWithValue("$finalizedAt", new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero).ToString("O"));
            seedCommand.Parameters.AddWithValue("$schemaVersion", FinalizedPassdownSnapshot.CurrentSchemaVersion);
            seedCommand.Parameters.AddWithValue("$snapshotJson", "{}");
            seedCommand.Parameters.AddWithValue("$contentHash", "pre-9c-hash");
            seedCommand.Parameters.AddWithValue("$origin", FinalizationOrigin.Scheduled.ToString());
            seedCommand.ExecuteNonQuery();
        }

        var upgradedVersion = new MigrationRunner(connectionFactory, AllMigrations, NullLogger<MigrationRunner>.Instance).Run();
        Assert.Equal(CurrentSchemaVersion, upgradedVersion);

        var reloaded = await new SqliteFinalizedPassdownRepository(connectionFactory).GetByDateAsync(passdownDate);
        Assert.NotNull(reloaded);
        Assert.False(reloaded!.HasFrozenPdf);
        Assert.Null(reloaded.PdfBytes);
        Assert.Equal("pre-9c-hash", reloaded.ContentHash);
    }

    // ------------------------------------------------------------------
    // Phase 11A Part AC: schema 9 -> 10 (operational_notes table) is purely additive — it creates the
    // new table/index and leaves every existing Phase 9 table (and its data) untouched.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Schema9ToSchema10_CreatesOperationalNotesTable_AndPreservesExistingFinalizedAndDraftData()
    {
        var databasePath = Path.Combine(_tempDirectory, "carowinds-passdown.db");
        var connectionFactory = new SqliteConnectionFactory(databasePath);

        IReadOnlyList<IMigration> schema9Migrations =
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
        var schema9Version = new MigrationRunner(connectionFactory, schema9Migrations, NullLogger<MigrationRunner>.Instance).Run();
        Assert.Equal(9, schema9Version);

        var operatingDate = new DateOnly(2026, 8, 22);
        var checkedAt = new DateTimeOffset(2026, 8, 22, 6, 0, 0, TimeSpan.Zero);
        await new SqliteAssociateDraftRepository(connectionFactory).SaveAsync(
            new AssociateDraft(operatingDate, 1, """{"notes":"pre-existing draft"}""", checkedAt));

        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        await new SqliteFinalizedPassdownRepository(connectionFactory).AddAsync(new Application.Persistence.FinalizedPassdown(
            Guid.NewGuid(), operatingDate, checkedAt, FinalizedPassdownSnapshot.CurrentSchemaVersion,
            "{}", "pre-existing-hash", FinalizationOrigin.Scheduled, pdfBytes, "deadbeef", pdfBytes.LongLength));

        var upgradedVersion = new MigrationRunner(connectionFactory, AllMigrations, NullLogger<MigrationRunner>.Instance).Run();
        Assert.Equal(CurrentSchemaVersion, upgradedVersion);

        // The new table exists, is empty, and is immediately usable through the real repository.
        var noteRepository = new SqliteOperationalNoteRepository(connectionFactory);
        Assert.Empty(await noteRepository.ListAsync());
        var note = new OperationalNote(
            Guid.NewGuid(), "Test note", null, OperationalNoteCategory.General, OperationalNotePriority.Normal,
            operatingDate, null, OperationalNoteStatus.Active, checkedAt, checkedAt, null);
        await noteRepository.AddAsync(note);
        Assert.NotNull(await noteRepository.GetByIdAsync(note.Id));

        // Required columns/index exist with the expected shape.
        using (var connection = connectionFactory.OpenConnection())
        {
            using var tableInfo = connection.CreateCommand();
            tableInfo.CommandText = "PRAGMA table_info(operational_notes);";
            using var reader = tableInfo.ExecuteReader();
            var columnNames = new List<string>();
            while (reader.Read())
            {
                columnNames.Add(reader.GetString(1));
            }
            Assert.Equal(
                ["id", "title", "body", "category", "priority", "effective_from", "effective_through", "status", "created_at_utc", "updated_at_utc", "resolved_at_utc"],
                columnNames);

            using var indexList = connection.CreateCommand();
            indexList.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'operational_notes';";
            using var indexReader = indexList.ExecuteReader();
            var indexNames = new List<string>();
            while (indexReader.Read())
            {
                indexNames.Add(indexReader.GetString(0));
            }
            Assert.Contains("ix_operational_notes_status_effective_from", indexNames);
        }

        // Existing Phase 9 data is untouched by this purely additive migration.
        var reloadedDraft = await new SqliteAssociateDraftRepository(connectionFactory).GetAsync(operatingDate);
        Assert.NotNull(reloadedDraft);
        Assert.Equal("""{"notes":"pre-existing draft"}""", reloadedDraft.PayloadJson);

        var reloadedFinalized = await new SqliteFinalizedPassdownRepository(connectionFactory).GetByDateAsync(operatingDate);
        Assert.NotNull(reloadedFinalized);
        Assert.Equal("pre-existing-hash", reloadedFinalized.ContentHash);
        Assert.Equal(pdfBytes, reloadedFinalized.PdfBytes);

        using var finalConnection = connectionFactory.OpenConnection();
        Assert.Equal(1, CountRows(finalConnection, "associate_drafts"));
        Assert.Equal(1, CountRows(finalConnection, "finalized_passdowns"));
        Assert.Equal(1, CountRows(finalConnection, "operational_notes"));
    }

    // ------------------------------------------------------------------
    // Phase 16C: schema 11 -> 12 (sync_entity_state table) is purely additive — it creates the new
    // generic sync metadata table and leaves every existing table (drafts, notes, History) untouched.
    // Personal Sync itself remains disabled unless it was already explicitly configured (Phase 16B's
    // own SyncConfiguration.Disabled default) — no sync_entity_state row is ever fabricated by the
    // migration itself, only by an actual local save going through Application.PersonalSync.ISyncChangeTracker.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Schema11ToSchema12_CreatesSyncEntityStateTable_AndPreservesExistingDraftNoteAndHistoryData()
    {
        var databasePath = Path.Combine(_tempDirectory, "carowinds-passdown.db");
        var connectionFactory = new SqliteConnectionFactory(databasePath);

        IReadOnlyList<IMigration> schema11Migrations =
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
        ];
        var schema11Version = new MigrationRunner(connectionFactory, schema11Migrations, NullLogger<MigrationRunner>.Instance).Run();
        Assert.Equal(11, schema11Version);

        var operatingDate = new DateOnly(2026, 8, 22);
        var checkedAt = new DateTimeOffset(2026, 8, 22, 6, 0, 0, TimeSpan.Zero);
        await new SqliteAssociateDraftRepository(connectionFactory).SaveAsync(
            new AssociateDraft(operatingDate, 3, """{"notes":"pre-16C draft"}""", checkedAt));

        var note = new OperationalNote(
            Guid.NewGuid(), "Pre-16C note", null, OperationalNoteCategory.General, OperationalNotePriority.Normal,
            operatingDate, null, OperationalNoteStatus.Active, checkedAt, checkedAt, null);
        await new SqliteOperationalNoteRepository(connectionFactory).AddAsync(note);

        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        await new SqliteFinalizedPassdownRepository(connectionFactory).AddAsync(new Application.Persistence.FinalizedPassdown(
            Guid.NewGuid(), operatingDate, checkedAt, FinalizedPassdownSnapshot.CurrentSchemaVersion,
            "{}", "pre-16c-hash", FinalizationOrigin.Scheduled, pdfBytes, "deadbeef", pdfBytes.LongLength));

        var upgradedVersion = new MigrationRunner(connectionFactory, AllMigrations, NullLogger<MigrationRunner>.Instance).Run();
        Assert.Equal(CurrentSchemaVersion, upgradedVersion);

        // The new table exists, is empty (no sync metadata is fabricated by the migration itself),
        // and is immediately usable through the real repository.
        var stateRepository = new SqliteSyncEntityStateRepository(connectionFactory);
        Assert.Empty(await stateRepository.ListByTypeAsync(Application.PersonalSync.SyncEntityType.AssociateDraft));
        Assert.Empty(await stateRepository.ListByTypeAsync(Application.PersonalSync.SyncEntityType.OperationalNote));

        var state = Application.PersonalSync.SyncEntityState.CreateNew(Application.PersonalSync.SyncEntityType.AssociateDraft, "2026-08-22", checkedAt);
        await stateRepository.UpsertAsync(state);
        Assert.Equal(state, await stateRepository.GetAsync(Application.PersonalSync.SyncEntityType.AssociateDraft, "2026-08-22"));

        // Required columns exist with the expected shape.
        using (var connection = connectionFactory.OpenConnection())
        {
            using var tableInfo = connection.CreateCommand();
            tableInfo.CommandText = "PRAGMA table_info(sync_entity_state);";
            using var reader = tableInfo.ExecuteReader();
            var columnNames = new List<string>();
            while (reader.Read())
            {
                columnNames.Add(reader.GetString(1));
            }
            Assert.Equal(
                ["entity_type", "entity_id", "local_revision", "synced_revision", "synced_content_hash",
                 "last_remote_device_id", "last_remote_revision", "conflict_state", "conflict_remote_device_id",
                 "conflict_remote_revision", "conflict_remote_content_hash", "conflict_detected_at_utc", "updated_at_utc"],
                columnNames);
        }

        // Existing pre-16C data is untouched by this purely additive migration.
        var reloadedDraft = await new SqliteAssociateDraftRepository(connectionFactory).GetAsync(operatingDate);
        Assert.NotNull(reloadedDraft);
        Assert.Equal("""{"notes":"pre-16C draft"}""", reloadedDraft.PayloadJson);

        var reloadedNote = await new SqliteOperationalNoteRepository(connectionFactory).GetByIdAsync(note.Id);
        Assert.NotNull(reloadedNote);
        Assert.Equal("Pre-16C note", reloadedNote.Title);

        var reloadedFinalized = await new SqliteFinalizedPassdownRepository(connectionFactory).GetByDateAsync(operatingDate);
        Assert.NotNull(reloadedFinalized);
        Assert.Equal("pre-16c-hash", reloadedFinalized.ContentHash);

        using var finalConnection = connectionFactory.OpenConnection();
        Assert.Equal(1, CountRows(finalConnection, "associate_drafts"));
        Assert.Equal(1, CountRows(finalConnection, "operational_notes"));
        Assert.Equal(1, CountRows(finalConnection, "finalized_passdowns"));
        Assert.Equal(1, CountRows(finalConnection, "sync_entity_state"));
    }

    [Fact]
    public async Task Schema12ToSchema13_CreatesSyncConflictsTable_AndPreservesExistingDraftNoteHistoryAndSyncEntityStateData()
    {
        var databasePath = Path.Combine(_tempDirectory, "carowinds-passdown.db");
        var connectionFactory = new SqliteConnectionFactory(databasePath);

        IReadOnlyList<IMigration> schema12Migrations =
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
        ];
        var schema12Version = new MigrationRunner(connectionFactory, schema12Migrations, NullLogger<MigrationRunner>.Instance).Run();
        Assert.Equal(12, schema12Version);

        var operatingDate = new DateOnly(2026, 9, 1);
        var checkedAt = new DateTimeOffset(2026, 9, 1, 6, 0, 0, TimeSpan.Zero);
        await new SqliteAssociateDraftRepository(connectionFactory).SaveAsync(
            new AssociateDraft(operatingDate, 3, """{"notes":"pre-16D draft"}""", checkedAt));

        var note = new OperationalNote(
            Guid.NewGuid(), "Pre-16D note", null, OperationalNoteCategory.General, OperationalNotePriority.Normal,
            operatingDate, null, OperationalNoteStatus.Active, checkedAt, checkedAt, null);
        await new SqliteOperationalNoteRepository(connectionFactory).AddAsync(note);

        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        await new SqliteFinalizedPassdownRepository(connectionFactory).AddAsync(new Application.Persistence.FinalizedPassdown(
            Guid.NewGuid(), operatingDate, checkedAt, FinalizedPassdownSnapshot.CurrentSchemaVersion,
            "{}", "pre-16d-hash", FinalizationOrigin.Scheduled, pdfBytes, "deadbeef", pdfBytes.LongLength));

        var stateRepositoryBeforeUpgrade = new SqliteSyncEntityStateRepository(connectionFactory);
        var preExistingState = Application.PersonalSync.SyncEntityState.CreateNew(Application.PersonalSync.SyncEntityType.AssociateDraft, "2026-09-01", checkedAt) with { LocalRevision = 3, SyncedRevision = 3, SyncedContentHash = "pre-16d-hash" };
        await stateRepositoryBeforeUpgrade.UpsertAsync(preExistingState);

        var upgradedVersion = new MigrationRunner(connectionFactory, AllMigrations, NullLogger<MigrationRunner>.Instance).Run();
        Assert.Equal(CurrentSchemaVersion, upgradedVersion);

        // The new table exists, is empty (no conflict is fabricated by the migration itself), and is
        // immediately usable through the real repository.
        var conflictRepository = new SqliteSyncConflictRepository(connectionFactory);
        Assert.Empty(await conflictRepository.ListAsync());

        var conflictRecord = new Application.PersonalSync.SyncConflictRecord(
            Application.PersonalSync.SyncEntityType.AssociateDraft, "2026-09-01", Guid.NewGuid(), 2, checkedAt, false,
            3, """{"notes":"remote"}""", "remote-hash", checkedAt, null, null, null);
        await conflictRepository.UpsertAsync(conflictRecord);
        Assert.Equal(conflictRecord, await conflictRepository.GetAsync(Application.PersonalSync.SyncEntityType.AssociateDraft, "2026-09-01"));

        // Required columns exist with the expected shape.
        using (var connection = connectionFactory.OpenConnection())
        {
            using var tableInfo = connection.CreateCommand();
            tableInfo.CommandText = "PRAGMA table_info(sync_conflicts);";
            using var reader = tableInfo.ExecuteReader();
            var columnNames = new List<string>();
            while (reader.Read())
            {
                columnNames.Add(reader.GetString(1));
            }
            Assert.Equal(
                ["entity_type", "entity_id", "remote_device_id", "remote_revision", "remote_modified_at_utc",
                 "remote_is_deleted", "remote_payload_schema_version", "remote_payload_json", "remote_content_hash",
                 "detected_at_utc", "newer_remote_device_id", "newer_remote_revision", "newer_remote_detected_at_utc"],
                columnNames);
        }

        // Existing pre-16D data — drafts, notes, history, and sync_entity_state — is untouched by this
        // purely additive migration.
        var reloadedDraft = await new SqliteAssociateDraftRepository(connectionFactory).GetAsync(operatingDate);
        Assert.NotNull(reloadedDraft);
        Assert.Equal("""{"notes":"pre-16D draft"}""", reloadedDraft.PayloadJson);

        var reloadedNote = await new SqliteOperationalNoteRepository(connectionFactory).GetByIdAsync(note.Id);
        Assert.NotNull(reloadedNote);
        Assert.Equal("Pre-16D note", reloadedNote.Title);

        var reloadedFinalized = await new SqliteFinalizedPassdownRepository(connectionFactory).GetByDateAsync(operatingDate);
        Assert.NotNull(reloadedFinalized);
        Assert.Equal("pre-16d-hash", reloadedFinalized.ContentHash);

        var reloadedState = await new SqliteSyncEntityStateRepository(connectionFactory).GetAsync(Application.PersonalSync.SyncEntityType.AssociateDraft, "2026-09-01");
        Assert.NotNull(reloadedState);
        Assert.Equal(3, reloadedState.LocalRevision);

        using var finalConnection13 = connectionFactory.OpenConnection();
        Assert.Equal(1, CountRows(finalConnection13, "associate_drafts"));
        Assert.Equal(1, CountRows(finalConnection13, "operational_notes"));
        Assert.Equal(1, CountRows(finalConnection13, "finalized_passdowns"));
        Assert.Equal(1, CountRows(finalConnection13, "sync_entity_state"));
        Assert.Equal(1, CountRows(finalConnection13, "sync_conflicts"));
    }

    // Phase 17B: two purely additive migrations (0014/0015) — no existing table/column is touched, no
    // monthly ERT record is fabricated from prior park-hours data (spec Part 34).
    [Fact]
    public async Task Schema13ToSchema15_CreatesMonthlyErtAndOverrideTables_AndFabricatesNothing()
    {
        var databasePath = Path.Combine(_tempDirectory, "carowinds-passdown.db");
        var connectionFactory = new SqliteConnectionFactory(databasePath);

        IReadOnlyList<IMigration> schema13Migrations =
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
        ];
        var schema13Version = new MigrationRunner(connectionFactory, schema13Migrations, NullLogger<MigrationRunner>.Instance).Run();
        Assert.Equal(13, schema13Version);

        var operatingDate = new DateOnly(2026, 9, 1);
        var checkedAt = new DateTimeOffset(2026, 9, 1, 6, 0, 0, TimeSpan.Zero);
        await new SqliteAssociateDraftRepository(connectionFactory).SaveAsync(
            new AssociateDraft(operatingDate, 4, """{"notes":"pre-17B draft"}""", checkedAt));

        var upgradedVersion = new MigrationRunner(connectionFactory, AllMigrations, NullLogger<MigrationRunner>.Instance).Run();
        Assert.Equal(CurrentSchemaVersion, upgradedVersion);

        // Spec Part 34: existing users get NotConfigured for every month — nothing is backfilled from
        // old park-hours-derived behavior. Both new tables start empty.
        var monthlyRepository = new SqliteMonthlyErtRepository(connectionFactory);
        var overrideRepository = new SqliteErtDateOverrideRepository(connectionFactory);
        Assert.Empty(await monthlyRepository.ListAsync());
        Assert.Empty(await overrideRepository.ListAsync());
        Assert.Null(await monthlyRepository.GetAsync(operatingDate.Year, operatingDate.Month));

        // Both new tables are immediately usable through their real repositories. (Field-by-field —
        // IReadOnlyList<string> falls back to reference equality on the record's own Equals, exactly
        // like AssociateDraftPayload's Assert.Equal(expected, actual) never comparing lists directly.)
        var configuration = new Application.Ert.MonthlyErtConfiguration(
            2026, 9, Domain.Ert.ErtConfigurationState.Configured, new TimeOnly(9, 30), ["Fury 325"], null, checkedAt);
        await monthlyRepository.UpsertAsync(configuration);
        var reloadedConfiguration = await monthlyRepository.GetAsync(2026, 9);
        Assert.Equal(configuration.StartTime, reloadedConfiguration!.StartTime);
        Assert.Equal(configuration.Rides, reloadedConfiguration.Rides);

        var dateOverride = new Application.Ert.ErtDateOverride(
            operatingDate, Domain.Ert.ErtConfigurationState.Configured, new TimeOnly(10, 0), ["Copperhead Strike"], "Fury maintenance", checkedAt);
        await overrideRepository.UpsertAsync(dateOverride);
        var reloadedOverride = await overrideRepository.GetAsync(operatingDate);
        Assert.Equal(dateOverride.StartTime, reloadedOverride!.StartTime);
        Assert.Equal(dateOverride.Rides, reloadedOverride.Rides);

        // Existing pre-17B draft data is untouched by this purely additive migration.
        var reloadedDraft = await new SqliteAssociateDraftRepository(connectionFactory).GetAsync(operatingDate);
        Assert.NotNull(reloadedDraft);
        Assert.Equal("""{"notes":"pre-17B draft"}""", reloadedDraft.PayloadJson);

        using var finalConnection = connectionFactory.OpenConnection();
        Assert.Equal(1, CountRows(finalConnection, "associate_drafts"));
        Assert.Equal(1, CountRows(finalConnection, "monthly_ert_configurations"));
        Assert.Equal(1, CountRows(finalConnection, "ert_date_overrides"));
    }

    private static long CountRows(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        return Convert.ToInt64(command.ExecuteScalar());
    }
}
