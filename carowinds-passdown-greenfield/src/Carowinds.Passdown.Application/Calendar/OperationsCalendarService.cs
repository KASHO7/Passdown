using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Time;

namespace Carowinds.Passdown.Application.Calendar;

/// <summary>
/// <see cref="IOperationsCalendarService"/> built entirely as a projection over existing local state
/// (Phase 12A Part D), mirroring how <see cref="Dashboard.OperationsDashboardService"/> projects over
/// the same kind of state for "today" — never a second finalization/relevance/draft-composition rule.
/// Depends only on <see cref="IAssociatePassdownService"/> (the composed workspace for the selected
/// date), <see cref="IAssociateDraftRepository"/> (which dates have a saved draft at all — the same
/// signal <see cref="Finalization.ScheduledPassdownFinalizer"/> already uses), <see cref="IFinalizedPassdownRepository"/>
/// (metadata only — never <c>GetByDateAsync</c>, so a calendar load can never materialize a historical
/// PDF blob), <see cref="IOperationalNotesService"/>, and <see cref="IClock"/>. No HTTP/Playwright/Task
/// Scheduler dependency exists on this type by construction.
/// </summary>
public sealed class OperationsCalendarService(
    IAssociatePassdownService associatePassdownService,
    IAssociateDraftRepository associateDraftRepository,
    IFinalizedPassdownRepository finalizedPassdownRepository,
    IOperationalNotesService operationalNotesService,
    IClock clock) : IOperationsCalendarService
{
    public async Task<OperationsCalendarSnapshot> LoadAsync(DateOnly displayedMonth, DateOnly selectedDate, CancellationToken cancellationToken = default)
    {
        var monthStart = new DateOnly(displayedMonth.Year, displayedMonth.Month, 1);
        var today = TodayEastern();
        var gridDates = BuildGridDates(monthStart);

        // Three calls total regardless of grid size (never one query per day cell): finalization
        // metadata, saved-draft dates, and every persistent note — the same relevance rule
        // (OperationalNoteRelevance.IsRelevantForDate) is then applied in memory per day, exactly as
        // IOperationalNotesService.ListRelevantForDateAsync already does for one date at a time.
        var finalizedSummaries = await finalizedPassdownRepository.ListAsync(cancellationToken).ConfigureAwait(false);
        var finalizedByDate = finalizedSummaries.ToDictionary(summary => summary.PassdownDate);

        var draftDates = await associateDraftRepository.ListSavedDraftDatesAsync(cancellationToken).ConfigureAwait(false);
        var draftDateSet = draftDates.ToHashSet();

        var allNotes = await operationalNotesService.ListAsync(cancellationToken).ConfigureAwait(false);

        var days = gridDates
            .Select(date => new OperationsCalendarDay(
                date,
                IsInDisplayedMonth: date.Month == monthStart.Month && date.Year == monthStart.Year,
                IsToday: date == today,
                HasFinalizedPassdown: finalizedByDate.ContainsKey(date),
                HasAssociateDraft: draftDateSet.Contains(date),
                RelevantOperationalNoteCount: allNotes.Count(note => OperationalNoteRelevance.IsRelevantForDate(note, date))))
            .ToList();

        var selected = await BuildSelectedSummaryAsync(selectedDate, today, finalizedByDate, allNotes, cancellationToken).ConfigureAwait(false);

        return new OperationsCalendarSnapshot(monthStart, today, selectedDate, days, selected, clock.UtcNow);
    }

    /// <summary>
    /// The selected date's own composed workspace is loaded once here — the same local-only call
    /// <see cref="Dashboard.OperationsDashboardService"/> makes for "today" — rather than for every
    /// day in the grid, since only the selected date's summary needs draft-save-status detail.
    /// </summary>
    private async Task<OperationsCalendarSelectedDateSummary> BuildSelectedSummaryAsync(
        DateOnly selectedDate,
        DateOnly today,
        IReadOnlyDictionary<DateOnly, FinalizedPassdownSummary> finalizedByDate,
        IReadOnlyList<OperationalNote> allNotes,
        CancellationToken cancellationToken)
    {
        var workspace = await associatePassdownService.LoadAssociateDateAsync(selectedDate, cancellationToken).ConfigureAwait(false);
        var finalized = finalizedByDate.GetValueOrDefault(selectedDate);

        var status = finalized is not null
            ? OperationsCalendarPassdownStatus.Finalized
            : workspace.SaveStatus.State == AssociateSaveState.NeverSaved
                ? OperationsCalendarPassdownStatus.NoDraft
                : OperationsCalendarPassdownStatus.DraftInProgress;

        var noteCount = allNotes.Count(note => OperationalNoteRelevance.IsRelevantForDate(note, selectedDate));

        return new OperationsCalendarSelectedDateSummary(
            selectedDate,
            selectedDate == today,
            status,
            workspace.SaveStatus.LastSavedAt,
            finalized?.FinalizedAtUtc,
            noteCount);
    }

    private DateOnly TodayEastern() => DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(clock.UtcNow).DateTime);

    /// <summary>Complete Sunday-through-Saturday weeks covering the whole month — variable row count (5 or 6 weeks) rather than a fixed 6-row grid, so a caller never sees fabricated cells outside any real week.</summary>
    private static List<DateOnly> BuildGridDates(DateOnly monthStart)
    {
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var gridStart = monthStart.AddDays(-(int)monthStart.DayOfWeek);
        var gridEnd = monthEnd.AddDays(6 - (int)monthEnd.DayOfWeek);

        var dates = new List<DateOnly>();
        for (var date = gridStart; date <= gridEnd; date = date.AddDays(1))
        {
            dates.Add(date);
        }

        return dates;
    }
}
