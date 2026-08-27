using System.Reflection;
using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Calendar;
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
        var service = new OperationsCalendarService(associateService, drafts, finalizedPassdowns, operationalNotes, clock);
        return new TestEnvironment(service, drafts, finalizedPassdowns, operationalNotes, clock);
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
        var service = new OperationsCalendarService(associateService, drafts, countingRepository, env.OperationalNotes, env.Clock);
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
}
