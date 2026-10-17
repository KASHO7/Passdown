using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Application.Calendar;

/// <summary>
/// <see cref="IOperationsCalendarService"/> built entirely as a projection over existing local state
/// (Phase 12A Part D), mirroring how <see cref="Dashboard.OperationsDashboardService"/> projects over
/// the same kind of state for "today" — never a second finalization/relevance/draft-composition rule.
/// Depends only on <see cref="IAssociatePassdownService"/> (the composed workspace for the selected
/// date), <see cref="IAssociateDraftRepository"/> (which dates have a saved draft at all, plus — Phase
/// 12B Part Q — every visible date's own draft payload, for resolving manual-hours/private-buyout
/// overrides), <see cref="IDatedResultRepository"/> (Phase 12B Part A/N — official park hours for
/// every visible date, batch-loaded), <see cref="IFinalizedPassdownRepository"/> (metadata only —
/// never <c>GetByDateAsync</c>, so a calendar load can never materialize a historical PDF blob),
/// <see cref="IOperationalNotesService"/>, and <see cref="IClock"/>. No HTTP/Playwright/Task Scheduler
/// dependency exists on this type by construction.
/// </summary>
public sealed class OperationsCalendarService(
    IAssociatePassdownService associatePassdownService,
    IAssociateDraftRepository associateDraftRepository,
    IDatedResultRepository datedResultRepository,
    IFinalizedPassdownRepository finalizedPassdownRepository,
    IOperationalNotesService operationalNotesService,
    IClock clock) : IOperationsCalendarService
{
    public async Task<OperationsCalendarSnapshot> LoadAsync(DateOnly displayedMonth, DateOnly selectedDate, CancellationToken cancellationToken = default)
    {
        var monthStart = new DateOnly(displayedMonth.Year, displayedMonth.Month, 1);
        var today = TodayEastern();
        var asOf = clock.UtcNow;
        var gridDates = BuildGridDates(monthStart);
        var gridStart = gridDates[0];
        var gridEnd = gridDates[^1];

        // Fixed number of calls regardless of grid size (never one query per day cell — spec Phase
        // 12B Part N/O): finalization metadata, saved-draft dates, every persistent note, one
        // category/date-range query for official park hours across the whole visible grid (Part A),
        // and one date-range query for every visible date's own saved draft (Part Q — the only local
        // path a manual-hours/private-buyout override can be resolved from).
        var finalizedSummaries = await finalizedPassdownRepository.ListAsync(cancellationToken).ConfigureAwait(false);
        var finalizedByDate = finalizedSummaries.ToDictionary(summary => summary.PassdownDate);

        var draftDates = await associateDraftRepository.ListSavedDraftDatesAsync(cancellationToken).ConfigureAwait(false);
        var draftDateSet = draftDates.ToHashSet();

        var allNotes = await operationalNotesService.ListAsync(cancellationToken).ConfigureAwait(false);

        var parkHoursByDate = await ComposeParkHoursForRangeAsync(gridStart, gridEnd, asOf, cancellationToken).ConfigureAwait(false);

        var days = gridDates
            .Select(date =>
            {
                var parkHours = parkHoursByDate[date];
                return new OperationsCalendarDay(
                    date,
                    IsInDisplayedMonth: date.Month == monthStart.Month && date.Year == monthStart.Year,
                    IsToday: date == today,
                    HasFinalizedPassdown: finalizedByDate.ContainsKey(date),
                    HasAssociateDraft: draftDateSet.Contains(date),
                    RelevantOperationalNoteCount: allNotes.Count(note => OperationalNoteRelevance.IsRelevantForDate(note, date)),
                    OperatingState: ClassifyOperatingState(parkHours),
                    ParkHours: parkHours,
                    ImportantOperationalNoteCount: CountImportant(allNotes, date));
            })
            .ToList();

        var selected = await BuildSelectedSummaryAsync(selectedDate, today, finalizedByDate, allNotes, cancellationToken).ConfigureAwait(false);

        return new OperationsCalendarSnapshot(monthStart, today, selectedDate, days, selected, asOf);
    }

    /// <summary>
    /// The selected date's own composed workspace is loaded once here — the same local-only call
    /// <see cref="Dashboard.OperationsDashboardService"/> makes for "today" — rather than for every
    /// day in the grid, since only the selected date's summary needs draft-save-status detail.
    /// <see cref="AssociateWorkspace.ParkHours"/> is reused directly for the selected date's operating
    /// state/hours (Phase 12B Part K) — it comes from the exact same
    /// <see cref="AssociateParkHoursComposer.Compose"/> call the day-cell grid data uses, and is
    /// authoritative for this date whether or not it happens to fall inside the currently displayed
    /// grid range.
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
            noteCount,
            ClassifyOperatingState(workspace.ParkHours),
            workspace.ParkHours,
            ImportantOperationalNoteCount: CountImportant(allNotes, selectedDate),
            DailyAdmission: BuildDailyAdmissionPrice(workspace))
        {
            Shows = workspace.ShowSchedule,
        };
    }

    /// <summary>
    /// Phase 13D Part AH: the selected date's regular Daily Admission price — read straight from the
    /// already-composed <see cref="AssociateWorkspace.Products"/> list (never a second retrieval/
    /// composition). Deliberately just one representative product per Part AK's "keep it compact"
    /// rule: among every <see cref="SourceCategory.DatedAdmissionTickets"/> product for this date, the
    /// lowest-priced one — the base admission ticket is reliably the cheapest option in this category
    /// (bundles/upsells are always priced higher), so this identifies it structurally rather than by
    /// matching a specific product name (never special-cased per Part AF's "generic, not
    /// name-specific" rule). Never fabricates a price: no product in the category at all is reported
    /// via the category's own checked/unchecked/unavailable status, exactly like every other Dashboard/
    /// Calendar status surface.
    /// </summary>
    private static OperationsCalendarDailyAdmissionPrice BuildDailyAdmissionPrice(AssociateWorkspace workspace)
    {
        var candidate = workspace.Products
            .Where(p => p.Category == SourceCategory.DatedAdmissionTickets && p.OfficialPriceAmount is not null)
            .OrderBy(p => p.OfficialPriceAmount)
            .FirstOrDefault();

        if (candidate is not null)
        {
            return new OperationsCalendarDailyAdmissionPrice(
                OperationsCalendarDailyAdmissionStatus.Available,
                candidate.DisplayName,
                candidate.OfficialPriceAmount,
                candidate.PriceKind,
                candidate.PriceQualifier);
        }

        var categoryStatus = workspace.CategoryStatuses.FirstOrDefault(s => s.Category == SourceCategory.DatedAdmissionTickets);
        var status = categoryStatus?.State switch
        {
            AssociateVerificationState.NotPublished => OperationsCalendarDailyAdmissionStatus.NotPublished,
            AssociateVerificationState.Unavailable or AssociateVerificationState.Error => OperationsCalendarDailyAdmissionStatus.Unavailable,
            _ => OperationsCalendarDailyAdmissionStatus.NotCheckedYet,
        };

        return new OperationsCalendarDailyAdmissionPrice(status, DisplayName: null, PriceAmount: null, PriceKind.Unknown, PriceQualifier: null);
    }

    /// <summary>
    /// Important-priority relevant notes for one date (Phase 12C Part E/F) — a further in-memory
    /// filter over the same <paramref name="allNotes"/> list already loaded once per <c>LoadAsync</c>
    /// (spec Part O: never a second/dedicated Important-count query), reusing the exact same
    /// <see cref="OperationalNoteRelevance.IsRelevantForDate"/> rule as every other relevance count in
    /// this type.
    /// </summary>
    private static int CountImportant(IReadOnlyList<OperationalNote> allNotes, DateOnly date) =>
        allNotes.Count(note => note.Priority == OperationalNotePriority.Important && OperationalNoteRelevance.IsRelevantForDate(note, date));

    /// <summary>
    /// Composes an <see cref="AssociateParkHoursView"/> for every date in
    /// <paramref name="rangeStart"/>..<paramref name="rangeEnd"/> (spec Phase 12B Part A/N/O/Q) using
    /// the exact same <see cref="AssociateParkHoursComposer.Compose"/> call
    /// <see cref="AssociatePassdownService"/> itself uses — never a second hours engine. Two queries
    /// total for the whole range (official park-hours results, saved draft payloads), then one
    /// in-memory composition per date — never one repository round trip per day cell.
    /// </summary>
    private async Task<IReadOnlyDictionary<DateOnly, AssociateParkHoursView>> ComposeParkHoursForRangeAsync(
        DateOnly rangeStart, DateOnly rangeEnd, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var datedResults = await datedResultRepository.GetByCategoryAndDateRangeAsync(SourceCategory.ParkHours, rangeStart, rangeEnd, cancellationToken).ConfigureAwait(false);
        var datedByDate = datedResults.ToDictionary(record => record.Key.Date);

        var drafts = await associateDraftRepository.GetByDateRangeAsync(rangeStart, rangeEnd, cancellationToken).ConfigureAwait(false);
        var draftPayloadByDate = drafts.ToDictionary(draft => draft.OperatingDate, draft => AssociateDraftPayloadSerializer.Deserialize(draft.PayloadJson));

        var result = new Dictionary<DateOnly, AssociateParkHoursView>();
        for (var date = rangeStart; date <= rangeEnd; date = date.AddDays(1))
        {
            var record = datedByDate.GetValueOrDefault(date);
            var draft = draftPayloadByDate.GetValueOrDefault(date, AssociateDraftPayload.CreateDefault());
            result[date] = AssociateParkHoursComposer.Compose(record, draft, asOf);
        }

        return result;
    }

    /// <summary>
    /// Structurally identical to <see cref="Dashboard.OperationsDashboardService"/>'s own
    /// <c>BuildParkStatus</c> classification (Phase 10A) — a pure label mapping over the shared
    /// <see cref="EffectiveHours"/>/<see cref="AssociateVerificationState"/> values (spec Phase 12B
    /// Part A/B). Kept independent per <see cref="OperationsCalendarOperatingState"/>'s own doc
    /// comment, not because the classification differs.
    /// </summary>
    private static OperationsCalendarOperatingState ClassifyOperatingState(AssociateParkHoursView hours)
    {
        var effective = hours.Effective;

        return effective.Status switch
        {
            EffectiveHoursStatus.Open when effective.IsPrivateBuyoutApplied => OperationsCalendarOperatingState.PrivateBuyout,
            EffectiveHoursStatus.Open when effective.IsManualOverrideApplied => OperationsCalendarOperatingState.CustomHours,
            EffectiveHoursStatus.Open => OperationsCalendarOperatingState.Open,
            EffectiveHoursStatus.Closed => OperationsCalendarOperatingState.Closed,
            _ => hours.Verification.State switch
            {
                AssociateVerificationState.NotPublished => OperationsCalendarOperatingState.NotPublished,
                AssociateVerificationState.Unavailable => OperationsCalendarOperatingState.Unavailable,
                AssociateVerificationState.Error => OperationsCalendarOperatingState.Unavailable,
                _ => OperationsCalendarOperatingState.NotChecked,
            },
        };
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
