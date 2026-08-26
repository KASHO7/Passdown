using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Desktop.ScheduledRefresh;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Carowinds.Passdown.Ui.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Ui.Tests.ScheduledRefresh;

/// <summary>
/// Phase 9B "FULL VALIDATION" deterministic integration scenario: a real temp-file SQLite database,
/// real migrations, real repositories, and the real <see cref="ScheduledPassdownFinalizer"/>/
/// <see cref="FinalizedPassdownFinalizationService"/>/<see cref="FinalizedPassdownSnapshotBuilder"/>
/// pipeline — wired through the same <see cref="ScheduledRefreshHost"/> a real
/// <c>--scheduled-refresh</c> invocation runs. No live network: <see cref="ThrowingRefreshOrchestrator"/>
/// proves finalization itself never reaches a refresh/retrieval abstraction, and the scheduled
/// refresh step afterward uses a scripted <see cref="FakeScheduledRefreshRunner"/> rather than any
/// real source adapter.
/// </summary>
public class ScheduledRefreshFinalizationIntegrationTests
{
    private static readonly DateOnly PassdownDate = new(2026, 8, 25);

    private sealed class ThrowingRefreshOrchestrator : IRefreshOrchestrator
    {
        public Task<RefreshSummary> RunScheduledRefreshAsync(IProgress<RefreshProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Finalization must never trigger a refresh.");

        public Task<RefreshSummary> RunManualDateRefreshAsync(DateOnly date, IProgress<RefreshProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Finalization must never trigger a refresh.");
    }

    private static IServiceProvider BuildProvider(string databasePath, FakeClock clock, FakeScheduledRefreshRunner refreshRunner)
    {
        var connectionFactory = new SqliteConnectionFactory(databasePath);
        var drafts = new SqliteAssociateDraftRepository(connectionFactory);
        var datedResults = new SqliteDatedResultRepository(connectionFactory);
        var catalogProducts = new SqliteCatalogProductRepository(connectionFactory);
        var finalizedPassdowns = new SqliteFinalizedPassdownRepository(connectionFactory);

        var associateService = new AssociatePassdownService(
            drafts, datedResults, catalogProducts, new ThrowingRefreshOrchestrator(),
            new OperationalNotesService(new InMemoryOperationalNoteRepository(), clock), clock);
        var snapshotBuilder = new FinalizedPassdownSnapshotBuilder();
        var finalizationService = new FinalizedPassdownFinalizationService(finalizedPassdowns, snapshotBuilder);
        var finalizer = new ScheduledPassdownFinalizer(
            drafts, associateService, finalizedPassdowns, finalizationService, clock, NullLogger<ScheduledPassdownFinalizer>.Instance);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(connectionFactory);
        services.AddSingleton<IReadOnlyList<IMigration>>([
            new Migration0001CreateSchemaVersionTable(),
            new Migration0002CreateSourceStateTable(),
            new Migration0003CreateDatedResultsTable(),
            new Migration0004CreateCatalogProductsTable(),
            new Migration0005CreateProductHistoryTable(),
            new Migration0006CreateAssociateDraftsTable(),
            new Migration0007CreateManagerDraftsTable(),
            new Migration0008CreateFinalizedPassdownsTable(),
            new Migration0009AddFinalizedPassdownPdfColumns(),
        ]);
        services.AddSingleton<MigrationRunner>();
        services.AddSingleton<IScheduledPassdownFinalizer>(finalizer);
        services.AddSingleton<IScheduledRefreshRunner>(refreshRunner);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ScheduledPipeline_SavedDraftPastCutoff_FinalizesThenRefreshes()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-9b-integration-{Guid.NewGuid():N}.db");
        try
        {
            // 06:01 AM Eastern on 2026-08-26 (EDT, UTC-4) — one minute past 8/25's cutoff.
            var clock = new FakeClock(new DateTimeOffset(2026, 8, 26, 10, 1, 0, TimeSpan.Zero));
            var refreshRunner = new FakeScheduledRefreshRunner { ResultToReturn = FakeScheduledRefreshRunner.EmptySuccess() };

            // Seed the 8/25 saved draft directly against the real database, exactly as a prior
            // interactive Associate save would have left it.
            var seedConnectionFactory = new SqliteConnectionFactory(tempDb);
            new MigrationRunner(
                seedConnectionFactory,
                [
                    new Migration0001CreateSchemaVersionTable(), new Migration0002CreateSourceStateTable(),
                    new Migration0003CreateDatedResultsTable(), new Migration0004CreateCatalogProductsTable(),
                    new Migration0005CreateProductHistoryTable(), new Migration0006CreateAssociateDraftsTable(),
                    new Migration0007CreateManagerDraftsTable(), new Migration0008CreateFinalizedPassdownsTable(),
                    new Migration0009AddFinalizedPassdownPdfColumns(),
                ],
                NullLogger<MigrationRunner>.Instance).Run();
            await new SqliteAssociateDraftRepository(seedConnectionFactory).SaveAsync(new AssociateDraft(
                PassdownDate, AssociateDraftPayload.CurrentSchemaVersion, JsonSerializer.Serialize(AssociateDraftPayload.CreateDefault()), clock.UtcNow.AddDays(-1)));

            var host = new ScheduledRefreshHost(() => BuildProvider(tempDb, clock, refreshRunner));

            var exitCode = await host.RunAsync();

            Assert.Equal(ScheduledRefreshExitCode.Success, exitCode);
            Assert.Equal(1, refreshRunner.CallCount); // refresh ran afterward.

            var verifyConnectionFactory = new SqliteConnectionFactory(tempDb);
            var finalized = await new SqliteFinalizedPassdownRepository(verifyConnectionFactory).GetByDateAsync(PassdownDate);
            Assert.NotNull(finalized);
            Assert.Equal(FinalizationOrigin.Scheduled, finalized!.Origin);
            Assert.Equal(clock.UtcNow, finalized.FinalizedAtUtc);

            // Phase 9C: the real end-to-end scheduled pipeline now freezes a real PDF alongside the
            // snapshot, atomically, in the same insert.
            Assert.True(finalized.HasFrozenPdf);
            Assert.NotNull(finalized.PdfBytes);
            Assert.NotEmpty(finalized.PdfBytes!);
            Assert.Equal(finalized.PdfBytes!.LongLength, finalized.PdfSizeBytes);
            Assert.Matches("^[0-9a-f]{64}$", finalized.PdfSha256);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(tempDb);
        }
    }
}
