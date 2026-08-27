using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.History;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Desktop.ScheduledRefresh;
using Carowinds.Passdown.Desktop.Startup;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Concurrency;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Carowinds.Passdown.Ui.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Ui.Tests.ScheduledRefresh;

/// <summary>
/// Phase 9E "FULL VALIDATION" — integration-level tests over the complete draft -&gt; finalization -&gt;
/// refresh -&gt; History pipeline, using real temp-file SQLite databases, real migrations, and real
/// Application/Infrastructure/Documents services throughout (never in-memory fakes for the pieces
/// being validated). <see cref="ScheduledRefreshFinalizationIntegrationTests"/> (Phase 9B/9C) already
/// covers the single-date happy path; this file covers what only an end-to-end pipeline can prove:
/// History round-tripping real finalized output, post-refresh immutability, multi-day recovery,
/// real-failure-blocks-refresh, idempotency, History list/BLOB efficiency at scale, malformed/legacy
/// row tolerance, startup catch-up, and lock-protected overlap — all with no live network
/// (<see cref="ThrowingRefreshOrchestrator"/> proves finalization itself never reaches retrieval).
/// </summary>
public class Phase9EIntegrationTests
{
    private static readonly Uri SourceUrl = new("https://www.sixflags.com/carowinds/park-hours");

