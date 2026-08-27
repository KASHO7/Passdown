using System.Reflection;
using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Calendar;

/// <summary>
/// Covers <see cref="OperationsCalendarService"/> (Phase 12A Part D): every value must come from
/// existing local abstractions — real <see cref="AssociatePassdownService"/>, the same saved-draft-date
/// signal <see cref="Finalization.ScheduledPassdownFinalizer"/> already uses, finalization metadata
/// only, and the same relevance rule <see cref="OperationalNotesService"/> already applies — never a
/// second implementation. Uses the real collaborator types over in-memory repositories (not fakes) so
/// a passing test actually proves reuse.
/// </summary>
public class OperationsCalendarServiceTests
{
    private static readonly DateOnly SelectedDate = new(2026, 8, 21);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 14, 0, 0, TimeSpan.Zero); // 10:00 AM EDT on 2026-08-21

    private sealed record TestEnvironment(
        OperationsCalendarService Service,
        InMemoryAssociateDraftRepository Drafts,
        InMemoryDatedResultRepository DatedResults,
        InMemoryFinalizedPassdownRepository FinalizedPassdowns,
        IOperationalNotesService OperationalNotes,
        FakeClock Clock);

    private static TestEnvironment CreateEnvironment(FakeClock? clock = null)
    {
        clock ??= new FakeClock(FixedNow);
        var drafts = new InMemoryAssociateDraftRepository();
        var datedResults = new InMemoryDatedResultRepository();
        var catalogProducts = new InMemoryCatalogProductRepository();
        var refreshOrchestrator = new FakeRefreshOrchestrator();
        var operationalNotes = new OperationalNotesService(new InMemoryOperationalNoteRepository(), clock);
        var associateService = new AssociatePassdownService(drafts, datedResults, catalogProducts, refreshOrchestrator, operationalNotes, clock);
        var finalizedPassdowns = new InMemoryFinalizedPassdownRepository();
        var service = new OperationsCalendarService(associateService, drafts, datedResults, finalizedPassdowns, operationalNotes, clock);
        return new TestEnvironment(service, drafts, datedResults, finalizedPassdowns, operationalNotes, clock);
    }

    /// <summary>Seeds a verified (or officially closed) official park-hours dated result for <paramref name="date"/> (spec Phase 12B Part A/D) — the same shape <see cref="AssociatePassdownService"/> itself reads.</summary>
    private static Task SeedOfficialParkHoursAsync(TestEnvironment env, DateOnly date, bool isClosed, TimeOnly? opening = null, TimeOnly? closing = null)
    {
        var status = isClosed ? RetrievalStatus.Closed : RetrievalStatus.Verified;
        var payloadJson = isClosed ? null : JsonSerializer.Serialize(new ParkHoursPayload(opening!.Value, closing!.Value));
        var evidence = new SourceEvidence(
            sourceName: "test-park-hours", sourceUrl: new Uri("https://example.com/hours"),
            timeChecked: env.Clock.UtcNow, status: status, detail: "Test.",
            strategy: RetrievalStrategy.DirectApi, requestedDate: date, confirmedDate: date, technicalDetail: null);

        return env.DatedResults.RecordSuccessAsync(new DatedResultKey(SourceCategory.ParkHours, date), evidence, payloadJson);
    }

    /// <summary>
    /// Seeds a successful-but-negative completion (spec Part H — <see cref="RetrievalStatus.NotPublished"/>
    /// is a "successful completion" per <see cref="RetrievalStatusExtensions.IsSuccessfulCompletion"/>, so
    /// it is recorded via <c>RecordSuccessAsync</c>, never <c>RecordFailedAttemptAsync</c>) with no payload.
    /// </summary>
    private static Task SeedNotPublishedParkHoursAsync(TestEnvironment env, DateOnly date)
    {
        var evidence = new SourceEvidence(
            sourceName: "test-park-hours", sourceUrl: new Uri("https://example.com/hours"),
            timeChecked: env.Clock.UtcNow, status: RetrievalStatus.NotPublished, detail: "Test.",
            strategy: RetrievalStrategy.DirectApi, requestedDate: date, confirmedDate: null, technicalDetail: null);

        return env.DatedResults.RecordSuccessAsync(new DatedResultKey(SourceCategory.ParkHours, date), evidence, payloadJson: null);
    }

    /// <summary>Seeds a genuinely failed attempt (spec Part I — the source could not be reached at all), with no prior success, so <see cref="AssociateVerificationState"/> resolves to <c>Error</c>.</summary>
    private static Task SeedErroredParkHoursAsync(TestEnvironment env, DateOnly date)
    {
        var evidence = new SourceEvidence(
            sourceName: "test-park-hours", sourceUrl: new Uri("https://example.com/hours"),
            timeChecked: env.Clock.UtcNow, status: RetrievalStatus.Error, detail: "Test.",
            strategy: RetrievalStrategy.DirectApi, requestedDate: date, confirmedDate: null, technicalDetail: null);

        return env.DatedResults.RecordFailedAttemptAsync(new DatedResultKey(SourceCategory.ParkHours, date), evidence);
    }

    private static Task SaveDraftAsync(TestEnvironment env, DateOnly date, AssociateDraftPayload draft) =>
        env.Drafts.SaveAsync(new AssociateDraft(date, AssociateDraftPayload.CurrentSchemaVersion, JsonSerializer.Serialize(draft), env.Clock.UtcNow));

    private static Task AddFinalizedAsync(TestEnvironment env, DateOnly date, DateTimeOffset finalizedAt) =>
        env.FinalizedPassdowns.AddAsync(new FinalizedPassdown(
            Guid.NewGuid(), date, finalizedAt, 1, "{}", "hash", FinalizationOrigin.Scheduled,
            PdfBytes: [1, 2, 3], PdfSha256: "abc", PdfSizeBytes: 3));

    // #1: local-only by construction — no HTTP/Playwright/refresh dependency exists on this type.
    [Fact]
    public void Constructor_HasNoRefreshOrchestratorOrRetrievalDependency()
    {
        var constructor = typeof(OperationsCalendarService).GetConstructors().Single();
        var parameterTypes = constructor.GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.DoesNotContain(parameterTypes, t => t == typeof(IRefreshOrchestrator));
        Assert.DoesNotContain(parameterTypes, t => t.Name.Contains("Http", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(parameterTypes, t => t.Name.Contains("Browser", StringComparison.OrdinalIgnoreCase));
    }

    // #2: DisplayedMonth is always normalized to the first day of the month, regardless of the
    // day-of-month passed in.
    [Fact]
    public async Task LoadAsync_ArbitraryDayOfMonth_NormalizesDisplayedMonthToFirstDay()
    {
        var env = CreateEnvironment();

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 17), SelectedDate);

        Assert.Equal(new DateOnly(2026, 8, 1), snapshot.DisplayedMonth);
    }

    // #3: the month grid is complete Sunday-through-Saturday weeks. August 2026 starts on a Saturday
    // and has 31 days, so it needs 6 full weeks (42 cells) to cover it.
    [Fact]
    public async Task LoadAsync_AugustGrid_ProducesCompleteWeeksCoveringTheMonth()
    {
        var env = CreateEnvironment();

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        Assert.Equal(42, snapshot.Days.Count);
        Assert.Equal(DayOfWeek.Sunday, snapshot.Days[0].Date.DayOfWeek);
        Assert.Equal(DayOfWeek.Saturday, snapshot.Days[^1].Date.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 7, 26), snapshot.Days[0].Date); // last Sunday of July
        Assert.Equal(new DateOnly(2026, 9, 5), snapshot.Days[^1].Date); // first Saturday of September
    }

    // #3/#4: February 2026 starts on a Sunday and has 28 days — exactly 4 weeks, no leading/trailing days needed.
    [Fact]
    public async Task LoadAsync_FebruaryStartingOnSunday_ProducesExactlyFourWeeksNoOutOfMonthDays()
    {
        var env = CreateEnvironment();

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 5));

        Assert.Equal(28, snapshot.Days.Count);
        Assert.All(snapshot.Days, day => Assert.True(day.IsInDisplayedMonth));
    }

    // #4: in-month vs. out-of-month classification for the leading/trailing days of a month that
    // doesn't start on Sunday.
    [Fact]
    public async Task LoadAsync_AugustGrid_ClassifiesInMonthVsOutOfMonthCorrectly()
    {
        var env = CreateEnvironment();

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        var leadingJulyDay = snapshot.Days.First(d => d.Date == new DateOnly(2026, 7, 26));
        var firstOfAugust = snapshot.Days.First(d => d.Date == new DateOnly(2026, 8, 1));
        var trailingSeptemberDay = snapshot.Days.First(d => d.Date == new DateOnly(2026, 9, 5));

        Assert.False(leadingJulyDay.IsInDisplayedMonth);
        Assert.True(firstOfAugust.IsInDisplayedMonth);
        Assert.False(trailingSeptemberDay.IsInDisplayedMonth);
    }

    // #4: December -> January year-boundary month navigation still normalizes/grids correctly.
    [Fact]
    public async Task LoadAsync_DecemberToJanuaryBoundary_NormalizesAcrossYearCorrectly()
    {
        var env = CreateEnvironment();

        var decemberSnapshot = await env.Service.LoadAsync(new DateOnly(2026, 12, 15), new DateOnly(2026, 12, 15));
        var januarySnapshot = await env.Service.LoadAsync(new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 1));

        Assert.Equal(new DateOnly(2026, 12, 1), decemberSnapshot.DisplayedMonth);
        Assert.Equal(new DateOnly(2027, 1, 1), januarySnapshot.DisplayedMonth);
        Assert.Contains(decemberSnapshot.Days, d => d.Date == new DateOnly(2026, 12, 31));
        Assert.Contains(januarySnapshot.Days, d => d.Date == new DateOnly(2027, 1, 1));
    }

    // #5: "today" resolves to the current Eastern calendar date — exercised near a UTC day boundary
    // so the Eastern conversion is actually proven, not a same-day coincidence.
    [Fact]
    public async Task LoadAsync_TodayFlag_UsesEasternCalendarDate()
    {
        var lateUtcClock = new FakeClock(new DateTimeOffset(2026, 8, 22, 2, 30, 0, TimeSpan.Zero)); // 10:30 PM EDT on 2026-08-21
        var env = CreateEnvironment(lateUtcClock);

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        Assert.Equal(new DateOnly(2026, 8, 21), snapshot.Today);
        var todayCell = snapshot.Days.Single(d => d.IsToday);
        Assert.Equal(new DateOnly(2026, 8, 21), todayCell.Date);
    }

    // #6: a day with a finalized passdown is flagged in the grid.
    [Fact]
    public async Task LoadAsync_DateHasFinalizedPassdown_DayCellFlagsIt()
    {
        var env = CreateEnvironment();
        var finalizedDate = new DateOnly(2026, 8, 10);
        await AddFinalizedAsync(env, finalizedDate, env.Clock.UtcNow);

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        var day = snapshot.Days.Single(d => d.Date == finalizedDate);
        Assert.True(day.HasFinalizedPassdown);
        Assert.False(snapshot.Days.Single(d => d.Date == new DateOnly(2026, 8, 11)).HasFinalizedPassdown);
    }

    // #7: a day with a saved (but not finalized) draft is flagged separately from a finalized day.
    [Fact]
    public async Task LoadAsync_DateHasSavedDraft_DayCellFlagsItAndNotFinalized()
    {
        var env = CreateEnvironment();
        var draftDate = new DateOnly(2026, 8, 12);
        await SaveDraftAsync(env, draftDate, AssociateDraftPayload.CreateDefault());

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        var day = snapshot.Days.Single(d => d.Date == draftDate);
        Assert.True(day.HasAssociateDraft);
        Assert.False(day.HasFinalizedPassdown);
    }

    // #8: relevant persistent notes are counted per day using the exact same relevance rule
    // OperationalNoteRelevance.IsRelevantForDate/IOperationalNotesService.ListRelevantForDateAsync use.
    [Fact]
    public async Task LoadAsync_RelevantOperationalNote_CountedOnEveryDayInItsRange()
    {
        var env = CreateEnvironment();
        await env.OperationalNotes.CreateAsync(new OperationalNoteDraft(
            "Scanner down", null, OperationalNoteCategory.Equipment, OperationalNotePriority.Important,
            new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 12)));

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        Assert.Equal(1, snapshot.Days.Single(d => d.Date == new DateOnly(2026, 8, 10)).RelevantOperationalNoteCount);
        Assert.Equal(1, snapshot.Days.Single(d => d.Date == new DateOnly(2026, 8, 12)).RelevantOperationalNoteCount);
        Assert.Equal(0, snapshot.Days.Single(d => d.Date == new DateOnly(2026, 8, 13)).RelevantOperationalNoteCount);
    }

    // #8: a resolved note is never counted as relevant, matching OperationalNoteRelevance exactly.
    [Fact]
    public async Task LoadAsync_ResolvedOperationalNote_NeverCounted()
    {
        var env = CreateEnvironment();
        var note = await env.OperationalNotes.CreateAsync(new OperationalNoteDraft(
            "Resolved thing", null, OperationalNoteCategory.General, OperationalNotePriority.Normal,
            new DateOnly(2026, 8, 1), null));
        await env.OperationalNotes.ResolveAsync(note.Id);

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        Assert.All(snapshot.Days, day => Assert.Equal(0, day.RelevantOperationalNoteCount));
    }

    // #9: a date with no saved draft and no finalized record is NoDraft.
    [Fact]
    public async Task LoadAsync_SelectedDateHasNoDraft_SelectedSummaryIsNoDraft()
    {
        var env = CreateEnvironment();

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        Assert.Equal(OperationsCalendarPassdownStatus.NoDraft, snapshot.Selected.PassdownStatus);
        Assert.Null(snapshot.Selected.LastSavedAt);
        Assert.Null(snapshot.Selected.FinalizedAt);
    }

    // #9: a saved-but-not-finalized draft is DraftInProgress.
    [Fact]
    public async Task LoadAsync_SelectedDateHasSavedDraft_SelectedSummaryIsDraftInProgress()
    {
        var env = CreateEnvironment();
        await SaveDraftAsync(env, SelectedDate, AssociateDraftPayload.CreateDefault());

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        Assert.Equal(OperationsCalendarPassdownStatus.DraftInProgress, snapshot.Selected.PassdownStatus);
        Assert.NotNull(snapshot.Selected.LastSavedAt);
    }

    // #9: a finalized date is Finalized, with the finalization timestamp surfaced, even though a
    // draft row also exists for it.
    [Fact]
    public async Task LoadAsync_SelectedDateIsFinalized_SelectedSummaryIsFinalized()
    {
        var env = CreateEnvironment();
        await SaveDraftAsync(env, SelectedDate, AssociateDraftPayload.CreateDefault());
        var finalizedAt = env.Clock.UtcNow.AddHours(10);
        await AddFinalizedAsync(env, SelectedDate, finalizedAt);

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        Assert.Equal(OperationsCalendarPassdownStatus.Finalized, snapshot.Selected.PassdownStatus);
        Assert.Equal(finalizedAt, snapshot.Selected.FinalizedAt);
    }

    // #10: the finalized-metadata lookup never calls GetByDateAsync, so a calendar load can never
    // materialize a historical PDF blob (mirrors OperationsDashboardService's own guarantee).
    [Fact]
    public async Task LoadAsync_NeverCallsGetByDateOnFinalizedPassdownRepository()
    {
        var env = CreateEnvironment();
        var countingRepository = new CountingFinalizedPassdownRepository(env.FinalizedPassdowns);
        var drafts = env.Drafts;
        var datedResults = new InMemoryDatedResultRepository();
        var catalogProducts = new InMemoryCatalogProductRepository();
        var refreshOrchestrator = new FakeRefreshOrchestrator();
        var associateService = new AssociatePassdownService(drafts, datedResults, catalogProducts, refreshOrchestrator, env.OperationalNotes, env.Clock);
        var service = new OperationsCalendarService(associateService, drafts, datedResults, countingRepository, env.OperationalNotes, env.Clock);
        await AddFinalizedAsync(env, SelectedDate, env.Clock.UtcNow);

        await service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        Assert.Equal(0, countingRepository.GetByDateCallCount);
    }

    // #11: the selected date's note count uses the same relevance rule as the day-cell counts, and
    // stays correct even when the selected date falls outside the currently displayed month.
    [Fact]
    public async Task LoadAsync_SelectedDateOutsideDisplayedMonth_StillComputesCorrectSelectedSummary()
    {
        var env = CreateEnvironment();
        var outsideMonthDate = new DateOnly(2026, 9, 15);
        await SaveDraftAsync(env, outsideMonthDate, AssociateDraftPayload.CreateDefault());
        await env.OperationalNotes.CreateAsync(new OperationalNoteDraft(
            "September note", null, OperationalNoteCategory.General, OperationalNotePriority.Normal,
            outsideMonthDate, outsideMonthDate));

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), outsideMonthDate);

        Assert.Equal(new DateOnly(2026, 8, 1), snapshot.DisplayedMonth);
        Assert.Equal(outsideMonthDate, snapshot.SelectedDate);
        Assert.Equal(OperationsCalendarPassdownStatus.DraftInProgress, snapshot.Selected.PassdownStatus);
        Assert.Equal(1, snapshot.Selected.RelevantOperationalNoteCount);
    }

    // #12: empty state — no drafts, no finalized passdowns, no notes anywhere in the visible grid.
    [Fact]
    public async Task LoadAsync_NothingSaved_EveryDayIsEmptyAndSelectedIsNoDraft()
    {
        var env = CreateEnvironment();

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        Assert.All(snapshot.Days, day =>
        {
            Assert.False(day.HasFinalizedPassdown);
            Assert.False(day.HasAssociateDraft);
            Assert.Equal(0, day.RelevantOperationalNoteCount);
        });
        Assert.Equal(OperationsCalendarPassdownStatus.NoDraft, snapshot.Selected.PassdownStatus);
    }

    // #13: many drafts/finalized/notes across the visible grid ("many metadata" case) — every day
    // cell still reflects exactly its own date's state, never bleeding into a neighboring day.
    [Fact]
    public async Task LoadAsync_ManyRecordsAcrossTheGrid_EachDayReflectsOnlyItsOwnState()
    {
        var env = CreateEnvironment();
        for (var day = 1; day <= 10; day++)
        {
            var date = new DateOnly(2026, 8, day);
            await SaveDraftAsync(env, date, AssociateDraftPayload.CreateDefault());
            if (day <= 5)
            {
                await AddFinalizedAsync(env, date, env.Clock.UtcNow);
            }
        }

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        for (var day = 1; day <= 10; day++)
        {
            var cell = snapshot.Days.Single(d => d.Date == new DateOnly(2026, 8, day));
            Assert.True(cell.HasAssociateDraft);
            Assert.Equal(day <= 5, cell.HasFinalizedPassdown);
        }

        Assert.False(snapshot.Days.Single(d => d.Date == new DateOnly(2026, 8, 11)).HasAssociateDraft);
    }

    // ==================================================================================
    // Phase 12B — Park Hours and Operating-State overlays
    // ==================================================================================

    // Part Z #1: a normal published-hours day classifies Open with the exact published times.
    [Fact]
    public async Task LoadAsync_NormalPublishedHours_ClassifiesOpenWithExactTimes()
    {
        var env = CreateEnvironment();
        var date = new DateOnly(2026, 8, 10);
        await SeedOfficialParkHoursAsync(env, date, isClosed: false, new TimeOnly(10, 0), new TimeOnly(22, 0));

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), date);

        var day = snapshot.Days.Single(d => d.Date == date);
        Assert.Equal(OperationsCalendarOperatingState.Open, day.OperatingState);
        Assert.Equal(new TimeOnly(10, 0), day.ParkHours.Effective.Opening);
        Assert.Equal(new TimeOnly(22, 0), day.ParkHours.Effective.Closing);
    }

    // Part Z #2: an officially closed day classifies Closed with no fabricated hours.
    [Fact]
    public async Task LoadAsync_OfficiallyClosedDay_ClassifiesClosedWithNoHours()
    {
        var env = CreateEnvironment();
        var date = new DateOnly(2026, 8, 11);
        await SeedOfficialParkHoursAsync(env, date, isClosed: true);

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), date);

        var day = snapshot.Days.Single(d => d.Date == date);
        Assert.Equal(OperationsCalendarOperatingState.Closed, day.OperatingState);
        Assert.Null(day.ParkHours.Effective.Opening);
        Assert.Null(day.ParkHours.Effective.Closing);
    }

    // Part Z #3/#8: a saved draft's manual-hours override wins over official hours — the exact
    // same precedence ParkHoursCalculator already enforces, resolved from the local draft path.
    [Fact]
    public async Task LoadAsync_SavedDraftHasManualOverride_ClassifiesCustomHoursOverridingOfficial()
    {
        var env = CreateEnvironment();
        var date = new DateOnly(2026, 8, 12);
        await SeedOfficialParkHoursAsync(env, date, isClosed: false, new TimeOnly(10, 0), new TimeOnly(22, 0));
        var draft = AssociateDraftPayload.CreateDefault() with
        {
            ManualHoursOverride = new ManualHoursOverride(true, new TimeOnly(9, 0), new TimeOnly(23, 0)),
        };
        await SaveDraftAsync(env, date, draft);

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), date);

        var day = snapshot.Days.Single(d => d.Date == date);
        Assert.Equal(OperationsCalendarOperatingState.CustomHours, day.OperatingState);
        Assert.Equal(new TimeOnly(9, 0), day.ParkHours.Effective.Opening);
        Assert.Equal(new TimeOnly(23, 0), day.ParkHours.Effective.Closing);
        Assert.True(day.ParkHours.Effective.IsManualOverrideApplied);
    }

    // Part Z #4/#9: a saved draft's private-buyout override wins even over an officially closed day.
    [Fact]
    public async Task LoadAsync_SavedDraftHasPrivateBuyout_ClassifiesPrivateBuyoutEvenWhenOfficiallyClosed()
    {
        var env = CreateEnvironment();
        var date = new DateOnly(2026, 8, 13);
        await SeedOfficialParkHoursAsync(env, date, isClosed: true);
        var draft = AssociateDraftPayload.CreateDefault() with
        {
            PrivateBuyout = new PrivateBuyoutOverride(true, new TimeOnly(18, 0), new TimeOnly(23, 0), "Corporate buyout"),
        };
        await SaveDraftAsync(env, date, draft);

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), date);

        var day = snapshot.Days.Single(d => d.Date == date);
        Assert.Equal(OperationsCalendarOperatingState.PrivateBuyout, day.OperatingState);
        Assert.Equal(new TimeOnly(18, 0), day.ParkHours.Effective.Opening);
        Assert.Equal(new TimeOnly(23, 0), day.ParkHours.Effective.Closing);
        Assert.True(day.ParkHours.Effective.IsPrivateBuyoutApplied);
    }

    // Part Z #5: hours checked and confirmed not published yet — never treated as an error.
    [Fact]
    public async Task LoadAsync_NotPublishedDay_ClassifiesNotPublished()
    {
        var env = CreateEnvironment();
        var date = new DateOnly(2026, 8, 14);
        await SeedNotPublishedParkHoursAsync(env, date);

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), date);

        Assert.Equal(OperationsCalendarOperatingState.NotPublished, snapshot.Days.Single(d => d.Date == date).OperatingState);
    }

    // Part Z #6: a source that could not be reached at all classifies Unavailable — never surfaced as raw error detail.
    [Fact]
    public async Task LoadAsync_SourceUnavailable_ClassifiesUnavailable()
    {
        var env = CreateEnvironment();
        var date = new DateOnly(2026, 8, 15);
        await SeedErroredParkHoursAsync(env, date);

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), date);

        Assert.Equal(OperationsCalendarOperatingState.Unavailable, snapshot.Days.Single(d => d.Date == date).OperatingState);
    }

    // Part Z #7/R: a date with no saved draft still resolves official hours cleanly — a draft is never required to show park hours.
    [Fact]
    public async Task LoadAsync_NoDraftButOfficialHoursExist_ClassifiesOpenFromOfficialAlone()
    {
        var env = CreateEnvironment();
        var date = new DateOnly(2026, 8, 16);
        await SeedOfficialParkHoursAsync(env, date, isClosed: false, new TimeOnly(9, 0), new TimeOnly(21, 0));

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), date);

        var day = snapshot.Days.Single(d => d.Date == date);
        Assert.Equal(OperationsCalendarOperatingState.Open, day.OperatingState);
        Assert.False(day.HasAssociateDraft);
    }

    // Part Z #7/R: no draft and no official data at all — NotChecked, never a fabricated state.
    [Fact]
    public async Task LoadAsync_NoDraftAndNoOfficialData_ClassifiesNotChecked()
    {
        var env = CreateEnvironment();
        var date = new DateOnly(2026, 8, 17);

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), date);

        Assert.Equal(OperationsCalendarOperatingState.NotChecked, snapshot.Days.Single(d => d.Date == date).OperatingState);
    }

    // Part Z #21: a future published date shows its hours — nothing here implies they're guaranteed final, but they are shown.
    [Fact]
    public async Task LoadAsync_FuturePublishedDate_ShowsHours()
    {
        var env = CreateEnvironment(new FakeClock(FixedNow));
        var futureDate = new DateOnly(2026, 9, 20);
        await SeedOfficialParkHoursAsync(env, futureDate, isClosed: false, new TimeOnly(10, 0), new TimeOnly(20, 0));

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 9, 1), futureDate);

        var day = snapshot.Days.Single(d => d.Date == futureDate);
        Assert.Equal(OperationsCalendarOperatingState.Open, day.OperatingState);
    }

    // Part Z #22: a future date that has been checked but has nothing published yet shows Not Published, never fabricated hours.
    [Fact]
    public async Task LoadAsync_FutureNotPublishedDate_ShowsNotPublished()
    {
        var env = CreateEnvironment(new FakeClock(FixedNow));
        var futureDate = new DateOnly(2026, 9, 21);
        await SeedNotPublishedParkHoursAsync(env, futureDate);

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 9, 1), futureDate);

        var day = snapshot.Days.Single(d => d.Date == futureDate);
        Assert.Equal(OperationsCalendarOperatingState.NotPublished, day.OperatingState);
    }

    // Part Z #23 / spec Part S: 12B deliberately shows LIVE/current composed hours for a finalized
    // date too, never a frozen History snapshot — documented choice (see docs/phase12-operations-calendar.md
    // "Finalized historical-date hours"). This proves that rule concretely: finalizing a date and then
    // changing its official hours afterward changes what Calendar shows for that date.
    [Fact]
    public async Task LoadAsync_FinalizedDate_ShowsCurrentLiveHours_NotFrozenAtFinalizationTime()
    {
        var env = CreateEnvironment();
        var date = new DateOnly(2026, 8, 18);
        await SeedOfficialParkHoursAsync(env, date, isClosed: false, new TimeOnly(10, 0), new TimeOnly(22, 0));
        await SaveDraftAsync(env, date, AssociateDraftPayload.CreateDefault());
        await AddFinalizedAsync(env, date, env.Clock.UtcNow);

        // Official hours change after finalization (e.g. a later refresh corrected them).
        await SeedOfficialParkHoursAsync(env, date, isClosed: false, new TimeOnly(11, 0), new TimeOnly(23, 0));

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), date);

        var day = snapshot.Days.Single(d => d.Date == date);
        Assert.True(day.HasFinalizedPassdown);
        Assert.Equal(new TimeOnly(11, 0), day.ParkHours.Effective.Opening); // reflects the post-finalization change, not the frozen value.
        Assert.Equal(OperationsCalendarPassdownStatus.Finalized, snapshot.Selected.PassdownStatus);
    }

    // Part Z #10 / spec Part A: given the exact same underlying local state, Calendar's and
    // Dashboard's park-hours classifications agree — both are built over the exact same
    // AssociateParkHoursComposer.Compose output, never two independent hours engines.
    [Theory]
    [InlineData(false, 9, 0, 21, DashboardParkStatus.Open, OperationsCalendarOperatingState.Open)]
    [InlineData(true, null, null, null, DashboardParkStatus.Closed, OperationsCalendarOperatingState.Closed)]
    public async Task LoadAsync_AgreesWithDashboardParkStatus_ForIdenticalUnderlyingState(
        bool officialClosed, int? officialOpenHour, int? officialOpenMinute, int? officialCloseHour,
        DashboardParkStatus expectedDashboardStatus, OperationsCalendarOperatingState expectedCalendarState)
    {
        var env = CreateEnvironment();
        var date = new DateOnly(2026, 8, 19);

        if (officialClosed)
        {
            await SeedOfficialParkHoursAsync(env, date, isClosed: true);
        }
        else if (officialOpenHour is not null)
        {
            await SeedOfficialParkHoursAsync(env, date, isClosed: false, new TimeOnly(officialOpenHour.Value, officialOpenMinute ?? 0), new TimeOnly(officialCloseHour!.Value, 0));
        }

        var dashboardService = new OperationsDashboardService(
            new AssociatePassdownService(env.Drafts, env.DatedResults, new InMemoryCatalogProductRepository(), new FakeRefreshOrchestrator(), env.OperationalNotes, env.Clock),
            env.FinalizedPassdowns, env.OperationalNotes, env.Clock);

        var dashboardSnapshot = await dashboardService.LoadAsync(date);
        var calendarSnapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), date);

        Assert.Equal(expectedDashboardStatus, dashboardSnapshot.ParkStatus.Status);
        Assert.Equal(expectedCalendarState, calendarSnapshot.Selected.OperatingState);
        Assert.Equal(dashboardSnapshot.ParkStatus.Hours.Effective.Opening, calendarSnapshot.Selected.ParkHours.Effective.Opening);
        Assert.Equal(dashboardSnapshot.ParkStatus.Hours.Effective.Closing, calendarSnapshot.Selected.ParkHours.Effective.Closing);
    }

    // ==================================================================================
    // Phase 12C — Passdown Status + Persistent Operational Notes overlays
    // ==================================================================================

    private static Task<OperationalNote> AddNoteAsync(
        TestEnvironment env, DateOnly effectiveFrom, DateOnly? effectiveThrough = null,
        OperationalNotePriority priority = OperationalNotePriority.Normal, string title = "Test note") =>
        env.OperationalNotes.CreateAsync(new OperationalNoteDraft(title, null, OperationalNoteCategory.General, priority, effectiveFrom, effectiveThrough));

    // Part AK #4: a date with both a saved draft and a finalized record shows Finalized, never Draft,
    // at the day-cell level — the same precedence already proven for the selected-date summary.
    [Fact]
    public async Task LoadAsync_DateHasBothSavedDraftAndFinalizedRecord_DayCellPassdownFlagsFavorFinalized()
    {
        var env = CreateEnvironment();
        var date = new DateOnly(2026, 8, 18);
        await SaveDraftAsync(env, date, AssociateDraftPayload.CreateDefault());
        await AddFinalizedAsync(env, date, env.Clock.UtcNow);

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        var day = snapshot.Days.Single(d => d.Date == date);
        Assert.True(day.HasFinalizedPassdown);
        Assert.True(day.HasAssociateDraft); // the underlying draft row still exists (spec Part B) — Calendar status presentation, not this raw flag, applies precedence.
    }

    // Part AK #1/2/3: the three passdown-status day-cell flag combinations.
    [Fact]
    public async Task LoadAsync_NoSavedDraftAndNoFinalizedRecord_DayCellFlagsAreBothFalse()
    {
        var env = CreateEnvironment();
        var date = new DateOnly(2026, 8, 18);

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        var day = snapshot.Days.Single(d => d.Date == date);
        Assert.False(day.HasFinalizedPassdown);
        Assert.False(day.HasAssociateDraft);
    }

    [Fact]
    public async Task LoadAsync_SavedDraftOnly_DayCellHasDraftFlagTrueAndFinalizedFlagFalse()
    {
        var env = CreateEnvironment();
        var date = new DateOnly(2026, 8, 18);
        await SaveDraftAsync(env, date, AssociateDraftPayload.CreateDefault());

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        var day = snapshot.Days.Single(d => d.Date == date);
        Assert.False(day.HasFinalizedPassdown);
        Assert.True(day.HasAssociateDraft);
    }

    // Part AK #5/6/7: relevant note counts of 0, 1, and >1 for a given date.
    [Fact]
    public async Task LoadAsync_NoRelevantNotes_CountIsZero()
    {
        var env = CreateEnvironment();
        var date = new DateOnly(2026, 8, 18);

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        Assert.Equal(0, snapshot.Days.Single(d => d.Date == date).RelevantOperationalNoteCount);
    }

    [Fact]
    public async Task LoadAsync_OneRelevantNote_CountIsOne()
    {
        var env = CreateEnvironment();
        var date = new DateOnly(2026, 8, 18);
        await AddNoteAsync(env, date);

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        Assert.Equal(1, snapshot.Days.Single(d => d.Date == date).RelevantOperationalNoteCount);
    }

    [Fact]
    public async Task LoadAsync_MultipleRelevantNotes_CountReflectsAll()
    {
        var env = CreateEnvironment();
        var date = new DateOnly(2026, 8, 18);
        await AddNoteAsync(env, date, title: "A");
        await AddNoteAsync(env, date, title: "B");
        await AddNoteAsync(env, date, title: "C");

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        Assert.Equal(3, snapshot.Days.Single(d => d.Date == date).RelevantOperationalNoteCount);
    }

    // Part AK #8: ImportantOperationalNoteCount is a subset of RelevantOperationalNoteCount.
    [Fact]
    public async Task LoadAsync_MixOfNormalAndImportantNotes_ImportantCountReflectsOnlyImportant()
    {
        var env = CreateEnvironment();
        var date = new DateOnly(2026, 8, 18);
        await AddNoteAsync(env, date, priority: OperationalNotePriority.Normal, title: "Normal 1");
        await AddNoteAsync(env, date, priority: OperationalNotePriority.Important, title: "Important 1");
        await AddNoteAsync(env, date, priority: OperationalNotePriority.Important, title: "Important 2");

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        var day = snapshot.Days.Single(d => d.Date == date);
        Assert.Equal(3, day.RelevantOperationalNoteCount);
        Assert.Equal(2, day.ImportantOperationalNoteCount);

        var selected = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), date);
        Assert.Equal(3, selected.Selected.RelevantOperationalNoteCount);
        Assert.Equal(2, selected.Selected.ImportantOperationalNoteCount);
    }

    // Part AK #9: a resolved note is never counted, relevant or Important.
    [Fact]
    public async Task LoadAsync_ResolvedNote_ExcludedFromBothCounts()
    {
        var env = CreateEnvironment();
        var date = new DateOnly(2026, 8, 18);
        var note = await AddNoteAsync(env, date, priority: OperationalNotePriority.Important);
        await env.OperationalNotes.ResolveAsync(note.Id);

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        var day = snapshot.Days.Single(d => d.Date == date);
        Assert.Equal(0, day.RelevantOperationalNoteCount);
        Assert.Equal(0, day.ImportantOperationalNoteCount);
    }

    // Part AK #10: an upcoming note (EffectiveFrom in the future relative to the date being checked) is excluded.
    [Fact]
    public async Task LoadAsync_UpcomingNote_ExcludedForEarlierDate()
    {
        var env = CreateEnvironment();
        var checkedDate = new DateOnly(2026, 8, 18);
        await AddNoteAsync(env, effectiveFrom: checkedDate.AddDays(1));

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        Assert.Equal(0, snapshot.Days.Single(d => d.Date == checkedDate).RelevantOperationalNoteCount);
    }

    // Part AK #11: an expired note (EffectiveThrough in the past relative to the date being checked) is excluded.
    [Fact]
    public async Task LoadAsync_ExpiredNote_ExcludedForLaterDate()
    {
        var env = CreateEnvironment();
        var checkedDate = new DateOnly(2026, 8, 18);
        await AddNoteAsync(env, effectiveFrom: checkedDate.AddDays(-10), effectiveThrough: checkedDate.AddDays(-1));

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        Assert.Equal(0, snapshot.Days.Single(d => d.Date == checkedDate).RelevantOperationalNoteCount);
    }

    // Part AK #12: an open-ended note (null EffectiveThrough) is counted for a date long after EffectiveFrom.
    [Fact]
    public async Task LoadAsync_OpenEndedNote_CountedForDateWellAfterEffectiveFrom()
    {
        var env = CreateEnvironment();
        var checkedDate = new DateOnly(2026, 8, 18);
        await AddNoteAsync(env, effectiveFrom: checkedDate.AddDays(-30), effectiveThrough: null);

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        Assert.Equal(1, snapshot.Days.Single(d => d.Date == checkedDate).RelevantOperationalNoteCount);
    }

    // Part AK #13/14: boundary dates — a note ending on, or beginning on, the checked date is counted.
    [Fact]
    public async Task LoadAsync_NoteEndingOnCheckedDate_IsCounted()
    {
        var env = CreateEnvironment();
        var checkedDate = new DateOnly(2026, 8, 18);
        await AddNoteAsync(env, effectiveFrom: checkedDate.AddDays(-5), effectiveThrough: checkedDate);

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        Assert.Equal(1, snapshot.Days.Single(d => d.Date == checkedDate).RelevantOperationalNoteCount);
    }

    [Fact]
    public async Task LoadAsync_NoteBeginningOnCheckedDate_IsCounted()
    {
        var env = CreateEnvironment();
        var checkedDate = new DateOnly(2026, 8, 18);
        await AddNoteAsync(env, effectiveFrom: checkedDate);

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        Assert.Equal(1, snapshot.Days.Single(d => d.Date == checkedDate).RelevantOperationalNoteCount);
    }

    // Part AK #15: note relevance is evaluated per Calendar date, never against "today" — a note
    // relevant only for a past date does not leak into every other cell's count.
    [Fact]
    public async Task LoadAsync_NoteCountUsesEachCalendarDate_NotTodaysDate()
    {
        var env = CreateEnvironment();
        var noteDate = new DateOnly(2026, 8, 5);
        var otherDate = new DateOnly(2026, 8, 6);
        await AddNoteAsync(env, effectiveFrom: noteDate, effectiveThrough: noteDate);

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        Assert.Equal(1, snapshot.Days.Single(d => d.Date == noteDate).RelevantOperationalNoteCount);
        Assert.Equal(0, snapshot.Days.Single(d => d.Date == otherDate).RelevantOperationalNoteCount);
    }

    // Part AK #16: off-month cells still carry real underlying data in the snapshot (spec Part I says
    // the *view* hides the overlay for off-month cells — the Application-layer snapshot itself never
    // fabricates or suppresses a cell's own real values).
    [Fact]
    public async Task LoadAsync_OffMonthCell_StillCarriesRealPassdownAndNoteData()
    {
        var env = CreateEnvironment();
        // The grid for August 2026 includes trailing July days; pick one that's off-month.
        var offMonthDate = new DateOnly(2026, 7, 31);
        await SaveDraftAsync(env, offMonthDate, AssociateDraftPayload.CreateDefault());
        await AddNoteAsync(env, offMonthDate);

        var snapshot = await env.Service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        var offMonthDay = snapshot.Days.SingleOrDefault(d => d.Date == offMonthDate);
        Assert.NotNull(offMonthDay);
        Assert.False(offMonthDay!.IsInDisplayedMonth);
        Assert.True(offMonthDay.HasAssociateDraft);
        Assert.Equal(1, offMonthDay.RelevantOperationalNoteCount);
    }

    // Part AK #33/34: computing ImportantOperationalNoteCount adds no additional Operational Notes
    // query — it is derived in memory from the exact same all-notes list already loaded once.
    [Fact]
    public async Task LoadAsync_ComputingImportantCounts_DoesNotAddAnOperationalNotesQuery()
    {
        var env = CreateEnvironment();
        var countingNotes = new CountingOperationalNotesService(env.OperationalNotes);
        var associateService = new AssociatePassdownService(env.Drafts, env.DatedResults, new InMemoryCatalogProductRepository(), new FakeRefreshOrchestrator(), countingNotes, env.Clock);
        var service = new OperationsCalendarService(associateService, env.Drafts, env.DatedResults, env.FinalizedPassdowns, countingNotes, env.Clock);

        await service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        Assert.Equal(1, countingNotes.ListAllCallCount);
    }

    // Part AK #35: a large data set (100+ notes across a month) still loads bounded and local.
    [Fact]
    public async Task LoadAsync_HundredPlusOperationalNotes_LoadsCorrectlyAndBounded()
    {
        var env = CreateEnvironment();
        var monthStart = new DateOnly(2026, 8, 1);
        for (var i = 0; i < 120; i++)
        {
            var day = monthStart.AddDays(i % 31);
            await AddNoteAsync(env, effectiveFrom: day, priority: i % 5 == 0 ? OperationalNotePriority.Important : OperationalNotePriority.Normal, title: $"Note {i}");
        }

        var countingNotes = new CountingOperationalNotesService(env.OperationalNotes);
        var associateService = new AssociatePassdownService(env.Drafts, env.DatedResults, new InMemoryCatalogProductRepository(), new FakeRefreshOrchestrator(), countingNotes, env.Clock);
        var service = new OperationsCalendarService(associateService, env.Drafts, env.DatedResults, env.FinalizedPassdowns, countingNotes, env.Clock);

        var snapshot = await service.LoadAsync(monthStart, monthStart);

        Assert.Equal(1, countingNotes.ListAllCallCount);
        Assert.True(snapshot.Days.Sum(d => d.RelevantOperationalNoteCount) > 0);
        Assert.True(snapshot.Days.Sum(d => d.ImportantOperationalNoteCount) > 0);
    }

    /// <summary>Counts <c>ListAsync</c> calls specifically (spec Phase 12C Part O), wrapping a real service so relevance/ordering stay authentic.</summary>
    private sealed class CountingOperationalNotesService(IOperationalNotesService inner) : IOperationalNotesService
    {
        public int ListAllCallCount { get; private set; }

        public Task<OperationalNote> CreateAsync(OperationalNoteDraft draft, CancellationToken cancellationToken = default) =>
            inner.CreateAsync(draft, cancellationToken);

        public Task<OperationalNote> UpdateAsync(Guid id, OperationalNoteDraft draft, CancellationToken cancellationToken = default) =>
            inner.UpdateAsync(id, draft, cancellationToken);

        public Task<OperationalNote> ResolveAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.ResolveAsync(id, cancellationToken);

        public Task<IReadOnlyList<OperationalNote>> ListAsync(CancellationToken cancellationToken = default)
        {
            ListAllCallCount++;
            return inner.ListAsync(cancellationToken);
        }

        public Task<IReadOnlyList<OperationalNote>> ListRelevantForDateAsync(DateOnly operatingDate, CancellationToken cancellationToken = default) =>
            inner.ListRelevantForDateAsync(operatingDate, cancellationToken);
    }

    // Part Z #20: exactly one query per collaborator per month load, regardless of grid size —
    // never one repository round trip per day cell.
    [Fact]
    public async Task LoadAsync_QueriesDatedResultsAndDraftsExactlyOncePerMonthLoad()
    {
        var env = CreateEnvironment();
        var countingDatedResults = new CountingDatedResultRepository(env.DatedResults);
        var countingDrafts = new CountingAssociateDraftRepository(env.Drafts);
        var datedResultsForAssociate = env.DatedResults;
        var catalogProducts = new InMemoryCatalogProductRepository();
        var refreshOrchestrator = new FakeRefreshOrchestrator();
        var associateService = new AssociatePassdownService(countingDrafts, datedResultsForAssociate, catalogProducts, refreshOrchestrator, env.OperationalNotes, env.Clock);
        var service = new OperationsCalendarService(associateService, countingDrafts, countingDatedResults, env.FinalizedPassdowns, env.OperationalNotes, env.Clock);

        await service.LoadAsync(new DateOnly(2026, 8, 1), SelectedDate);

        // One range query for the whole grid, plus one GetAsync from the selected-date workspace load
        // (Associate's own single-date composition, unrelated to the grid's batch query) — never one
        // range/batch call per day cell (42 cells in August 2026's grid).
        Assert.Equal(1, countingDatedResults.RangeCallCount);
        Assert.Equal(1, countingDrafts.RangeCallCount);
    }

    /// <summary>Counts <c>GetByDateAsync</c> calls specifically, wrapping a real in-memory repository so every other behavior stays authentic.</summary>
    private sealed class CountingFinalizedPassdownRepository(IFinalizedPassdownRepository inner) : IFinalizedPassdownRepository
    {
        public int GetByDateCallCount { get; private set; }

        public Task<FinalizedPassdown?> GetByDateAsync(DateOnly passdownDate, CancellationToken cancellationToken = default)
        {
            GetByDateCallCount++;
            return inner.GetByDateAsync(passdownDate, cancellationToken);
        }

        public Task<bool> ExistsForDateAsync(DateOnly passdownDate, CancellationToken cancellationToken = default) =>
            inner.ExistsForDateAsync(passdownDate, cancellationToken);

        public Task AddAsync(FinalizedPassdown finalizedPassdown, CancellationToken cancellationToken = default) =>
            inner.AddAsync(finalizedPassdown, cancellationToken);

        public Task<IReadOnlyList<FinalizedPassdownSummary>> ListAsync(CancellationToken cancellationToken = default) =>
            inner.ListAsync(cancellationToken);
    }

    /// <summary>Counts <c>GetByCategoryAndDateRangeAsync</c> calls specifically (spec Phase 12B Part N/Z#20), wrapping a real in-memory repository so every other behavior stays authentic.</summary>
    private sealed class CountingDatedResultRepository(IDatedResultRepository inner) : IDatedResultRepository
    {
        public int RangeCallCount { get; private set; }

        public Task<DatedResultRecord?> GetAsync(DatedResultKey key, CancellationToken cancellationToken = default) =>
            inner.GetAsync(key, cancellationToken);

        public Task<IReadOnlyList<DatedResultRecord>> GetByRequestedDateAsync(DateOnly requestedDate, CancellationToken cancellationToken = default) =>
            inner.GetByRequestedDateAsync(requestedDate, cancellationToken);

        public Task<IReadOnlyList<DatedResultRecord>> GetByCategoryAndDateRangeAsync(SourceCategory category, DateOnly startDate, DateOnly endDateInclusive, CancellationToken cancellationToken = default)
        {
            RangeCallCount++;
            return inner.GetByCategoryAndDateRangeAsync(category, startDate, endDateInclusive, cancellationToken);
        }

        public Task RecordSuccessAsync(DatedResultKey key, SourceEvidence evidence, string? payloadJson, CancellationToken cancellationToken = default) =>
            inner.RecordSuccessAsync(key, evidence, payloadJson, cancellationToken);

        public Task RecordFailedAttemptAsync(DatedResultKey key, SourceEvidence evidence, CancellationToken cancellationToken = default) =>
            inner.RecordFailedAttemptAsync(key, evidence, cancellationToken);
    }

    /// <summary>Counts <c>GetByDateRangeAsync</c> calls specifically (spec Phase 12B Part N/Z#20), wrapping a real in-memory repository so every other behavior stays authentic.</summary>
    private sealed class CountingAssociateDraftRepository(IAssociateDraftRepository inner) : IAssociateDraftRepository
    {
        public int RangeCallCount { get; private set; }

        public Task<AssociateDraft?> GetAsync(DateOnly operatingDate, CancellationToken cancellationToken = default) =>
            inner.GetAsync(operatingDate, cancellationToken);

        public Task SaveAsync(AssociateDraft draft, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(draft, cancellationToken);

        public Task<IReadOnlyList<DateOnly>> ListSavedDraftDatesAsync(CancellationToken cancellationToken = default) =>
            inner.ListSavedDraftDatesAsync(cancellationToken);

        public Task<IReadOnlyList<AssociateDraft>> GetByDateRangeAsync(DateOnly startDate, DateOnly endDateInclusive, CancellationToken cancellationToken = default)
        {
            RangeCallCount++;
            return inner.GetByDateRangeAsync(startDate, endDateInclusive, cancellationToken);
        }
    }
}
