using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.History;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Carowinds.Passdown.Ui.Tests.TestSupport;
using UglyToad.PdfPig;

namespace Carowinds.Passdown.Ui.Tests.ScheduledRefresh;

/// <summary>
/// Phase 11D "central guarantee" end-to-end scenario (spec Part AZ): a real temp-file SQLite
/// database, real migrations (through <see cref="Migration0010CreateOperationalNotesTable"/>), and
/// the real <see cref="AssociatePassdownService"/>/<see cref="OperationalNotesService"/>/
/// <see cref="FinalizedPassdownFinalizationService"/>/<see cref="FinalizedPassdownSnapshotBuilder"/>/
/// <see cref="PassdownHistoryService"/> pipeline — no fakes for the pieces this scenario proves.
/// Demonstrates that a persistent note selected for one date freezes at selection time, survives a
/// source edit through reload/PDF/finalization, and that History remains immutable afterward while
/// the live source note reflects its current (edited) content.
/// </summary>
public class Phase11DIntegrationTests
{
    private sealed class ThrowingRefreshOrchestrator : IRefreshOrchestrator
    {
        public Task<RefreshSummary> RunScheduledRefreshAsync(IProgress<RefreshProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This scenario is entirely local-only.");

        public Task<RefreshSummary> RunUserForcedRefreshAsync(DateOnly date, IProgress<RefreshProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This scenario is entirely local-only.");

        public Task<RefreshSummary> RunDeepRefreshAsync(bool bypassFreshness, DateOnly? focusDate = null, IProgress<RefreshProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This scenario is entirely local-only.");
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
    ];

    private static string NewTempDb() => Path.Combine(Path.GetTempPath(), $"carowinds-passdown-11d-{Guid.NewGuid():N}.db");

    private static string ExtractText(byte[] pdfBytes)
    {
        using var document = PdfDocument.Open(pdfBytes);
        return string.Join("\n", document.GetPages().Select(page => page.Text));
    }

    [Fact]
    public async Task SelectedNoteContent_SurvivesSourceEdit_ThroughReload_Pdf_AndFinalization()
    {
        var databasePath = NewTempDb();
        try
        {
            var connectionFactory = new SqliteConnectionFactory(databasePath);
            new MigrationRunner(connectionFactory, AllMigrations, Microsoft.Extensions.Logging.Abstractions.NullLogger<MigrationRunner>.Instance).Run();

            var drafts = new SqliteAssociateDraftRepository(connectionFactory);
            var datedResults = new SqliteDatedResultRepository(connectionFactory);
            var catalogProducts = new SqliteCatalogProductRepository(connectionFactory);
            var operationalNoteRepository = new SqliteOperationalNoteRepository(connectionFactory);
            var finalizedPassdowns = new SqliteFinalizedPassdownRepository(connectionFactory);

            var clock = new FakeClock(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero));
            var operationalNotesService = new OperationalNotesService(operationalNoteRepository, clock);
            var associateService = new AssociatePassdownService(
                drafts, datedResults, catalogProducts, new ThrowingRefreshOrchestrator(), operationalNotesService, clock);
            var snapshotBuilder = new FinalizedPassdownSnapshotBuilder();
            var finalizationService = new FinalizedPassdownFinalizationService(finalizedPassdowns, snapshotBuilder);
            var historyService = new PassdownHistoryService(finalizedPassdowns);

            var passdownDate = new DateOnly(2026, 8, 26);

            // 1. Create a persistent note.
            var note = await operationalNotesService.CreateAsync(new OperationalNoteDraft(
                "South Gate scanner #3 out of service", "Direct guests to gate 2.",
                OperationalNoteCategory.Equipment, OperationalNotePriority.Normal, passdownDate.AddDays(-6), null));

            // 2. Load/save the Aug 26 Associate draft.
            await associateService.SaveDraftAsync(passdownDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 12000 });

            // 3. Select the persistent note for Aug 26.
            var afterSelection = await associateService.SelectOperationalNoteAsync(passdownDate, note.Id);

            // 4. Verify the frozen copy persisted.
            var selection = Assert.Single(afterSelection.Draft.SelectedOperationalNotes);
            Assert.Equal("South Gate scanner #3 out of service", selection.Title);

            // 5. Edit the source persistent note.
            await operationalNotesService.UpdateAsync(note.Id, new OperationalNoteDraft(
                "Scanner repaired; use normally", null, OperationalNoteCategory.Equipment, OperationalNotePriority.Normal, passdownDate.AddDays(-6), null));

            // 6. Reload the Aug 26 draft.
            var reloaded = await associateService.LoadAssociateDateAsync(passdownDate);

            // 7. The selected copy still says the old text.
            var reloadedSelection = Assert.Single(reloaded.Draft.SelectedOperationalNotes);
            Assert.Equal("South Gate scanner #3 out of service", reloadedSelection.Title);

            // 8. Generate the current PDF.
            var model = AssociatePassdownDocumentModelBuilder.Build(reloaded, clock.UtcNow);
            var pdfResult = new AssociatePassdownPdfGenerator().Generate(model);

            // 9. The old frozen text appears in the current PDF; the edited text does not.
            var pdfText = ExtractText(pdfResult.PdfBytes!);
            Assert.Contains("South Gate scanner #3 out of service", pdfText);
            Assert.DoesNotContain("Scanner repaired", pdfText);

            // 10. Finalize Aug 26.
            var finalization = await finalizationService.FinalizeAsync(
                passdownDate, reloaded, FinalizationOrigin.Manual, clock.UtcNow.AddMinutes(1));
            Assert.Equal(FinalizationOutcome.Created, finalization.Outcome);

            // 11. History's snapshot and frozen PDF contain the old frozen text.
            var historyDetail = await historyService.GetAsync(passdownDate);
            Assert.NotNull(historyDetail?.Snapshot);
            Assert.Contains(historyDetail!.Snapshot!.OperationalNotes, line => line.Contains("South Gate scanner #3 out of service", StringComparison.Ordinal));

            var historyPdfText = ExtractText(finalization.FinalizedPassdown.PdfBytes!);
            Assert.Contains("South Gate scanner #3 out of service", historyPdfText);
            Assert.DoesNotContain("Scanner repaired", historyPdfText);

            // 12. The current persistent source note reflects the edit.
            var currentSource = await operationalNotesService.ListAsync();
            Assert.Equal("Scanner repaired; use normally", Assert.Single(currentSource).Title);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }
}