    private sealed class ThrowingRefreshOrchestrator : IRefreshOrchestrator
    {
        public Task<RefreshSummary> RunScheduledRefreshAsync(IProgress<RefreshProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Finalization must never trigger a refresh.");

        public Task<RefreshSummary> RunUserForcedRefreshAsync(DateOnly date, IProgress<RefreshProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Finalization must never trigger a refresh.");
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
    ];

    private static string NewTempDb() => Path.Combine(Path.GetTempPath(), $"carowinds-passdown-9e-{Guid.NewGuid():N}.db");

    private static void RunMigrations(string databasePath) =>
        new MigrationRunner(new SqliteConnectionFactory(databasePath), AllMigrations, NullLogger<MigrationRunner>.Instance).Run();

    private sealed record Environment(
        SqliteConnectionFactory ConnectionFactory,
        SqliteAssociateDraftRepository Drafts,
        SqliteDatedResultRepository DatedResults,
        SqliteCatalogProductRepository CatalogProducts,
        SqliteFinalizedPassdownRepository FinalizedPassdowns,
        AssociatePassdownService AssociateService,
        FinalizedPassdownSnapshotBuilder SnapshotBuilder,
        FinalizedPassdownFinalizationService FinalizationService,
        PassdownHistoryService HistoryService,
        FinalizedPassdownPdfRetrievalService PdfRetrievalService);

    private static Environment BuildEnvironment(string databasePath, FakeClock clock)
    {
        var connectionFactory = new SqliteConnectionFactory(databasePath);
        var drafts = new SqliteAssociateDraftRepository(connectionFactory);
        var datedResults = new SqliteDatedResultRepository(connectionFactory);
        var catalogProducts = new SqliteCatalogProductRepository(connectionFactory);
        var finalizedPassdowns = new SqliteFinalizedPassdownRepository(connectionFactory);
        var associateService = new AssociatePassdownService(
            drafts, datedResults, catalogProducts, new ThrowingRefreshOrchestrator(),
            new OperationalNotesService(new InMemoryOperationalNoteRepository(), clock), clock);
        var snapshotBuilder = new FinalizedPassdownSnapshotBuilder(); // real Documents PDF generator — no fakes.
        var finalizationService = new FinalizedPassdownFinalizationService(finalizedPassdowns, snapshotBuilder);
        var historyService = new PassdownHistoryService(finalizedPassdowns);
        var pdfRetrievalService = new FinalizedPassdownPdfRetrievalService(finalizedPassdowns);

        return new Environment(
            connectionFactory, drafts, datedResults, catalogProducts, finalizedPassdowns,
            associateService, snapshotBuilder, finalizationService, historyService, pdfRetrievalService);
    }

    private static IServiceProvider BuildHostProvider(string databasePath, FakeClock clock, FakeScheduledRefreshRunner refreshRunner)
    {
        var env = BuildEnvironment(databasePath, clock);
        var finalizer = new ScheduledPassdownFinalizer(
            env.Drafts, env.AssociateService, env.FinalizedPassdowns, env.FinalizationService, clock, NullLogger<ScheduledPassdownFinalizer>.Instance);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(env.ConnectionFactory);
        services.AddSingleton(AllMigrations);
        services.AddSingleton<MigrationRunner>();
        services.AddSingleton<IScheduledPassdownFinalizer>(finalizer);
        services.AddSingleton<IScheduledRefreshRunner>(refreshRunner);
        return services.BuildServiceProvider();
    }

    private static Task SaveDraftAsync(SqliteAssociateDraftRepository drafts, DateOnly date, AssociateDraftPayload payload, DateTimeOffset updatedAt) =>
        drafts.SaveAsync(new AssociateDraft(date, AssociateDraftPayload.CurrentSchemaVersion, JsonSerializer.Serialize(payload), updatedAt));

    private static async Task SeedParkHoursAsync(SqliteDatedResultRepository datedResults, DateOnly date, TimeOnly open, TimeOnly close, DateTimeOffset checkedAt)
    {
        var evidence = new SourceEvidence(
            sourceName: "Park hours", sourceUrl: SourceUrl, timeChecked: checkedAt,
            status: RetrievalStatus.Verified, detail: "Checked successfully.", strategy: RetrievalStrategy.Html,
            requestedDate: date, confirmedDate: date);
        await datedResults.RecordSuccessAsync(
            new DatedResultKey(SourceCategory.ParkHours, date), evidence, JsonSerializer.Serialize(new ParkHoursPayload(open, close)));
    }

    private static Task SeedCatalogProductAsync(
        SqliteCatalogProductRepository catalogProducts, SourceCategory category, string name,
        decimal price, string displayPrice, decimal? comparisonPrice, DateTimeOffset checkedAt)
    {
        var key = CatalogProductKey.FromSourceAndName(category, category.ToString(), name);
        var evidence = new SourceEvidence(
            sourceName: category.ToString(), sourceUrl: SourceUrl, timeChecked: checkedAt,
            status: RetrievalStatus.Verified, detail: "Checked successfully.", strategy: RetrievalStrategy.Html);
        return catalogProducts.UpsertAsync(new CatalogProductUpsert(
            Key: key,
            DisplayName: name,
            LifecycleState: ProductLifecycleState.Active,
            FirstSeenAt: checkedAt,
            LastSeenAt: checkedAt,
            LastVerifiedAt: checkedAt,
            ConsecutiveConfirmedAbsenceCount: 0,
            ArchivedAt: null,
            ArchivedReason: null,
            CurrentPriceAmount: price,
            CurrentDisplayPrice: displayPrice,
            ComparisonPriceAmount: comparisonPrice,
            PriceQualifier: comparisonPrice is null ? null : "Online Price",
            DateSpecific: false,
            ApplicableDate: null,
            LastEvidence: evidence));
    }

    private static AssociateDraftPayload RepresentativeDraft() => AssociateDraftPayload.CreateDefault() with
    {
        ProjectedAttendance = 18500,
        PreSolds = 4200,
        OperationalNotes = ["Gate crew short-staffed until 11 AM.", "Fury 325 closed for scheduled maintenance until 11 AM."],
        DailyFeatureType = DailyFeatureType.QuestionOfTheDay,
        DailyFeatureText = "What year did Carowinds open?",
        CustomProducts = [new CustomProductEntry("meal-voucher", "Team Member Meal Voucher", 8.00m, "One per shift.", IncludeOnPdf: true)],
    };

    // ==================================================================
    // Part A + Part B: full pipeline, History round-trip, then post-refresh immutability.
    // ==================================================================

    [Fact]
    public async Task FullPipeline_RepresentativePassdown_FinalizesAndHistoryMatches_ThenSurvivesLaterMutation()
    {
        var passdownDate = new DateOnly(2026, 8, 25);
        var tempDb = NewTempDb();
        try
        {
            RunMigrations(tempDb);
            var seedClock = new FakeClock(new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero));
            var seedEnv = BuildEnvironment(tempDb, seedClock);

            await SaveDraftAsync(seedEnv.Drafts, passdownDate, RepresentativeDraft(), seedClock.UtcNow);
            await SeedParkHoursAsync(seedEnv.DatedResults, passdownDate, new TimeOnly(9, 0), new TimeOnly(22, 0), seedClock.UtcNow);
            await SeedCatalogProductAsync(seedEnv.CatalogProducts, SourceCategory.SeasonPasses, "Gold Pass", 149.99m, "$149.99", 199.99m, seedClock.UtcNow);

            // 06:01 AM Eastern on 2026-08-26 (EDT, UTC-4) — one minute past 8/25's cutoff.
            var clock = new FakeClock(new DateTimeOffset(2026, 8, 26, 10, 1, 0, TimeSpan.Zero));
            var refreshRunner = new FakeScheduledRefreshRunner { ResultToReturn = FakeScheduledRefreshRunner.EmptySuccess() };
            var host = new ScheduledRefreshHost(() => BuildHostProvider(tempDb, clock, refreshRunner));

            var exitCode = await host.RunAsync();

            Assert.Equal(ScheduledRefreshExitCode.Success, exitCode);
            Assert.Equal(1, refreshRunner.CallCount); // finalization ran, then refresh ran afterward.

            var verifyEnv = BuildEnvironment(tempDb, clock);
            var finalized = await verifyEnv.FinalizedPassdowns.GetByDateAsync(passdownDate);
            Assert.NotNull(finalized);
            Assert.True(finalized!.HasFrozenPdf);

            // History service (real SQLite, real service) surfaces the row newest-first.
            var list = await verifyEnv.HistoryService.ListAsync();
            Assert.Single(list);
            Assert.Equal(passdownDate, list[0].PassdownDate);

            var detail = await verifyEnv.HistoryService.GetAsync(passdownDate);
            Assert.NotNull(detail);
            Assert.Null(detail!.UnavailableReason);
            Assert.NotNull(detail.Snapshot);
            Assert.Equal(18500, detail.Snapshot!.Attendance.ProjectedAttendance);
            Assert.Equal("What year did Carowinds open?", detail.Snapshot.DailyFeature.Text);
            var goldPass = detail.Snapshot.ProductCategories.SelectMany(c => c.Products).Single(p => p.DisplayName == "Gold Pass");
            Assert.Equal("$149.99", goldPass.EffectivePriceText);
            Assert.Equal("$199.99", goldPass.ComparisonPriceText);
            var voucher = detail.Snapshot.ProductCategories.SelectMany(c => c.Products).Single(p => p.DisplayName == "Team Member Meal Voucher");
            Assert.Equal("$8.00", voucher.EffectivePriceText);

            // Stored PDF bytes match the finalized bytes exactly (real retrieval service, not the repository directly).
            var pdf = await verifyEnv.PdfRetrievalService.GetPdfAsync(passdownDate);
            Assert.NotNull(pdf);
            Assert.Equal(finalized.PdfBytes, pdf!.PdfBytes);
            Assert.Equal(finalized.PdfSha256, pdf.PdfSha256);
            Assert.Equal(finalized.PdfSizeBytes, pdf.PdfSizeBytes);

            var originalSnapshotHash = finalized.ContentHash;
            var originalPdfHash = finalized.PdfSha256;
            var originalPdfBytes = finalized.PdfBytes;

            // --- Part B: mutate current state after the finalized record exists. ---
            await SeedCatalogProductAsync(verifyEnv.CatalogProducts, SourceCategory.SeasonPasses, "Gold Pass", 174.99m, "$174.99", 219.99m, clock.UtcNow);
            await SeedParkHoursAsync(verifyEnv.DatedResults, passdownDate, new TimeOnly(8, 0), new TimeOnly(23, 0), clock.UtcNow);
            await SaveDraftAsync(verifyEnv.Drafts, passdownDate, RepresentativeDraft() with
            {
                OperationalNotes = ["A completely different note entered after finalization."],
                DailyFeatureText = "A different Daily Feature entirely.",
            }, clock.UtcNow);

            var reloadedEnv = BuildEnvironment(tempDb, clock);
            var reloadedFinalized = await reloadedEnv.FinalizedPassdowns.GetByDateAsync(passdownDate);
            Assert.NotNull(reloadedFinalized);
            Assert.Equal(originalSnapshotHash, reloadedFinalized!.ContentHash);
            Assert.Equal(originalPdfHash, reloadedFinalized.PdfSha256);
            Assert.Equal(originalPdfBytes, reloadedFinalized.PdfBytes);

            var reloadedDetail = await reloadedEnv.HistoryService.GetAsync(passdownDate);
            Assert.NotNull(reloadedDetail);
            Assert.Equal("What year did Carowinds open?", reloadedDetail!.Snapshot!.DailyFeature.Text);
            Assert.Equal(["Gate crew short-staffed until 11 AM.", "Fury 325 closed for scheduled maintenance until 11 AM."], reloadedDetail.Snapshot.OperationalNotes);
            var reloadedGoldPass = reloadedDetail.Snapshot.ProductCategories.SelectMany(c => c.Products).Single(p => p.DisplayName == "Gold Pass");
            Assert.Equal("$149.99", reloadedGoldPass.EffectivePriceText); // still the frozen price, not $174.99.

            var reloadedPdf = await reloadedEnv.PdfRetrievalService.GetPdfAsync(passdownDate);
            Assert.Equal(originalPdfBytes, reloadedPdf!.PdfBytes);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(tempDb);
        }
    }

