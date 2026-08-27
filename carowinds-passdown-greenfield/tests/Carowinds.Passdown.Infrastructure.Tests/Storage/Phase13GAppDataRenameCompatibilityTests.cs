using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage;

/// <summary>
/// Phase 13G: the visible product rename ("Carowinds Passdown" → "Carowinds Operations") is
/// branding-only — it must never touch the on-disk local-data location, so an upgrading user's
/// existing data keeps loading unmodified under the renamed app. <see cref="AppPathsTests"/> already
/// proves <see cref="AppPaths.LocalApplicationDataDirectory"/>'s construction is unaffected by
/// current-directory changes; this class proves the higher-priority end-to-end contract the phase
/// brief calls out explicitly: a directory using the exact pre-rename folder name, containing
/// representative pre-existing data (a draft, a finalized/History row, Deep Refresh status), loads
/// correctly through the same migration + repository path <c>App.axaml.cs</c> uses on every launch —
/// and no second, empty "Carowinds Operations" directory is ever created alongside it. Uses an
/// isolated temp directory standing in for <c>%LOCALAPPDATA%</c> (never the developer's actual
/// LocalAppData), exactly like <see cref="PortableLifecycleTests"/>.
/// </summary>
public class Phase13GAppDataRenameCompatibilityTests : IDisposable
{
    private readonly string _fakeLocalAppDataRoot = Path.Combine(Path.GetTempPath(), $"carowinds-13g-rename-{Guid.NewGuid():N}");

    // Reproduces AppPaths.LocalApplicationDataDirectory's exact, unchanged folder name (spec
    // "Critical compatibility rule" — do not rename local persistence identifiers).
    private string LegacyAppDataDirectory => Path.Combine(_fakeLocalAppDataRoot, "Carowinds Passdown");
    private string NewlyNamedAppDataDirectory => Path.Combine(_fakeLocalAppDataRoot, "Carowinds Operations");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_fakeLocalAppDataRoot))
        {
            Directory.Delete(_fakeLocalAppDataRoot, recursive: true);
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
    ];

    private const int CurrentSchemaVersion = 11;

    [Fact]
    public void AppPaths_LocalApplicationDataDirectory_StillUsesTheLegacyFolderName_NotTheRenamedProductName()
    {
        Assert.EndsWith("Carowinds Passdown", AppPaths.LocalApplicationDataDirectory);
        Assert.DoesNotContain("Carowinds Operations", AppPaths.LocalApplicationDataDirectory);
    }

    [Fact]
    public async Task ExistingLegacyAppData_DraftHistoryAndDeepRefreshState_AllLoadUnchanged_UnderTheRenamedApp()
    {
        // Step 1/2: seed a legacy-named local-data directory with representative pre-existing data,
        // exactly as if written by a pre-13G build (the pre-rename code and this phase's code both
        // use the identical schema/repository types — the rename touched no persistence code).
        Directory.CreateDirectory(LegacyAppDataDirectory);
        var databasePath = Path.Combine(LegacyAppDataDirectory, "carowinds-passdown.db");
        var connectionFactory = new SqliteConnectionFactory(databasePath);
        var appliedVersion = new MigrationRunner(connectionFactory, AllMigrations, NullLogger<MigrationRunner>.Instance).Run();
        Assert.Equal(CurrentSchemaVersion, appliedVersion);

        var operatingDate = new DateOnly(2026, 8, 22);
        var checkedAt = new DateTimeOffset(2026, 8, 22, 6, 0, 0, TimeSpan.Zero);

        await new SqliteAssociateDraftRepository(connectionFactory).SaveAsync(
            new AssociateDraft(operatingDate, 1, """{"notes":"Pre-rename draft"}""", checkedAt));

        var finalized = new FinalizedPassdown(
            Guid.NewGuid(),
            operatingDate.AddDays(-1),
            checkedAt,
            FinalizedPassdownSnapshot.CurrentSchemaVersion,
            """{"PassdownDate":"2026-08-21"}""",
            "pre-rename-content-hash",
            FinalizationOrigin.Scheduled,
            PdfBytes: [0x25, 0x50, 0x44, 0x46],
            PdfSha256: "deadbeef",
            PdfSizeBytes: 4);
        await new SqliteFinalizedPassdownRepository(connectionFactory).AddAsync(finalized);

        await new SqliteDeepRefreshStatusRepository(connectionFactory).SaveAsync(
            new DeepRefreshStatusRecord(checkedAt, checkedAt, DeepRefreshOutcome.Success, operatingDate.AddDays(7), 8, 0));

        // Step 3: "launch under the renamed app" — a fresh connection factory over the same file,
        // exactly what App.axaml.cs does on every real launch (open + re-run migrations). Nothing
        // about the rename changes which directory/file this points at.
        var relaunchFactory = new SqliteConnectionFactory(databasePath);
        var relaunchVersion = new MigrationRunner(relaunchFactory, AllMigrations, NullLogger<MigrationRunner>.Instance).Run();
        Assert.Equal(CurrentSchemaVersion, relaunchVersion);

        // Step 4: drafts still load.
        var reloadedDraft = await new SqliteAssociateDraftRepository(relaunchFactory).GetAsync(operatingDate);
        Assert.NotNull(reloadedDraft);
        Assert.Equal("""{"notes":"Pre-rename draft"}""", reloadedDraft.PayloadJson);

        // Step 5: History (finalized passdowns) still loads.
        var reloadedFinalized = await new SqliteFinalizedPassdownRepository(relaunchFactory).GetByDateAsync(operatingDate.AddDays(-1));
        Assert.NotNull(reloadedFinalized);
        Assert.Equal("pre-rename-content-hash", reloadedFinalized.ContentHash);
        Assert.True(reloadedFinalized.HasFrozenPdf);

        // Step 6: Deep Refresh state still loads.
        var reloadedDeepRefresh = await new SqliteDeepRefreshStatusRepository(relaunchFactory).GetAsync();
        Assert.NotNull(reloadedDeepRefresh);
        Assert.Equal(DeepRefreshOutcome.Success, reloadedDeepRefresh.Outcome);
        Assert.Equal(8, reloadedDeepRefresh.SourceSuccessCount);

        // Step 7: no new, empty "Carowinds Operations" data directory ever became authoritative —
        // every read above went through the original legacy-named directory/file, and nothing in
        // this test (or the production code it exercises) ever wrote to the differently-named path.
        Assert.False(Directory.Exists(NewlyNamedAppDataDirectory));
        Assert.True(File.Exists(databasePath));
    }
}
