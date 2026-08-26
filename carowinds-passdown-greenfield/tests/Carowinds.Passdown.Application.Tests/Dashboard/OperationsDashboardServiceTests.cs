using System.Reflection;
using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Dashboard;

/// <summary>
/// Covers <see cref="OperationsDashboardService"/> (Phase 10A Part E/J): every value must come from
/// the real <see cref="AssociatePassdownService"/>'s already-composed <see cref="AssociateWorkspace"/>
/// (never a second hours/draft-composition implementation) and <see cref="IFinalizedPassdownRepository"/>
/// metadata only — never a PDF blob load. Uses the real <see cref="AssociatePassdownService"/> over
/// in-memory repositories (not a fake) so a passing test actually proves reuse, not just a scripted
/// double agreeing with itself.
/// </summary>
public class OperationsDashboardServiceTests
{
    private static readonly DateOnly Date = new(2026, 8, 21);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 14, 0, 0, TimeSpan.Zero); // 10:00 AM Eastern (EDT, UTC-4)

    private sealed record TestEnvironment(
        OperationsDashboardService Service,
        InMemoryAssociateDraftRepository Drafts,
        InMemoryDatedResultRepository DatedResults,
        InMemoryCatalogProductRepository CatalogProducts,
        FakeRefreshOrchestrator RefreshOrchestrator,
        CountingFinalizedPassdownRepository FinalizedPassdowns,
        FakeClock Clock);

    private static TestEnvironment CreateEnvironment(FakeClock? clock = null)
    {
        clock ??= new FakeClock(FixedNow);
        var drafts = new InMemoryAssociateDraftRepository();
        var datedResults = new InMemoryDatedResultRepository();
        var catalogProducts = new InMemoryCatalogProductRepository();
        var refreshOrchestrator = new FakeRefreshOrchestrator();
        var associateService = new AssociatePassdownService(drafts, datedResults, catalogProducts, refreshOrchestrator, clock);
        var finalizedPassdowns = new CountingFinalizedPassdownRepository();
        var service = new OperationsDashboardService(associateService, finalizedPassdowns, clock);
        return new TestEnvironment(service, drafts, datedResults, catalogProducts, refreshOrchestrator, finalizedPassdowns, clock);
    }

    private static Task SaveDraftAsync(TestEnvironment env, AssociateDraftPayload draft) =>
        env.Drafts.SaveAsync(new AssociateDraft(Date, AssociateDraftPayload.CurrentSchemaVersion, JsonSerializer.Serialize(draft), env.Clock.UtcNow));

    // #4/#5: "today" resolves to the current Eastern calendar date, independent of any caller-selected date.
    [Fact]
    public async Task GetTodayAsync_ResolvesToCurrentEasternDate()
    {
        // 2026-08-21T14:00:00Z is 10:00 AM EDT (UTC-4) on 2026-08-21 — same calendar date either way,
        // so also assert the Eastern conversion is actually being used near a day boundary.
        var lateUtcClock = new FakeClock(new DateTimeOffset(2026, 8, 22, 2, 30, 0, TimeSpan.Zero)); // 10:30 PM EDT on 2026-08-21
        var env = CreateEnvironment(lateUtcClock);

        var snapshot = await env.Service.GetTodayAsync();

        Assert.Equal(new DateOnly(2026, 8, 21), snapshot.OperatingDate);
    }

    [Fact]
    public async Task LoadAsync_ArbitraryDate_UsesThatDateRegardlessOfClock()
    {
        var env = CreateEnvironment();

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 9, 1));

        Assert.Equal(new DateOnly(2026, 9, 1), snapshot.OperatingDate);
    }

    // #6/#23: no HTTP/Playwright/refresh dependency exists on this type by construction.
    [Fact]
    public void Constructor_HasNoRefreshOrchestratorOrRetrievalDependency()
    {
        var constructor = typeof(OperationsDashboardService).GetConstructors().Single();
        var parameterTypes = constructor.GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.DoesNotContain(parameterTypes, t => t == typeof(IRefreshOrchestrator));
        Assert.DoesNotContain(parameterTypes, t => t.Name.Contains("Http", StringComparison.OrdinalIgnoreCase));
    }

    // #12: no draft ever saved -> NoDraft, never Finalized/DraftInProgress.
    [Fact]
    public async Task LoadAsync_NoSavedDraft_ReportsNoDraft()
    {
        var env = CreateEnvironment();

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(DashboardPassdownStatus.NoDraft, snapshot.PassdownStatus.Status);
        Assert.Null(snapshot.PassdownStatus.LastSavedAt);
    }

    // #11: a saved (but not finalized) draft -> DraftInProgress, carrying the real save timestamp.
    [Fact]
    public async Task LoadAsync_SavedDraft_ReportsDraftInProgress()
    {
        var env = CreateEnvironment();
        await SaveDraftAsync(env, AssociateDraftPayload.CreateDefault());

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(DashboardPassdownStatus.DraftInProgress, snapshot.PassdownStatus.Status);
        Assert.Equal(env.Clock.UtcNow, snapshot.PassdownStatus.LastSavedAt);
    }

    [Fact]
    public async Task LoadAsync_FinalizedDate_ReportsFinalizedWithTimestamp_AndNeverLoadsFullRecord()
    {
        var env = CreateEnvironment();
        await SaveDraftAsync(env, AssociateDraftPayload.CreateDefault());
        var finalizedAt = env.Clock.UtcNow.AddHours(20);
        await env.FinalizedPassdowns.AddAsync(new FinalizedPassdown(
            Guid.NewGuid(), Date, finalizedAt, 1, "{}", "hash", FinalizationOrigin.Scheduled,
            PdfBytes: [1, 2, 3], PdfSha256: "pdfhash", PdfSizeBytes: 3));

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(DashboardPassdownStatus.Finalized, snapshot.PassdownStatus.Status);
        Assert.Equal(finalizedAt, snapshot.PassdownStatus.FinalizedAt);

        // #18: the dashboard must never materialize a historical PDF blob.
        Assert.Equal(0, env.FinalizedPassdowns.GetByDateCallCount);
    }

    // #8: officially closed -> Closed.
    [Fact]
    public async Task LoadAsync_OfficialParkHoursClosed_ReportsClosed()
    {
        var env = CreateEnvironment();
        await env.DatedResults.RecordSuccessAsync(
            new DatedResultKey(SourceCategory.ParkHours, Date),
            Evidence(RetrievalStatus.Closed),
            payloadJson: null);

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(DashboardParkStatus.Closed, snapshot.ParkStatus.Status);
    }

    // #9: an enabled manual hours override on an otherwise-open day -> CustomHours (reusing ParkHoursCalculator's own precedence, never a second rule).
    [Fact]
    public async Task LoadAsync_ManualHoursOverrideEnabled_ReportsCustomHours()
    {
        var env = CreateEnvironment();
        await env.DatedResults.RecordSuccessAsync(
            new DatedResultKey(SourceCategory.ParkHours, Date),
            Evidence(RetrievalStatus.Verified),
            JsonSerializer.Serialize(new ParkHoursPayload(new TimeOnly(10, 0), new TimeOnly(20, 0))));
        await SaveDraftAsync(env, AssociateDraftPayload.CreateDefault() with
        {
            ManualHoursOverride = new ManualHoursOverride(true, new TimeOnly(9, 0), new TimeOnly(23, 0)),
        });

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(DashboardParkStatus.CustomHours, snapshot.ParkStatus.Status);
    }

    // #10: an enabled private-buyout override -> PrivateBuyout, even on an officially closed day (Part F names this the one exception).
    [Fact]
    public async Task LoadAsync_PrivateBuyoutEnabled_ReportsPrivateBuyout()
    {
        var env = CreateEnvironment();
        await env.DatedResults.RecordSuccessAsync(
            new DatedResultKey(SourceCategory.ParkHours, Date),
            Evidence(RetrievalStatus.Closed),
            payloadJson: null);
        await SaveDraftAsync(env, AssociateDraftPayload.CreateDefault() with
        {
            PrivateBuyout = new PrivateBuyoutOverride(true, new TimeOnly(18, 0), new TimeOnly(22, 0), "Corporate event."),
        });

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(DashboardParkStatus.PrivateBuyout, snapshot.ParkStatus.Status);
    }

    // Phase 10B Part Y #5: an official source explicitly reporting "not published yet" for park
    // hours (no manual override, no private buyout) -> NotPublished, never a bare "Not checked yet".
    [Fact]
    public async Task LoadAsync_ParkHoursNotPublished_ReportsNotPublished()
    {
        var env = CreateEnvironment();
        await env.DatedResults.RecordSuccessAsync(
            new DatedResultKey(SourceCategory.ParkHours, Date),
            Evidence(RetrievalStatus.NotPublished),
            payloadJson: null);

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(DashboardParkStatus.NotPublished, snapshot.ParkStatus.Status);
    }

    // Phase 10B Part Y #4: a failed retrieval attempt with no prior last-known-good -> Unavailable,
    // which must never render/look like Open (spec Part C).
    [Fact]
    public async Task LoadAsync_ParkHoursUnavailable_ReportsUnavailable()
    {
        var env = CreateEnvironment();
        await env.DatedResults.RecordFailedAttemptAsync(
            new DatedResultKey(SourceCategory.ParkHours, Date),
            Evidence(RetrievalStatus.Unavailable));

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(DashboardParkStatus.Unavailable, snapshot.ParkStatus.Status);
    }

    // #13: notes summary derives only from the current date's draft — never a second persistence model.
    [Fact]
    public async Task LoadAsync_OperationalNotes_SummarizesFromDraftOnly()
    {
        var env = CreateEnvironment();
        await SaveDraftAsync(env, AssociateDraftPayload.CreateDefault() with
        {
            OperationalNotes = ["Gate 3 closed.", "VIP tour at 11 AM.", "Extra staffing at Fury.", "Fourth line."],
        });

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.True(snapshot.Notes.HasNotes);
        Assert.Equal(4, snapshot.Notes.TotalLineCount);
        Assert.Equal(3, snapshot.Notes.PreviewLines.Count);
    }

    [Fact]
    public async Task LoadAsync_NoOperationalNotes_ReportsEmptySummary()
    {
        var env = CreateEnvironment();

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.False(snapshot.Notes.HasNotes);
        Assert.Empty(snapshot.Notes.PreviewLines);
    }

    // #14: source health aggregates the same per-category verification state the Associate screen already computes.
    [Fact]
    public async Task LoadAsync_OneCategoryUnavailable_ReportsSomeUnavailable()
    {
        var env = CreateEnvironment();
        await env.DatedResults.RecordFailedAttemptAsync(
            new DatedResultKey(SourceCategory.DatedFastLane, Date),
            Evidence(RetrievalStatus.Unavailable));

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(DashboardOverallDataStatus.SomeUnavailable, snapshot.DataHealth.OverallStatus);
        Assert.True(snapshot.DataHealth.UnavailableCount > 0);
    }

    // Phase 10D Part B/Q: nothing has ever been checked -> the overall status must never fabricate
    // "Current" (a real gap in Phase 10A/B's original 3-state model, fixed here) — it honestly reports
    // that nothing is currently confirmed.
    [Fact]
    public async Task LoadAsync_NothingEverChecked_ReportsNoPublishedData()
    {
        var env = CreateEnvironment();

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(DashboardOverallDataStatus.NoPublishedData, snapshot.DataHealth.OverallStatus);
        Assert.Equal(0, snapshot.DataHealth.CurrentCount);
        Assert.Equal(7, snapshot.DataHealth.NotCheckedCount);
    }

    // Phase 10D Part AD #1: every source current -> overall Current.
    [Fact]
    public async Task LoadAsync_AllSourcesCurrent_ReportsOverallCurrent()
    {
        var env = CreateEnvironment();
        await env.DatedResults.RecordSuccessAsync(new DatedResultKey(SourceCategory.ParkHours, Date), Evidence(RetrievalStatus.Verified), payloadJson: null);
        await env.DatedResults.RecordSuccessAsync(new DatedResultKey(SourceCategory.DatedAdmissionTickets, Date), Evidence(RetrievalStatus.Verified), JsonSerializer.Serialize(new DatedAdmissionPayload([])));
        await env.DatedResults.RecordSuccessAsync(new DatedResultKey(SourceCategory.DatedFastLane, Date), Evidence(RetrievalStatus.Verified), JsonSerializer.Serialize(new DatedFastLanePayload([])));
        await env.DatedResults.RecordSuccessAsync(new DatedResultKey(SourceCategory.HauntedAttractions, Date), Evidence(RetrievalStatus.Verified), JsonSerializer.Serialize(new DatedHauntedPayload([])));

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(DashboardOverallDataStatus.Current, snapshot.DataHealth.OverallStatus);
        Assert.Equal(4, snapshot.DataHealth.CurrentCount);
        Assert.Equal(0, snapshot.DataHealth.VerificationIssueCount);
    }

    // Phase 10D Part AD #3/G: a category that has never succeeded and whose latest attempt errored
    // reports VerificationIssue overall — distinct from Unavailable/NotPublished/Stale.
    [Fact]
    public async Task LoadAsync_ParkHoursNeverSucceededAndErrored_ReportsVerificationIssue()
    {
        var env = CreateEnvironment();
        await env.DatedResults.RecordFailedAttemptAsync(new DatedResultKey(SourceCategory.ParkHours, Date), Evidence(RetrievalStatus.Error));

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(DashboardOverallDataStatus.VerificationIssue, snapshot.DataHealth.OverallStatus);
        Assert.Equal(1, snapshot.DataHealth.VerificationIssueCount);
        var parkHoursEntry = Assert.Single(snapshot.DataHealth.Sources, s => s.Category == SourceCategory.ParkHours);
        Assert.Equal(DashboardSourceStatus.VerificationIssue, parkHoursEntry.Status);
        Assert.Null(parkHoursEntry.LastVerifiedAt);
    }

    // Phase 10D Part D: a genuinely, successfully verified "park is closed" must never read as a
    // confidence problem — Closed is operationally valid, so it counts as Current.
    [Fact]
    public async Task LoadAsync_ParkHoursVerifiedClosed_CountsAsCurrentNotIssue()
    {
        var env = CreateEnvironment();
        await env.DatedResults.RecordSuccessAsync(new DatedResultKey(SourceCategory.ParkHours, Date), Evidence(RetrievalStatus.Closed), payloadJson: null);

        var snapshot = await env.Service.LoadAsync(Date);

        var parkHoursEntry = Assert.Single(snapshot.DataHealth.Sources, s => s.Category == SourceCategory.ParkHours);
        Assert.Equal(DashboardSourceStatus.Current, parkHoursEntry.Status);
        Assert.Equal(DashboardOverallDataStatus.Current, snapshot.DataHealth.OverallStatus);
    }

    // Phase 10D Part E: NotPublished must not degrade the overall state when another source is
    // already Current — it is informational, not a confidence failure.
    [Fact]
    public async Task LoadAsync_OneCategoryNotPublished_OverallStaysCurrentWhenAnotherIsCurrent()
    {
        var env = CreateEnvironment();
        await env.DatedResults.RecordSuccessAsync(new DatedResultKey(SourceCategory.ParkHours, Date), Evidence(RetrievalStatus.Verified), payloadJson: null);
        await env.DatedResults.RecordFailedAttemptAsync(new DatedResultKey(SourceCategory.DatedFastLane, Date), Evidence(RetrievalStatus.NotPublished));

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(DashboardOverallDataStatus.Current, snapshot.DataHealth.OverallStatus);
        var fastLaneEntry = Assert.Single(snapshot.DataHealth.Sources, s => s.Category == SourceCategory.DatedFastLane);
        Assert.Equal(DashboardSourceStatus.NotPublished, fastLaneEntry.Status);
    }

    // Phase 10D Part F/J: a category that previously succeeded, then failed on a later attempt,
    // reports Stale while still surfacing the OLD success timestamp as LastVerifiedAt — never the
    // failed attempt's own time.
    [Fact]
    public async Task LoadAsync_ParkHoursSucceededThenErrored_ReportsStaleWithOldVerifiedTimestamp()
    {
        var env = CreateEnvironment();
        var successAt = FixedNow.AddHours(-10);
        await env.DatedResults.RecordSuccessAsync(new DatedResultKey(SourceCategory.ParkHours, Date), Evidence(RetrievalStatus.Verified, successAt), payloadJson: null);
        await env.DatedResults.RecordFailedAttemptAsync(new DatedResultKey(SourceCategory.ParkHours, Date), Evidence(RetrievalStatus.Error, FixedNow));

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(DashboardOverallDataStatus.SomeStale, snapshot.DataHealth.OverallStatus);
        var parkHoursEntry = Assert.Single(snapshot.DataHealth.Sources, s => s.Category == SourceCategory.ParkHours);
        Assert.Equal(DashboardSourceStatus.Stale, parkHoursEntry.Status);
        Assert.Equal(successAt, parkHoursEntry.LastVerifiedAt);
        Assert.Equal(successAt, snapshot.DataHealth.LatestSuccessfulVerification);
    }

    // Phase 10D Part J: the three dated-tracked categories (Admission/Fast Lane/Haunted) only ever
    // carry an attempt-based timestamp, never a success-only "verified" one — even when currently
    // Current — so a caller can never mislabel it.
    [Fact]
    public async Task LoadAsync_DatedTrackedCategory_NeverExposesLastVerifiedTimestamp()
    {
        var env = CreateEnvironment();
        await env.DatedResults.RecordSuccessAsync(new DatedResultKey(SourceCategory.DatedFastLane, Date), Evidence(RetrievalStatus.Verified), JsonSerializer.Serialize(new DatedFastLanePayload([])));

        var snapshot = await env.Service.LoadAsync(Date);

        var fastLaneEntry = Assert.Single(snapshot.DataHealth.Sources, s => s.Category == SourceCategory.DatedFastLane);
        Assert.Equal(DashboardSourceStatus.Current, fastLaneEntry.Status);
        Assert.Null(fastLaneEntry.LastVerifiedAt);
        Assert.NotNull(fastLaneEntry.LastCheckedAt);
    }

    // Phase 10D Part W/X: a catalog category (Promotions/Passes/Add-Ons) with at least one currently
    // stored product is Current, using that product's own success-only LastVerifiedAt — no separate
    // catalog freshness system invented here.
    [Fact]
    public async Task LoadAsync_CatalogCategoryWithProduct_ReportsCurrentWithVerifiedTimestamp()
    {
        var env = CreateEnvironment();
        var verifiedAt = FixedNow.AddDays(-1);
        await env.CatalogProducts.UpsertAsync(new CatalogProductUpsert(
            CatalogProductKey.FromSourceAndName(SourceCategory.Promotions, "Fake Source", "Summer Splash Promo"), "Summer Splash Promo", ProductLifecycleState.Active,
            FirstSeenAt: verifiedAt, LastSeenAt: verifiedAt, LastVerifiedAt: verifiedAt, ConsecutiveConfirmedAbsenceCount: 0,
            ArchivedAt: null, ArchivedReason: null, CurrentPriceAmount: 10m, CurrentDisplayPrice: "$10", ComparisonPriceAmount: null,
            PriceQualifier: null, DateSpecific: false, ApplicableDate: null, LastEvidence: Evidence(RetrievalStatus.Verified, verifiedAt)));

        var snapshot = await env.Service.LoadAsync(Date);

        var promotionsEntry = Assert.Single(snapshot.DataHealth.Sources, s => s.Category == SourceCategory.Promotions);
        Assert.Equal(DashboardSourceStatus.Current, promotionsEntry.Status);
        Assert.Equal(verifiedAt, promotionsEntry.LastVerifiedAt);
    }

    // Phase 10D Part X: a catalog category with no stored products is honestly NotChecked, never a
    // fabricated NotPublished/Unavailable claim this service has no evidence for.
    [Fact]
    public async Task LoadAsync_CatalogCategoryWithNoProducts_ReportsNotChecked()
    {
        var env = CreateEnvironment();

        var snapshot = await env.Service.LoadAsync(Date);

        var passesEntry = Assert.Single(snapshot.DataHealth.Sources, s => s.Category == SourceCategory.SeasonPasses);
        Assert.Equal(DashboardSourceStatus.NotChecked, passesEntry.Status);
        Assert.Null(passesEntry.LastVerifiedAt);
    }

    // Phase 10D Part P: the local-only load path must never call the manual refresh workflow.
    [Fact]
    public async Task LoadAsync_NeverTriggersOnlineRefresh()
    {
        var env = CreateEnvironment();

        await env.Service.GetTodayAsync();
        await env.Service.LoadAsync(Date);

        Assert.Empty(env.RefreshOrchestrator.ManualDateRefreshCalls);
    }

    // Phase 10D Part L/M: Check Online reuses the exact same manual-date refresh workflow
    // IAssociatePassdownService.CheckAssociateDateOnlineAsync already exposes, scoped to today.
    [Fact]
    public async Task CheckOnlineAsync_CallsExistingManualRefreshForToday()
    {
        var lateUtcClock = new FakeClock(new DateTimeOffset(2026, 8, 22, 2, 30, 0, TimeSpan.Zero)); // 10:30 PM EDT on 2026-08-21
        var env = CreateEnvironment(lateUtcClock);

        await env.Service.CheckOnlineAsync();

        Assert.Equal([new DateOnly(2026, 8, 21)], env.RefreshOrchestrator.ManualDateRefreshCalls);
    }

    // Phase 10D Part O: Check Online's returned snapshot reflects the refresh's own recomposed
    // workspace directly — no second LoadAssociateDateAsync round trip is needed to see the result.
    [Fact]
    public async Task CheckOnlineAsync_ReturnedSnapshotReflectsNewlyRetrievedData()
    {
        var env = CreateEnvironment();
        env.RefreshOrchestrator.OnManualDateRefresh = async (date, ct) =>
            await env.DatedResults.RecordSuccessAsync(new DatedResultKey(SourceCategory.ParkHours, date), Evidence(RetrievalStatus.Verified), payloadJson: null);

        var result = await env.Service.CheckOnlineAsync();

        var parkHoursEntry = Assert.Single(result.Snapshot.DataHealth.Sources, s => s.Category == SourceCategory.ParkHours);
        Assert.Equal(DashboardSourceStatus.Current, parkHoursEntry.Status);
    }

    // Phase 10D Part O/Q: a failed Check Online never destroys a prior success — last-known-good data
    // stays visible (as Stale, once a later attempt failed after a prior success).
    [Fact]
    public async Task CheckOnlineAsync_FailedRefresh_PreservesLastKnownGood()
    {
        var env = CreateEnvironment();
        var successAt = FixedNow.AddHours(-6);
        await env.DatedResults.RecordSuccessAsync(new DatedResultKey(SourceCategory.ParkHours, Date), Evidence(RetrievalStatus.Verified, successAt), payloadJson: null);
        env.RefreshOrchestrator.OnManualDateRefresh = async (date, ct) =>
            await env.DatedResults.RecordFailedAttemptAsync(new DatedResultKey(SourceCategory.ParkHours, date), Evidence(RetrievalStatus.Error, FixedNow));

        var result = await env.Service.CheckOnlineAsync();

        var parkHoursEntry = Assert.Single(result.Snapshot.DataHealth.Sources, s => s.Category == SourceCategory.ParkHours);
        Assert.Equal(DashboardSourceStatus.Stale, parkHoursEntry.Status);
        Assert.Equal(successAt, parkHoursEntry.LastVerifiedAt);
    }

    // Phase 10C Part M: no finalized passdown has ever existed -> LatestFinalized is null, never a
    // fabricated date.
    [Fact]
    public async Task LoadAsync_NoFinalizedHistory_LatestFinalizedIsNull()
    {
        var env = CreateEnvironment();

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Null(snapshot.LatestFinalized);
    }

    // Phase 10C Part B: exactly one finalized record -> it is surfaced as LatestFinalized, metadata only.
    [Fact]
    public async Task LoadAsync_OneFinalizedRecord_LatestFinalizedPopulated()
    {
        var env = CreateEnvironment();
        var finalizedDate = new DateOnly(2026, 8, 20);
        var finalizedAt = env.Clock.UtcNow;
        await env.FinalizedPassdowns.AddAsync(new FinalizedPassdown(
            Guid.NewGuid(), finalizedDate, finalizedAt, 1, "{}", "hash", FinalizationOrigin.Scheduled,
            PdfBytes: [1, 2, 3], PdfSha256: "pdfhash", PdfSizeBytes: 3));

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.NotNull(snapshot.LatestFinalized);
        Assert.Equal(finalizedDate, snapshot.LatestFinalized!.PassdownDate);
        Assert.Equal(finalizedAt, snapshot.LatestFinalized.FinalizedAtUtc);
        Assert.Equal(FinalizationOrigin.Scheduled, snapshot.LatestFinalized.Origin);
    }

    // Phase 10C Part C: "latest" is the highest PassdownDate, never the most-recently-inserted row or
    // the row with the most recent FinalizedAtUtc — the last-inserted/last-finalized row here (Aug 18)
    // is deliberately the earliest PassdownDate, so a naive "last row"/"most recent FinalizedAtUtc"
    // implementation would pick the wrong one.
    [Fact]
    public async Task LoadAsync_MultipleFinalizedRecords_LatestSelectedByHighestPassdownDate()
    {
        var env = CreateEnvironment();
        await env.FinalizedPassdowns.AddAsync(new FinalizedPassdown(
            Guid.NewGuid(), new DateOnly(2026, 8, 19), env.Clock.UtcNow.AddHours(1), 1, "{}", "hash", FinalizationOrigin.Scheduled,
            PdfBytes: [1], PdfSha256: "h1", PdfSizeBytes: 1));
        await env.FinalizedPassdowns.AddAsync(new FinalizedPassdown(
            Guid.NewGuid(), new DateOnly(2026, 8, 22), env.Clock.UtcNow.AddHours(-5), 1, "{}", "hash", FinalizationOrigin.Recovery,
            PdfBytes: [1], PdfSha256: "h2", PdfSizeBytes: 1));
        await env.FinalizedPassdowns.AddAsync(new FinalizedPassdown(
            Guid.NewGuid(), new DateOnly(2026, 8, 18), env.Clock.UtcNow.AddHours(10), 1, "{}", "hash", FinalizationOrigin.Scheduled,
            PdfBytes: [1], PdfSha256: "h3", PdfSizeBytes: 1));

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(new DateOnly(2026, 8, 22), snapshot.LatestFinalized!.PassdownDate);
        Assert.Equal(FinalizationOrigin.Recovery, snapshot.LatestFinalized.Origin);
    }

    // Phase 10C Part N: a legacy pre-9C record with no frozen PDF must still be surfaced, never hidden
    // or treated as invalid.
    [Fact]
    public async Task LoadAsync_LatestFinalizedHasNoPdf_StillPopulated()
    {
        var env = CreateEnvironment();
        var finalizedDate = new DateOnly(2026, 8, 20);
        await env.FinalizedPassdowns.AddAsync(new FinalizedPassdown(
            Guid.NewGuid(), finalizedDate, env.Clock.UtcNow, 1, "{}", "hash", FinalizationOrigin.Scheduled,
            PdfBytes: null, PdfSha256: null, PdfSizeBytes: null));

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.NotNull(snapshot.LatestFinalized);
        Assert.Equal(finalizedDate, snapshot.LatestFinalized!.PassdownDate);
        Assert.False(snapshot.LatestFinalized.HasFrozenPdf);
    }

    // Phase 10C Part O: a malformed SnapshotJson row must never break the dashboard — LatestFinalized
    // is built from FinalizedPassdownSummary metadata only, and this service never deserializes
    // SnapshotJson to render it.
    [Fact]
    public async Task LoadAsync_LatestFinalizedHasMalformedSnapshotJson_DoesNotThrow()
    {
        var env = CreateEnvironment();
        var finalizedDate = new DateOnly(2026, 8, 20);
        await env.FinalizedPassdowns.AddAsync(new FinalizedPassdown(
            Guid.NewGuid(), finalizedDate, env.Clock.UtcNow, 1, "{ this is not valid json", "hash", FinalizationOrigin.Scheduled,
            PdfBytes: [1], PdfSha256: "h", PdfSizeBytes: 1));

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(finalizedDate, snapshot.LatestFinalized!.PassdownDate);
    }

    // Phase 10C Part O: a row from a future/unknown snapshot schema version must never break the
    // dashboard either, for the same reason — metadata only, no deserialization.
    [Fact]
    public async Task LoadAsync_LatestFinalizedHasFutureSchemaVersion_DoesNotThrow()
    {
        var env = CreateEnvironment();
        var finalizedDate = new DateOnly(2026, 8, 20);
        await env.FinalizedPassdowns.AddAsync(new FinalizedPassdown(
            Guid.NewGuid(), finalizedDate, env.Clock.UtcNow, int.MaxValue, "{}", "hash", FinalizationOrigin.Scheduled,
            PdfBytes: [1], PdfSha256: "h", PdfSizeBytes: 1));

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(finalizedDate, snapshot.LatestFinalized!.PassdownDate);
    }

    // Phase 10C Part T #22: other dates being finalized must never cause today's own status to be
    // misreported as Finalized — only a record for operatingDate itself counts.
    [Fact]
    public async Task LoadAsync_OtherDatesFinalized_TodaysOwnStatusUnaffected()
    {
        var env = CreateEnvironment();
        await SaveDraftAsync(env, AssociateDraftPayload.CreateDefault());
        await env.FinalizedPassdowns.AddAsync(new FinalizedPassdown(
            Guid.NewGuid(), Date.AddDays(-1), env.Clock.UtcNow, 1, "{}", "hash", FinalizationOrigin.Scheduled,
            PdfBytes: [1], PdfSha256: "h", PdfSizeBytes: 1));
        await env.FinalizedPassdowns.AddAsync(new FinalizedPassdown(
            Guid.NewGuid(), Date.AddDays(1), env.Clock.UtcNow, 1, "{}", "hash", FinalizationOrigin.Scheduled,
            PdfBytes: [1], PdfSha256: "h", PdfSizeBytes: 1));

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(DashboardPassdownStatus.DraftInProgress, snapshot.PassdownStatus.Status);
        Assert.Null(snapshot.PassdownStatus.FinalizedAt);
        // The other dates are still correctly surfaced as History's own latest-finalized concern.
        Assert.Equal(Date.AddDays(1), snapshot.LatestFinalized!.PassdownDate);
    }

    // Phase 10C Part L: a large finalized-history set stays metadata-only — GetByDateAsync (the only
    // method that can return a PDF blob or SnapshotJson-bearing full row) is never called even once.
    [Fact]
    public async Task LoadAsync_LargeFinalizedHistorySet_RemainsMetadataOnly()
    {
        var env = CreateEnvironment();
        for (var i = 0; i < 50; i++)
        {
            await env.FinalizedPassdowns.AddAsync(new FinalizedPassdown(
                Guid.NewGuid(), new DateOnly(2026, 1, 1).AddDays(i), env.Clock.UtcNow, 1, "{}", "hash", FinalizationOrigin.Scheduled,
                PdfBytes: [1], PdfSha256: "h", PdfSizeBytes: 1));
        }

        var snapshot = await env.Service.LoadAsync(Date);

        Assert.Equal(new DateOnly(2026, 1, 1).AddDays(49), snapshot.LatestFinalized!.PassdownDate);
        Assert.Equal(0, env.FinalizedPassdowns.GetByDateCallCount);
    }

    private static SourceEvidence Evidence(RetrievalStatus status, DateTimeOffset? timeChecked = null) =>
        new("Fake Source", new Uri("https://example.test/fake-source"), timeChecked ?? FixedNow, status, $"{status}.", RetrievalStrategy.DirectApi, Date, status == RetrievalStatus.Verified ? Date : null);

    /// <summary>Wraps the real in-memory repository, counting <see cref="GetByDateAsync"/> calls — the only method that can return a historical PDF blob — so a test can assert the dashboard never calls it (Phase 10A Part S).</summary>
    private sealed class CountingFinalizedPassdownRepository : IFinalizedPassdownRepository
    {
        private readonly InMemoryFinalizedPassdownRepository _inner = new();

        public int GetByDateCallCount { get; private set; }

        public Task<FinalizedPassdown?> GetByDateAsync(DateOnly passdownDate, CancellationToken cancellationToken = default)
        {
            GetByDateCallCount++;
            return _inner.GetByDateAsync(passdownDate, cancellationToken);
        }

        public Task<bool> ExistsForDateAsync(DateOnly passdownDate, CancellationToken cancellationToken = default) =>
            _inner.ExistsForDateAsync(passdownDate, cancellationToken);

        public Task AddAsync(FinalizedPassdown finalizedPassdown, CancellationToken cancellationToken = default) =>
            _inner.AddAsync(finalizedPassdown, cancellationToken);

        public Task<IReadOnlyList<FinalizedPassdownSummary>> ListAsync(CancellationToken cancellationToken = default) =>
            _inner.ListAsync(cancellationToken);
    }
}