    // ==================================================================
    // Part C: multi-day missed-run recovery with real persistence.
    // ==================================================================

    [Fact]
    public async Task MissedRunRecovery_MultiDayDowntime_FinalizesOldestFirst_OneRefreshAfterward()
    {
        var dates = new[] { new DateOnly(2026, 8, 24), new DateOnly(2026, 8, 25), new DateOnly(2026, 8, 26), new DateOnly(2026, 8, 27) };
        var tempDb = NewTempDb();
        try
        {
            RunMigrations(tempDb);
            var seedClock = new FakeClock(new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero));
            var seedEnv = BuildEnvironment(tempDb, seedClock);
            foreach (var date in dates)
            {
                await SaveDraftAsync(seedEnv.Drafts, date, RepresentativeDraft(), seedClock.UtcNow);
            }

            // Already finalized before the recovery run — 8/25 should be skipped, not duplicated.
            var preFinalized = await seedEnv.FinalizationService.FinalizeAsync(
                dates[1], await seedEnv.AssociateService.LoadAssociateDateAsync(dates[1]), FinalizationOrigin.Scheduled, seedClock.UtcNow);
            Assert.Equal(FinalizationOutcome.Created, preFinalized.Outcome);

            // 2026-08-28 09:00 Eastern (EDT, UTC-4) — well past every remaining date's cutoff.
            var clock = new FakeClock(new DateTimeOffset(2026, 8, 28, 13, 0, 0, TimeSpan.Zero));
            var refreshRunner = new FakeScheduledRefreshRunner { ResultToReturn = FakeScheduledRefreshRunner.EmptySuccess() };
            var host = new ScheduledRefreshHost(() => BuildHostProvider(tempDb, clock, refreshRunner));

            var exitCode = await host.RunAsync();

            Assert.Equal(ScheduledRefreshExitCode.Success, exitCode);
            Assert.Equal(1, refreshRunner.CallCount); // exactly one refresh invocation after all recovery finalization finished.

            var verifyEnv = BuildEnvironment(tempDb, clock);
            foreach (var date in dates)
            {
                Assert.True(await verifyEnv.FinalizedPassdowns.ExistsForDateAsync(date), $"{date} should be finalized.");
            }

            var list = await verifyEnv.HistoryService.ListAsync();
            Assert.Equal(4, list.Count);
            Assert.Equal(dates.OrderByDescending(d => d).ToArray(), list.Select(s => s.PassdownDate).ToArray()); // newest-first.

            // 8/25 was already finalized before this run: origin unchanged from the pre-seeded value.
            var preExisting = await verifyEnv.FinalizedPassdowns.GetByDateAsync(dates[1]);
            Assert.Equal(FinalizationOrigin.Scheduled, preExisting!.Origin);

            // The newly recovered dates: 8/24, 8/26, 8/27's cutoff calendar dates (8/25, 8/27, 8/28) are
            // all earlier than today's Eastern calendar date (8/28) except 8/27 (cutoff 8/28 == today).
            Assert.Equal(FinalizationOrigin.Recovery, (await verifyEnv.FinalizedPassdowns.GetByDateAsync(dates[0]))!.Origin);
            Assert.Equal(FinalizationOrigin.Recovery, (await verifyEnv.FinalizedPassdowns.GetByDateAsync(dates[2]))!.Origin);
            Assert.Equal(FinalizationOrigin.Scheduled, (await verifyEnv.FinalizedPassdowns.GetByDateAsync(dates[3]))!.Origin);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(tempDb);
        }
    }

    // ==================================================================
    // Part E: a real failure (PDF generator's one-page enforcement rejects excessive content, not a
    // simulated fake) blocks refresh entirely, and a later retry with the failure removed succeeds.
    // ==================================================================

    [Fact]
    public async Task RealPdfGenerationFailure_BlocksRefresh_PreservesState_ThenSucceedsOnRetryAfterFixed()
    {
        var passdownDate = new DateOnly(2026, 8, 25);
        var tempDb = NewTempDb();
        try
        {
            RunMigrations(tempDb);
            var seedClock = new FakeClock(new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));
            var seedEnv = BuildEnvironment(tempDb, seedClock);

            // Excessive content that the real AssociatePassdownPdfGenerator's strict one-page layout
            // rejects (mirrors Documents.Tests' AssociateWorkspaceFixtures.ExcessiveDay composition).
            // Deliberately draft-only (no catalog/dated seeding): the fix on retry below is simply
            // re-saving the draft, so nothing else needs to be undone for the retry to succeed.
            var excessiveDraft = RepresentativeDraft() with
            {
                OperationalNotes = Enumerable.Range(1, 6)
                    .Select(i => $"Operational note number {i} describing a staffing or guest-service situation for the day in enough detail to take up a full line.")
                    .ToList(),
                CustomProducts = Enumerable.Range(1, 40)
                    .Select(i => new CustomProductEntry($"custom-{i}", $"Custom Internal Product Number {i} With A Long Descriptive Name", 12.34m, $"Note text for custom product {i}.", true))
                    .ToList(),
            };
            await SaveDraftAsync(seedEnv.Drafts, passdownDate, excessiveDraft, seedClock.UtcNow);

            var clock = new FakeClock(new DateTimeOffset(2026, 8, 26, 10, 1, 0, TimeSpan.Zero));
            var refreshRunner = new FakeScheduledRefreshRunner { ResultToReturn = FakeScheduledRefreshRunner.EmptySuccess() };
            var host = new ScheduledRefreshHost(() => BuildHostProvider(tempDb, clock, refreshRunner));

            var exitCode = await host.RunAsync();

            Assert.Equal(ScheduledRefreshExitCode.FatalFailure, exitCode);
            Assert.Equal(0, refreshRunner.CallCount); // refresh never invoked for this invocation.

            var afterFailureEnv = BuildEnvironment(tempDb, clock);
            Assert.False(await afterFailureEnv.FinalizedPassdowns.ExistsForDateAsync(passdownDate)); // no partial row.
            var draftAfterFailure = await afterFailureEnv.Drafts.GetAsync(passdownDate);
            Assert.NotNull(draftAfterFailure); // pre-refresh persisted source state (the draft) is unchanged.

            // Retry: replace the excessive draft with the normal representative one; the same
            // now-eligible date should finalize successfully and refresh should then proceed.
            await SaveDraftAsync(afterFailureEnv.Drafts, passdownDate, RepresentativeDraft(), clock.UtcNow);
            var retryRefreshRunner = new FakeScheduledRefreshRunner { ResultToReturn = FakeScheduledRefreshRunner.EmptySuccess() };
            var retryHost = new ScheduledRefreshHost(() => BuildHostProvider(tempDb, clock, retryRefreshRunner));

            var retryExitCode = await retryHost.RunAsync();

            Assert.Equal(ScheduledRefreshExitCode.Success, retryExitCode);
            Assert.Equal(1, retryRefreshRunner.CallCount);
            var afterRetryEnv = BuildEnvironment(tempDb, clock);
            Assert.True(await afterRetryEnv.FinalizedPassdowns.ExistsForDateAsync(passdownDate));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(tempDb);
        }
    }

    // ==================================================================
    // Part F: repeated invocation for the same eligible date is idempotent.
    // ==================================================================

    [Fact]
    public async Task DuplicateInvocation_SameEligibleDate_ProducesOneRowAndSkipsOnSubsequentRuns()
    {
        var passdownDate = new DateOnly(2026, 8, 25);
        var tempDb = NewTempDb();
        try
        {
            RunMigrations(tempDb);
            var seedClock = new FakeClock(new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));
            var seedEnv = BuildEnvironment(tempDb, seedClock);
            await SaveDraftAsync(seedEnv.Drafts, passdownDate, RepresentativeDraft(), seedClock.UtcNow);

            var clock = new FakeClock(new DateTimeOffset(2026, 8, 26, 10, 1, 0, TimeSpan.Zero));

            Guid? firstId = null;
            for (var run = 0; run < 3; run++)
            {
                var refreshRunner = new FakeScheduledRefreshRunner { ResultToReturn = FakeScheduledRefreshRunner.EmptySuccess() };
                var host = new ScheduledRefreshHost(() => BuildHostProvider(tempDb, clock, refreshRunner));

                var exitCode = await host.RunAsync();

                Assert.Equal(ScheduledRefreshExitCode.Success, exitCode);
                Assert.Equal(1, refreshRunner.CallCount); // every invocation still proceeds to refresh normally.

                var env = BuildEnvironment(tempDb, clock);
                var finalized = await env.FinalizedPassdowns.GetByDateAsync(passdownDate);
                Assert.NotNull(finalized);
                firstId ??= finalized!.Id;
                Assert.Equal(firstId, finalized!.Id); // same row every time, never replaced.

                var list = await env.HistoryService.ListAsync();
                Assert.Single(list); // no duplicate History entry.
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(tempDb);
        }
    }

    // Defense-in-depth: the unique index remains the final backstop even if application-level
    // check-before-insert is bypassed by calling AddAsync twice directly.
    [Fact]
    public async Task DuplicateInsert_BypassingApplicationCheck_UniqueIndexRejectsSecondRow()
    {
        var passdownDate = new DateOnly(2026, 8, 25);
        var tempDb = NewTempDb();
        try
        {
            RunMigrations(tempDb);
            var repository = new SqliteFinalizedPassdownRepository(new SqliteConnectionFactory(tempDb));
            var now = new DateTimeOffset(2026, 8, 26, 10, 1, 0, TimeSpan.Zero);

            await repository.AddAsync(new FinalizedPassdown(
                Guid.NewGuid(), passdownDate, now, FinalizedPassdownSnapshot.CurrentSchemaVersion, "{}", "hash-1", FinalizationOrigin.Scheduled, null, null, null));

            await Assert.ThrowsAsync<SqliteException>(() => repository.AddAsync(new FinalizedPassdown(
                Guid.NewGuid(), passdownDate, now, FinalizedPassdownSnapshot.CurrentSchemaVersion, "{}", "hash-2", FinalizationOrigin.Scheduled, null, null, null)));

            var list = await repository.ListAsync();
            Assert.Single(list);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(tempDb);
        }
    }

    // ==================================================================
    // Part G/H/R: History list scale, BLOB efficiency, and malformed/legacy row tolerance — all
    // against a real SQLite database with a realistic number of rows.
    // ==================================================================

    [Fact]
    public async Task HistoryAtScale_MetadataOnlyListing_OneRowDetailAndPdfLoad_ToleratesBadRows()
    {
        var tempDb = NewTempDb();
        try
        {
            RunMigrations(tempDb);
            var clock = new FakeClock(new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero));
            var env = BuildEnvironment(tempDb, clock);

            var parkHours = new AssociateParkHoursView(
                ParkHoursCalculator.Calculate(false, null, null, false, null, null, false, null, null),
                AssociateVerificationInfo.NotCheckedYet, OfficialIsClosed: false, OfficialOpening: null, OfficialClosing: null);

            const int normalRecordCount = 40;
            var baseDate = new DateOnly(2026, 1, 1);
            for (var i = 0; i < normalRecordCount; i++)
            {
                var date = baseDate.AddDays(i);
                var workspace = new AssociateWorkspace(date, AssociateDraftPayload.CreateDefault(), parkHours, [], [], AssociateSaveStatus.NeverSaved);
                var result = await env.FinalizationService.FinalizeAsync(date, workspace, FinalizationOrigin.Scheduled, clock.UtcNow);
                Assert.Equal(FinalizationOutcome.Created, result.Outcome);
            }

            // Legacy pre-9C row (no PDF columns) — inserted the same way a pre-Migration0009 row would exist.
            var legacyDate = new DateOnly(2026, 3, 1);
            await InsertLegacyRowAsync(env.ConnectionFactory, legacyDate, clock.UtcNow);

            // Malformed JSON row (current schema version, but corrupt payload).
            var malformedDate = new DateOnly(2026, 3, 2);
            await InsertRawRowAsync(env.ConnectionFactory, malformedDate, clock.UtcNow, FinalizedPassdownSnapshot.CurrentSchemaVersion, "{ not valid json");

            // Future/unsupported schema version row.
            var futureSchemaDate = new DateOnly(2026, 3, 3);
            await InsertRawRowAsync(env.ConnectionFactory, futureSchemaDate, clock.UtcNow, FinalizedPassdownSnapshot.CurrentSchemaVersion + 1, "{}");

            var totalRows = normalRecordCount + 3;

            // List must still load and return every row's metadata without throwing on the bad rows.
            var list = await env.HistoryService.ListAsync();
            Assert.Equal(totalRows, list.Count);
            Assert.Equal(list.OrderByDescending(s => s.PassdownDate).Select(s => s.PassdownDate), list.Select(s => s.PassdownDate));

            // Selecting one normal date only loads that date's detail/PDF — verified indirectly by
            // GetAsync/GetPdfAsync returning exactly the requested date's content.
            var oneDate = baseDate.AddDays(5);
            var detail = await env.HistoryService.GetAsync(oneDate);
            Assert.NotNull(detail);
            Assert.Null(detail!.UnavailableReason);
            var pdf = await env.PdfRetrievalService.GetPdfAsync(oneDate);
            Assert.NotNull(pdf);
            Assert.NotEmpty(pdf!.PdfBytes);

            // Legacy no-PDF row: detail still renders, PDF retrieval reports unavailable, never regenerates.
            var legacyDetail = await env.HistoryService.GetAsync(legacyDate);
            Assert.NotNull(legacyDetail);
            Assert.Null(legacyDetail!.UnavailableReason);
            Assert.False(legacyDetail.Summary.HasFrozenPdf);
            Assert.Null(await env.PdfRetrievalService.GetPdfAsync(legacyDate));

            // Malformed JSON row: metadata still visible, structured detail unavailable, no crash.
            var malformedDetail = await env.HistoryService.GetAsync(malformedDate);
            Assert.NotNull(malformedDetail);
            Assert.Null(malformedDetail!.Snapshot);
            Assert.NotNull(malformedDetail.UnavailableReason);

            // Future schema-version row: same graceful degradation.
            var futureDetail = await env.HistoryService.GetAsync(futureSchemaDate);
            Assert.NotNull(futureDetail);
            Assert.Null(futureDetail!.Snapshot);
            Assert.NotNull(futureDetail.UnavailableReason);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(tempDb);
        }
    }

    private static async Task InsertLegacyRowAsync(SqliteConnectionFactory connectionFactory, DateOnly date, DateTimeOffset finalizedAt)
    {
        var snapshot = new FinalizedPassdownSnapshot(
            FinalizedPassdownSnapshot.CurrentSchemaVersion, date, date.ToString("yyyy-MM-dd"),
            new FinalizedParkHoursSnapshot(FinalizedParkHoursStatus.NotCheckedYet, "Hours not checked yet", null, null, null, null, null),
            new FinalizedAttendanceSnapshot(null, null),
            new FinalizedEarlyRideTimeSnapshot(false, null, []),
            new FinalizedDailyFeatureSnapshot(null, null),
            [], [], HasUnverifiedProductPricing: false, GeneratedAtText: finalizedAt.ToString("O"));
        var json = FinalizedPassdownSnapshotSerializer.Serialize(snapshot);

        await using var connection = await connectionFactory.OpenConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO finalized_passdowns (id, passdown_date, finalized_at_utc, snapshot_schema_version, snapshot_json, content_hash, origin)
            VALUES ($id, $passdownDate, $finalizedAt, $schemaVersion, $snapshotJson, $contentHash, $origin);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$passdownDate", date.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$finalizedAt", finalizedAt.ToString("O"));
        command.Parameters.AddWithValue("$schemaVersion", FinalizedPassdownSnapshot.CurrentSchemaVersion);
        command.Parameters.AddWithValue("$snapshotJson", json);
        command.Parameters.AddWithValue("$contentHash", FinalizedPassdownSnapshotSerializer.ComputeContentHash(json));
        command.Parameters.AddWithValue("$origin", FinalizationOrigin.Scheduled.ToString());
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertRawRowAsync(SqliteConnectionFactory connectionFactory, DateOnly date, DateTimeOffset finalizedAt, int schemaVersion, string snapshotJson)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO finalized_passdowns (id, passdown_date, finalized_at_utc, snapshot_schema_version, snapshot_json, content_hash, origin)
            VALUES ($id, $passdownDate, $finalizedAt, $schemaVersion, $snapshotJson, $contentHash, $origin);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$passdownDate", date.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$finalizedAt", finalizedAt.ToString("O"));
        command.Parameters.AddWithValue("$schemaVersion", schemaVersion);
        command.Parameters.AddWithValue("$snapshotJson", snapshotJson);
        command.Parameters.AddWithValue("$contentHash", "irrelevant-for-this-test");
        command.Parameters.AddWithValue("$origin", FinalizationOrigin.Scheduled.ToString());
        await command.ExecuteNonQueryAsync();
    }

    // ==================================================================
    // Part K: startup catch-up end-to-end (real SQLite, real lock, real finalizer).
    // ==================================================================

    [Fact]
    public async Task StartupCatchUp_MissedCutoff_FinalizesAndHistoryAppears()
    {
        var passdownDate = new DateOnly(2026, 8, 25);
        var tempDb = NewTempDb();
        var lockPath = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-9e-startup-lock-{Guid.NewGuid():N}.lock");
        try
        {
            RunMigrations(tempDb);
            var seedClock = new FakeClock(new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));
            var seedEnv = BuildEnvironment(tempDb, seedClock);
            await SaveDraftAsync(seedEnv.Drafts, passdownDate, RepresentativeDraft(), seedClock.UtcNow);

            var clock = new FakeClock(new DateTimeOffset(2026, 8, 26, 10, 1, 0, TimeSpan.Zero));
            var refreshRunner = new FakeScheduledRefreshRunner { ResultToReturn = FakeScheduledRefreshRunner.EmptySuccess() };
            var services = BuildHostProvider(tempDb, clock, refreshRunner);
            services.GetRequiredService<MigrationRunner>().Run();

            using var scheduledLock = new FileScheduledRefreshLock(lockPath);

            await StartupCatchUpRefresh.RunAsync(services, scheduledLock, NullLogger<Phase9EIntegrationTests>.Instance);

            Assert.Equal(1, refreshRunner.CallCount);
            var verifyEnv = BuildEnvironment(tempDb, clock);
            Assert.True(await verifyEnv.FinalizedPassdowns.ExistsForDateAsync(passdownDate));
            var list = await verifyEnv.HistoryService.ListAsync();
            Assert.Single(list);
            Assert.Equal(passdownDate, list[0].PassdownDate);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(tempDb);
            if (File.Exists(lockPath))
            {
                File.Delete(lockPath);
            }
        }
    }

    // ==================================================================
    // Part L: overlap — a real FileScheduledRefreshLock already held (simulating a concurrently
    // running scheduled/catch-up invocation) means finalization never runs for this invocation.
    // ==================================================================

    [Fact]
    public async Task Overlap_LockAlreadyHeld_NeverFinalizes_NeverRefreshes()
    {
        var passdownDate = new DateOnly(2026, 8, 25);
        var tempDb = NewTempDb();
        var lockPath = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-9e-overlap-lock-{Guid.NewGuid():N}.lock");
        try
        {
            RunMigrations(tempDb);
            var seedClock = new FakeClock(new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));
            var seedEnv = BuildEnvironment(tempDb, seedClock);
            await SaveDraftAsync(seedEnv.Drafts, passdownDate, RepresentativeDraft(), seedClock.UtcNow);

            var clock = new FakeClock(new DateTimeOffset(2026, 8, 26, 10, 1, 0, TimeSpan.Zero));
            var refreshRunner = new FakeScheduledRefreshRunner { ResultToReturn = FakeScheduledRefreshRunner.EmptySuccess() };

            // Simulates another process already holding the lock (a concurrently running scheduled
            // or startup catch-up invocation).
            using var holder = new FileScheduledRefreshLock(lockPath);
            Assert.True(holder.TryAcquire());

            var host = new ScheduledRefreshHost(
                () => BuildHostProvider(tempDb, clock, refreshRunner),
                scheduledRefreshLockFactory: () => new FileScheduledRefreshLock(lockPath));

            var exitCode = await host.RunAsync();

            Assert.Equal(ScheduledRefreshExitCode.Overlap, exitCode);
            Assert.Equal(0, refreshRunner.CallCount);

            var verifyEnv = BuildEnvironment(tempDb, clock);
            Assert.False(await verifyEnv.FinalizedPassdowns.ExistsForDateAsync(passdownDate)); // no duplicate/partial finalization.
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(tempDb);
            if (File.Exists(lockPath))
            {
                File.Delete(lockPath);
            }
        }
    }

    // ==================================================================
    // Part M: 6:00 AM Eastern boundary revalidated at orchestration level, across a real DST
    // transition (2026-03-08 America/New_York spring-forward).
    // ==================================================================

    [Fact]
    public async Task DstBoundary_SpringForwardTransition_CutoffCorrectAtOrchestrationLevel()
    {
        var passdownDate = new DateOnly(2026, 3, 7); // cutoff: 2026-03-08 06:00 America/New_York, after that day's 2:00 AM spring-forward.
        var cutoff = FinalizationEligibility.EligibleForFinalizationAt(passdownDate);
        Assert.Equal(TimeSpan.FromHours(-4), cutoff.Offset); // already EDT by 6:00 AM on transition day.

        // Just before cutoff: not yet finalized.
        var beforeDb = NewTempDb();
        try
        {
            RunMigrations(beforeDb);
            var seedEnv = BuildEnvironment(beforeDb, new FakeClock(cutoff.AddDays(-1)));
            await SaveDraftAsync(seedEnv.Drafts, passdownDate, RepresentativeDraft(), cutoff.AddDays(-1));

            var beforeClock = new FakeClock(cutoff.AddSeconds(-1));
            var beforeRunner = new FakeScheduledRefreshRunner { ResultToReturn = FakeScheduledRefreshRunner.EmptySuccess() };
            var beforeHost = new ScheduledRefreshHost(() => BuildHostProvider(beforeDb, beforeClock, beforeRunner));
            await beforeHost.RunAsync();

            var beforeEnv = BuildEnvironment(beforeDb, beforeClock);
            Assert.False(await beforeEnv.FinalizedPassdowns.ExistsForDateAsync(passdownDate));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(beforeDb);
        }

        // Exactly at cutoff (post-transition EDT instant): finalized.
        var atDb = NewTempDb();
        try
        {
            RunMigrations(atDb);
            var seedEnv = BuildEnvironment(atDb, new FakeClock(cutoff.AddDays(-1)));
            await SaveDraftAsync(seedEnv.Drafts, passdownDate, RepresentativeDraft(), cutoff.AddDays(-1));

            var atClock = new FakeClock(cutoff);
            var atRunner = new FakeScheduledRefreshRunner { ResultToReturn = FakeScheduledRefreshRunner.EmptySuccess() };
            var atHost = new ScheduledRefreshHost(() => BuildHostProvider(atDb, atClock, atRunner));
            var exitCode = await atHost.RunAsync();

            Assert.Equal(ScheduledRefreshExitCode.Success, exitCode);
            var atEnv = BuildEnvironment(atDb, atClock);
            Assert.True(await atEnv.FinalizedPassdowns.ExistsForDateAsync(passdownDate));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(atDb);
        }
    }
}
