using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Covers <see cref="OperationsCalendarViewModel"/> (Phase 12A Part G) — date semantics (Eastern
/// "today", explicit displayed-month/selected-date state), month navigation, day selection, the
/// routing events, and the no-network-dependency guarantee.
/// </summary>
public class OperationsCalendarViewModelTests
{
    private static readonly DateOnly FixedToday = new(2026, 8, 21);

    private static FakeClock CreateClock() =>
        new(new DateTimeOffset(FixedToday.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero)); // 11:00 AM Eastern

    // #1: on construction, DisplayedMonth/SelectedDate both resolve to the current Eastern date.
    [Fact]
    public async Task Constructor_InitializesToEasternTodayAndLoads()
    {
        var service = new FakeOperationsCalendarService();
        var vm = new OperationsCalendarViewModel(service, CreateClock());
        await vm.PendingLoad;

        Assert.Equal(new DateOnly(2026, 8, 1), vm.DisplayedMonth);
        Assert.Equal(FixedToday, vm.SelectedDate);
        Assert.NotNull(vm.Snapshot);
        Assert.Single(service.LoadCalls);
        Assert.Equal((new DateOnly(2026, 8, 1), FixedToday), service.LoadCalls[0]);
    }

    // #2: "today" is resolved via Eastern conversion, not a raw UTC date, near a day boundary.
    [Fact]
    public async Task Constructor_LateUtcClock_ResolvesTodayUsingEasternConversion()
    {
        var lateUtcClock = new FakeClock(new DateTimeOffset(2026, 8, 22, 2, 30, 0, TimeSpan.Zero)); // 10:30 PM EDT on 2026-08-21
        var vm = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), lateUtcClock);
        await vm.PendingLoad;

        Assert.Equal(new DateOnly(2026, 8, 21), vm.SelectedDate);
        Assert.Equal(new DateOnly(2026, 8, 1), vm.DisplayedMonth);
    }

    // Phase 12D Part D: the chosen, documented behavior — PreviousMonth/NextMonth move DisplayedMonth
    // and carry SelectedDate's day-of-month along with it (never leaving the selection in a month the
    // grid no longer displays). FixedToday is the 21st, a day that exists in every month, so this
    // exercises the "no clamping needed" path; clamping itself is covered separately below.
    [Fact]
    public async Task PreviousAndNextMonthCommands_MoveDisplayedMonthAndCarrySelectedDateAlong()
    {
        var service = new FakeOperationsCalendarService();
        var vm = new OperationsCalendarViewModel(service, CreateClock());
        await vm.PendingLoad;

        vm.PreviousMonthCommand.Execute(null);
        await vm.PendingLoad;
        Assert.Equal(new DateOnly(2026, 7, 1), vm.DisplayedMonth);
        Assert.Equal(new DateOnly(2026, 7, 21), vm.SelectedDate);

        vm.NextMonthCommand.Execute(null);
        vm.NextMonthCommand.Execute(null);
        await vm.PendingLoad;
        Assert.Equal(new DateOnly(2026, 9, 1), vm.DisplayedMonth);
        Assert.Equal(new DateOnly(2026, 9, 21), vm.SelectedDate);
    }

    // Phase 12D Part D: Jan 31 -> Next Month clamps to Feb 28 in a non-leap year.
    [Fact]
    public async Task NextMonthCommand_JanuaryThirtyFirst_ClampsToFebruaryTwentyEighth_NonLeapYear()
    {
        var vm = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), CreateClock());
        await vm.PendingLoad;
        vm.SelectDateCommand.Execute(new DateOnly(2027, 1, 31));
        await vm.PendingLoad;

        vm.NextMonthCommand.Execute(null);
        await vm.PendingLoad;

        Assert.Equal(new DateOnly(2027, 2, 1), vm.DisplayedMonth);
        Assert.Equal(new DateOnly(2027, 2, 28), vm.SelectedDate);
    }

    // Phase 12D Part D: Jan 31 -> Next Month clamps to Feb 29 in a leap year.
    [Fact]
    public async Task NextMonthCommand_JanuaryThirtyFirst_ClampsToFebruaryTwentyNinth_LeapYear()
    {
        var vm = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), CreateClock());
        await vm.PendingLoad;
        vm.SelectDateCommand.Execute(new DateOnly(2028, 1, 31));
        await vm.PendingLoad;

        vm.NextMonthCommand.Execute(null);
        await vm.PendingLoad;

        Assert.Equal(new DateOnly(2028, 2, 1), vm.DisplayedMonth);
        Assert.Equal(new DateOnly(2028, 2, 29), vm.SelectedDate);
    }

    // Phase 12D Part D/V: Dec -> Jan year crossing carries the day-of-month across the year boundary cleanly.
    [Fact]
    public async Task NextMonthCommand_DecemberToJanuary_CrossesYearBoundary()
    {
        var vm = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), CreateClock());
        await vm.PendingLoad;
        vm.SelectDateCommand.Execute(new DateOnly(2026, 12, 15));
        await vm.PendingLoad;

        vm.NextMonthCommand.Execute(null);
        await vm.PendingLoad;

        Assert.Equal(new DateOnly(2027, 1, 1), vm.DisplayedMonth);
        Assert.Equal(new DateOnly(2027, 1, 15), vm.SelectedDate);
    }

    // Phase 12D Part AI: month navigation resolves to exactly one service load, never one per property.
    [Fact]
    public async Task NextMonthCommand_TriggersExactlyOneLoad()
    {
        var service = new FakeOperationsCalendarService();
        var vm = new OperationsCalendarViewModel(service, CreateClock());
        await vm.PendingLoad;
        service.LoadCalls.Clear();

        vm.NextMonthCommand.Execute(null);
        await vm.PendingLoad;

        Assert.Single(service.LoadCalls);
    }

    // #4: SelectDateCommand updates SelectedDate and reloads, without touching DisplayedMonth.
    [Fact]
    public async Task SelectDateCommand_UpdatesSelectedDate_PreservesDisplayedMonth()
    {
        var vm = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), CreateClock());
        await vm.PendingLoad;

        vm.SelectDateCommand.Execute(new DateOnly(2026, 8, 5));
        await vm.PendingLoad;

        Assert.Equal(new DateOnly(2026, 8, 5), vm.SelectedDate);
        Assert.Equal(new DateOnly(2026, 8, 1), vm.DisplayedMonth);
    }

    // #5: GoToToday resets both DisplayedMonth and SelectedDate back to the current Eastern date,
    // even after navigating away.
    [Fact]
    public async Task GoToTodayCommand_ResetsDisplayedMonthAndSelectedDate()
    {
        var vm = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), CreateClock());
        await vm.PendingLoad;

        vm.NextMonthCommand.Execute(null);
        vm.SelectDateCommand.Execute(new DateOnly(2026, 9, 10));
        await vm.PendingLoad;

        vm.GoToTodayCommand.Execute(null);
        await vm.PendingLoad;

        Assert.Equal(new DateOnly(2026, 8, 1), vm.DisplayedMonth);
        Assert.Equal(FixedToday, vm.SelectedDate);
    }

    // Phase 12D Part B: selecting an off-month cell moves the Calendar to that date's own month — the
    // chosen, documented behavior (as opposed to leaving off-month cells purely informational).
    [Fact]
    public async Task SelectDateCommand_OffMonthDate_MovesDisplayedMonthToMatch()
    {
        var vm = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), CreateClock());
        await vm.PendingLoad;

        var offMonthDate = new DateOnly(2026, 9, 2); // a leading September day visible in August's grid
        vm.SelectDateCommand.Execute(offMonthDate);
        await vm.PendingLoad;

        Assert.Equal(offMonthDate, vm.SelectedDate);
        Assert.Equal(new DateOnly(2026, 9, 1), vm.DisplayedMonth);
    }

    // Phase 12D Part AI: an in-month selection resolves to exactly one service load.
    [Fact]
    public async Task SelectDateCommand_InMonthDate_TriggersExactlyOneLoad()
    {
        var service = new FakeOperationsCalendarService();
        var vm = new OperationsCalendarViewModel(service, CreateClock());
        await vm.PendingLoad;
        service.LoadCalls.Clear();

        vm.SelectDateCommand.Execute(new DateOnly(2026, 8, 5));
        await vm.PendingLoad;

        Assert.Single(service.LoadCalls);
    }

    // Phase 12D Part C: Next Day advances the selection by one day and stays within the same month.
    [Fact]
    public async Task NextDayCommand_AdvancesSelectedDateByOneDay()
    {
        var vm = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), CreateClock());
        await vm.PendingLoad;

        vm.NextDayCommand.Execute(null);
        await vm.PendingLoad;

        Assert.Equal(FixedToday.AddDays(1), vm.SelectedDate);
        Assert.Equal(new DateOnly(2026, 8, 1), vm.DisplayedMonth);
    }

    // Phase 12D Part C: Previous Day steps back by one day.
    [Fact]
    public async Task PreviousDayCommand_StepsBackByOneDay()
    {
        var vm = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), CreateClock());
        await vm.PendingLoad;

        vm.PreviousDayCommand.Execute(null);
        await vm.PendingLoad;

        Assert.Equal(FixedToday.AddDays(-1), vm.SelectedDate);
    }

    // Phase 12D Part C/U: Next Day crosses a month boundary and DisplayedMonth follows.
    [Fact]
    public async Task NextDayCommand_CrossesMonthBoundary_DisplayedMonthFollows()
    {
        var vm = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), CreateClock());
        await vm.PendingLoad;
        vm.SelectDateCommand.Execute(new DateOnly(2026, 8, 31));
        await vm.PendingLoad;

        vm.NextDayCommand.Execute(null);
        await vm.PendingLoad;

        Assert.Equal(new DateOnly(2026, 9, 1), vm.SelectedDate);
        Assert.Equal(new DateOnly(2026, 9, 1), vm.DisplayedMonth);
    }

    // Phase 12D Part C/U: Previous Day crosses backward across a month (and leap-year) boundary.
    [Fact]
    public async Task PreviousDayCommand_CrossesMonthBoundary_IntoLeapYearFebruary()
    {
        var vm = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), CreateClock());
        await vm.PendingLoad;
        vm.SelectDateCommand.Execute(new DateOnly(2028, 3, 1));
        await vm.PendingLoad;

        vm.PreviousDayCommand.Execute(null);
        await vm.PendingLoad;

        Assert.Equal(new DateOnly(2028, 2, 29), vm.SelectedDate);
        Assert.Equal(new DateOnly(2028, 2, 1), vm.DisplayedMonth);
    }

    // Phase 12D Part C/V: Next Day crosses a year boundary cleanly.
    [Fact]
    public async Task NextDayCommand_CrossesYearBoundary()
    {
        var vm = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), CreateClock());
        await vm.PendingLoad;
        vm.SelectDateCommand.Execute(new DateOnly(2026, 12, 31));
        await vm.PendingLoad;

        vm.NextDayCommand.Execute(null);
        await vm.PendingLoad;

        Assert.Equal(new DateOnly(2027, 1, 1), vm.SelectedDate);
        Assert.Equal(new DateOnly(2027, 1, 1), vm.DisplayedMonth);
    }

    // Phase 12D Part C/V: Previous Day crosses a year boundary backward.
    [Fact]
    public async Task PreviousDayCommand_CrossesYearBoundaryBackward()
    {
        var vm = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), CreateClock());
        await vm.PendingLoad;
        vm.SelectDateCommand.Execute(new DateOnly(2027, 1, 1));
        await vm.PendingLoad;

        vm.PreviousDayCommand.Execute(null);
        await vm.PendingLoad;

        Assert.Equal(new DateOnly(2026, 12, 31), vm.SelectedDate);
        Assert.Equal(new DateOnly(2026, 12, 1), vm.DisplayedMonth);
    }

    // Phase 12D Part AI: day-stepping resolves to exactly one service load, never one per property.
    [Fact]
    public async Task NextDayCommand_CrossingMonthBoundary_TriggersExactlyOneLoad()
    {
        var service = new FakeOperationsCalendarService();
        var vm = new OperationsCalendarViewModel(service, CreateClock());
        await vm.PendingLoad;
        vm.SelectDateCommand.Execute(new DateOnly(2026, 8, 31));
        await vm.PendingLoad;
        service.LoadCalls.Clear();

        vm.NextDayCommand.Execute(null);
        await vm.PendingLoad;

        Assert.Single(service.LoadCalls);
    }

    // Phase 12D Part E/W: "Go to Today" follows the Eastern day change, not machine-local time — using
    // a mutable IClock so the same view model observes "today" changing across the Eastern midnight
    // boundary, exactly as the spec's deterministic-IClock requirement describes.
    [Fact]
    public async Task GoToTodayCommand_FollowsEasternMidnightBoundary()
    {
        // 11:59 PM Eastern on Aug 26, 2026 = 2026-08-27T03:59:00Z (EDT, UTC-4).
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 27, 3, 59, 0, TimeSpan.Zero));
        var vm = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), clock);
        await vm.PendingLoad;
        Assert.Equal(new DateOnly(2026, 8, 26), vm.SelectedDate);

        vm.NextMonthCommand.Execute(null);
        await vm.PendingLoad;

        // 12:01 AM Eastern on Aug 27, 2026 = 2026-08-27T04:01:00Z.
        clock.UtcNow = new DateTimeOffset(2026, 8, 27, 4, 1, 0, TimeSpan.Zero);
        vm.GoToTodayCommand.Execute(null);
        await vm.PendingLoad;

        Assert.Equal(new DateOnly(2026, 8, 27), vm.SelectedDate);
        Assert.Equal(new DateOnly(2026, 8, 1), vm.DisplayedMonth);
    }

    // Phase 12D Part AI: "Go to Today" resolves to exactly one service load.
    [Fact]
    public async Task GoToTodayCommand_TriggersExactlyOneLoad()
    {
        var service = new FakeOperationsCalendarService();
        var vm = new OperationsCalendarViewModel(service, CreateClock());
        await vm.PendingLoad;
        vm.NextMonthCommand.Execute(null);
        await vm.PendingLoad;
        service.LoadCalls.Clear();

        vm.GoToTodayCommand.Execute(null);
        await vm.PendingLoad;

        Assert.Single(service.LoadCalls);
    }

    // #6: Days is rebuilt from the snapshot, and IsSelected reflects the snapshot's own SelectedDate.
    [Fact]
    public async Task SnapshotLoaded_DaysReflectSelectionAndOrdering()
    {
        var vm = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), CreateClock());
        await vm.PendingLoad;

        Assert.NotEmpty(vm.Days);
        var selectedDay = Assert.Single(vm.Days, d => d.IsSelected);
        Assert.Equal(vm.SelectedDate, selectedDay.Date);
    }

    // #7: clicking a day cell's own SelectCommand routes back into the parent view model's SelectedDate.
    [Fact]
    public async Task DayViewModel_SelectCommand_UpdatesParentSelectedDate()
    {
        var vm = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), CreateClock());
        await vm.PendingLoad;

        var otherDay = vm.Days.First(d => d.Date != vm.SelectedDate);
        otherDay.SelectCommand.Execute(null);
        await vm.PendingLoad;

        Assert.Equal(otherDay.Date, vm.SelectedDate);
    }

    // #8: "Open Passdown" raises OpenAssociateRequested with the currently selected date.
    [Fact]
    public async Task OpenAssociateCommand_RaisesEventWithSelectedDate()
    {
        var vm = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), CreateClock());
        await vm.PendingLoad;
        vm.SelectDateCommand.Execute(new DateOnly(2026, 8, 5));
        await vm.PendingLoad;

        DateOnly? raised = null;
        vm.OpenAssociateRequested += (_, date) => raised = date;

        vm.OpenAssociateCommand.Execute(null);

        Assert.Equal(new DateOnly(2026, 8, 5), raised);
    }

    // #9: Open History is only enabled once the selected date's own passdown status is Finalized.
    [Fact]
    public async Task OpenHistoryCommand_EnabledOnlyWhenSelectedDateIsFinalized()
    {
        var service = new FakeOperationsCalendarService
        {
            SnapshotFactory = (month, selected) => FakeOperationsCalendarService.DefaultSnapshot(month, selected) with
            {
                Selected = new OperationsCalendarSelectedDateSummary(
                    selected, false, OperationsCalendarPassdownStatus.NoDraft, null, null, 0,
                    OperationsCalendarOperatingState.NotChecked, FakeOperationsCalendarService.NotCheckedParkHours),
            },
        };
        var vm = new OperationsCalendarViewModel(service, CreateClock());
        await vm.PendingLoad;

        Assert.False(vm.OpenHistoryCommand.CanExecute(null));

        service.SnapshotFactory = (month, selected) => FakeOperationsCalendarService.DefaultSnapshot(month, selected) with
        {
            Selected = new OperationsCalendarSelectedDateSummary(
                selected, false, OperationsCalendarPassdownStatus.Finalized, null, DateTimeOffset.UtcNow, 0,
                OperationsCalendarOperatingState.NotChecked, FakeOperationsCalendarService.NotCheckedParkHours),
        };
        vm.SelectDateCommand.Execute(new DateOnly(2026, 8, 5));
        await vm.PendingLoad;

        Assert.True(vm.OpenHistoryCommand.CanExecute(null));

        // Phase 12D Part I: the event carries the selected date, so MainViewModel can ask History to
        // preselect the matching finalized record.
        DateOnly? raisedDate = null;
        vm.OpenHistoryRequested += (_, date) => raisedDate = date;
        vm.OpenHistoryCommand.Execute(null);
        Assert.Equal(new DateOnly(2026, 8, 5), raisedDate);
    }

    // Phase 12E Part S: the disabled "Open History" action's help text explains *why* it's disabled
    // (never relies on visual dimming alone), and updates as the selected date's own status changes.
    [Fact]
    public async Task OpenHistoryHelpText_ReflectsWhetherSelectedDateIsFinalized()
    {
        var service = new FakeOperationsCalendarService
        {
            SnapshotFactory = (month, selected) => FakeOperationsCalendarService.DefaultSnapshot(month, selected) with
            {
                Selected = new OperationsCalendarSelectedDateSummary(
                    selected, false, OperationsCalendarPassdownStatus.NoDraft, null, null, 0,
                    OperationsCalendarOperatingState.NotChecked, FakeOperationsCalendarService.NotCheckedParkHours),
            },
        };
        var vm = new OperationsCalendarViewModel(service, CreateClock());
        await vm.PendingLoad;

        Assert.False(vm.OpenHistoryCommand.CanExecute(null));
        Assert.Contains("No finalized passdown", vm.OpenHistoryHelpText);

        service.SnapshotFactory = (month, selected) => FakeOperationsCalendarService.DefaultSnapshot(month, selected) with
        {
            Selected = new OperationsCalendarSelectedDateSummary(
                selected, false, OperationsCalendarPassdownStatus.Finalized, null, DateTimeOffset.UtcNow, 0,
                OperationsCalendarOperatingState.NotChecked, FakeOperationsCalendarService.NotCheckedParkHours),
        };
        vm.SelectDateCommand.Execute(new DateOnly(2026, 8, 5));
        await vm.PendingLoad;

        Assert.True(vm.OpenHistoryCommand.CanExecute(null));
        Assert.DoesNotContain("No finalized passdown", vm.OpenHistoryHelpText);
    }

    // #10: "Open Operational Notes" always raises its event — generic navigation, no date gating.
    [Fact]
    public async Task OpenOperationalNotesCommand_AlwaysRaisesEvent()
    {
        var vm = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), CreateClock());
        await vm.PendingLoad;

        var raised = false;
        vm.OpenOperationalNotesRequested += (_, _) => raised = true;

        vm.OpenOperationalNotesCommand.Execute(null);

        Assert.True(raised);
    }

    // #11: a load failure degrades gracefully with a concise message and no thrown exception.
    [Fact]
    public async Task LoadAsync_ServiceThrows_SetsErrorMessageAndClearsSnapshot()
    {
        var service = new FakeOperationsCalendarService { Exception = new InvalidOperationException("boom") };
        var vm = new OperationsCalendarViewModel(service, CreateClock());
        await vm.PendingLoad;

        Assert.Null(vm.Snapshot);
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
        Assert.True(vm.ShowEmptyOrErrorState);
    }

    // #13 (spec Part I; updated Phase 12C Part B/AB): every visual indicator on a day cell has a
    // textual equivalent in its automation name — never color-only. Asserted directly here since an
    // ItemsControl's generated item containers aren't realized by the headless View tests'
    // GetLogicalDescendants() without a layout pass (see OperationsCalendarViewTests). A date with
    // both a finalized record and an existing saved draft asserts "Finalized" only — Phase 12C Part B's
    // precedence means "Draft in progress" is never also shown once a date is finalized.
    [Fact]
    public void DayViewModel_AutomationName_IncludesEveryTextualIndicator()
    {
        var day = new OperationsCalendarDay(
            new DateOnly(2026, 8, 21), IsInDisplayedMonth: true, IsToday: true,
            HasFinalizedPassdown: true, HasAssociateDraft: true, RelevantOperationalNoteCount: 2,
            OperationsCalendarOperatingState.Open, OpenParkHours(new TimeOnly(10, 0), new TimeOnly(22, 0)),
            ImportantOperationalNoteCount: 1);

        var dayViewModel = new OperationsCalendarDayViewModel(day, isSelected: true, _ => { });

        Assert.Contains("August 21, 2026", dayViewModel.AutomationName);
        Assert.Contains("Open, 10 AM to 10 PM", dayViewModel.AutomationName);
        Assert.Contains("Today", dayViewModel.AutomationName);
        Assert.Contains("Selected", dayViewModel.AutomationName);
        Assert.Contains("Finalized", dayViewModel.AutomationName);
        Assert.DoesNotContain("Draft in progress", dayViewModel.AutomationName);
        Assert.Contains("2 active operational notes, 1 Important", dayViewModel.AutomationName);
    }

    // ==================================================================================
    // Phase 12C — Passdown Status + Persistent Operational Notes overlays
    // ==================================================================================

    // Part AK #17: normal cell presentation — No Draft, no notes.
    [Fact]
    public void DayViewModel_NoDraftNoNotes_PassdownLabelIsNoDraftAndNotesSummaryIsHidden()
    {
        var day = MakeDay(OperationsCalendarOperatingState.Open, OpenParkHours(new TimeOnly(10, 0), new TimeOnly(22, 0)));

        var dayViewModel = new OperationsCalendarDayViewModel(day, isSelected: false, _ => { });

        Assert.Equal("No Draft", dayViewModel.PassdownStatusLabel);
        Assert.Null(dayViewModel.NotesSummaryText);
        Assert.False(dayViewModel.HasOperationalNotes);
    }

    [Fact]
    public void DayViewModel_DraftOnly_PassdownLabelIsDraft()
    {
        var day = new OperationsCalendarDay(
            new DateOnly(2026, 8, 22), IsInDisplayedMonth: true, IsToday: false,
            HasFinalizedPassdown: false, HasAssociateDraft: true, RelevantOperationalNoteCount: 0,
            OperationsCalendarOperatingState.Open, OpenParkHours(new TimeOnly(10, 0), new TimeOnly(22, 0)));

        var dayViewModel = new OperationsCalendarDayViewModel(day, isSelected: false, _ => { });

        Assert.Equal("Draft", dayViewModel.PassdownStatusLabel);
    }

    // Part AK #18: Finalized wins over an existing draft at the cell-presentation level too.
    [Fact]
    public void DayViewModel_FinalizedAndDraftBothTrue_PassdownLabelIsFinalizedOnly()
    {
        var day = new OperationsCalendarDay(
            new DateOnly(2026, 8, 22), IsInDisplayedMonth: true, IsToday: false,
            HasFinalizedPassdown: true, HasAssociateDraft: true, RelevantOperationalNoteCount: 0,
            OperationsCalendarOperatingState.Open, OpenParkHours(new TimeOnly(10, 0), new TimeOnly(22, 0)));

        var dayViewModel = new OperationsCalendarDayViewModel(day, isSelected: false, _ => { });

        Assert.Equal("Finalized", dayViewModel.PassdownStatusLabel);
    }

    // Part AK #19: Important-note count presentation on a day cell.
    [Fact]
    public void DayViewModel_RelevantAndImportantNotes_NotesSummaryIncludesImportantCount()
    {
        var day = new OperationsCalendarDay(
            new DateOnly(2026, 8, 22), IsInDisplayedMonth: true, IsToday: false,
            HasFinalizedPassdown: false, HasAssociateDraft: false, RelevantOperationalNoteCount: 2,
            OperationsCalendarOperatingState.Open, OpenParkHours(new TimeOnly(10, 0), new TimeOnly(22, 0)),
            ImportantOperationalNoteCount: 1);

        var dayViewModel = new OperationsCalendarDayViewModel(day, isSelected: false, _ => { });

        Assert.Equal("2 Notes · 1 Important", dayViewModel.NotesSummaryText);
        Assert.True(dayViewModel.HasOperationalNotes);
    }

    // ==================================================================================
    // Phase 12E — Calendar polish, density, and accessibility
    // ==================================================================================

    // Part H: drives the day cell's restrained "Important" accent treatment (never a red/alarm badge).
    [Fact]
    public void DayViewModel_ImportantNotesPresent_HasImportantOperationalNotesIsTrue()
    {
        var day = new OperationsCalendarDay(
            new DateOnly(2026, 8, 22), IsInDisplayedMonth: true, IsToday: false,
            HasFinalizedPassdown: false, HasAssociateDraft: false, RelevantOperationalNoteCount: 2,
            OperationsCalendarOperatingState.Open, OpenParkHours(new TimeOnly(10, 0), new TimeOnly(22, 0)),
            ImportantOperationalNoteCount: 1);

        var dayViewModel = new OperationsCalendarDayViewModel(day, isSelected: false, _ => { });

        Assert.True(dayViewModel.HasImportantOperationalNotes);
    }

    [Fact]
    public void DayViewModel_RelevantNotesButNoneImportant_HasImportantOperationalNotesIsFalse()
    {
        var day = new OperationsCalendarDay(
            new DateOnly(2026, 8, 22), IsInDisplayedMonth: true, IsToday: false,
            HasFinalizedPassdown: false, HasAssociateDraft: false, RelevantOperationalNoteCount: 2,
            OperationsCalendarOperatingState.Open, OpenParkHours(new TimeOnly(10, 0), new TimeOnly(22, 0)),
            ImportantOperationalNoteCount: 0);

        var dayViewModel = new OperationsCalendarDayViewModel(day, isSelected: false, _ => { });

        Assert.False(dayViewModel.HasImportantOperationalNotes);
    }

    // #12: no HTTP/PDF/refresh-shaped constructor dependency exists on this type.
    [Fact]
    public void Constructor_HasNoRefreshOrPdfDependency()
    {
        var parameterTypes = typeof(OperationsCalendarViewModel)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(p => p.ParameterType)
            .ToArray();

        Assert.DoesNotContain(parameterTypes, t => t.Name.Contains("Refresh", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(parameterTypes, t => t.Name.Contains("Pdf", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(parameterTypes, t => t.Name.Contains("Repository", StringComparison.OrdinalIgnoreCase));
    }

    // ==================================================================================
    // Phase 12B — Park Hours and Operating-State overlays
    // ==================================================================================

    // Part Z #26: accessibility text for an open day.
    [Fact]
    public void DayViewModel_OpenState_AccessibilityTextDescribesHours()
    {
        var day = MakeDay(OperationsCalendarOperatingState.Open, OpenParkHours(new TimeOnly(10, 0), new TimeOnly(22, 0)));
        var dayViewModel = new OperationsCalendarDayViewModel(day, isSelected: false, _ => { });

        Assert.Contains("Open, 10 AM to 10 PM", dayViewModel.AutomationName);
        Assert.Null(dayViewModel.OperatingStateLabel); // Part D: hours alone communicate "open" — no redundant label.
        Assert.Equal("10 AM–10 PM", dayViewModel.EffectiveHoursLine);
    }

    // Part Z #27: accessibility text for a closed day — never fabricated 12 AM–12 AM hours.
    [Fact]
    public void DayViewModel_ClosedState_AccessibilityTextIsClosedWithNoHours()
    {
        var day = MakeDay(OperationsCalendarOperatingState.Closed, ClosedParkHours());
        var dayViewModel = new OperationsCalendarDayViewModel(day, isSelected: false, _ => { });

        Assert.Contains("Closed", dayViewModel.AutomationName);
        Assert.DoesNotContain("AM", dayViewModel.AutomationName);
        Assert.Equal("CLOSED", dayViewModel.OperatingStateLabel);
        Assert.Null(dayViewModel.EffectiveHoursLine);
    }

    // Part Z #28: accessibility text for a custom-hours day.
    [Fact]
    public void DayViewModel_CustomHoursState_AccessibilityTextDescribesCustomHours()
    {
        var day = MakeDay(OperationsCalendarOperatingState.CustomHours, CustomParkHours(new TimeOnly(9, 0), new TimeOnly(23, 0)));
        var dayViewModel = new OperationsCalendarDayViewModel(day, isSelected: false, _ => { });

        Assert.Contains("Custom Hours, 9 AM to 11 PM", dayViewModel.AutomationName);
        Assert.Equal("CUSTOM", dayViewModel.OperatingStateLabel);
        Assert.Equal("9 AM–11 PM", dayViewModel.EffectiveHoursLine);
    }

    // Part Z #29: accessibility text for a private-buyout day.
    [Fact]
    public void DayViewModel_PrivateBuyoutState_AccessibilityTextDescribesBuyoutHours()
    {
        var day = MakeDay(OperationsCalendarOperatingState.PrivateBuyout, BuyoutParkHours(new TimeOnly(18, 0), new TimeOnly(23, 0)));
        var dayViewModel = new OperationsCalendarDayViewModel(day, isSelected: false, _ => { });

        Assert.Contains("Private Buyout, 6 PM to 11 PM", dayViewModel.AutomationName);
        Assert.Equal("BUYOUT", dayViewModel.OperatingStateLabel);
        Assert.Equal("6 PM–11 PM", dayViewModel.EffectiveHoursLine);
    }

    // Part H: Not Published is never treated as an error and never fabricates hours.
    [Fact]
    public void DayViewModel_NotPublishedState_ShowsNotPublishedWithNoHours()
    {
        var day = MakeDay(OperationsCalendarOperatingState.NotPublished, NotCheckedParkHoursView());
        var dayViewModel = new OperationsCalendarDayViewModel(day, isSelected: false, _ => { });

        Assert.Equal("NOT PUBLISHED", dayViewModel.OperatingStateLabel);
        Assert.Null(dayViewModel.EffectiveHoursLine);
        Assert.Contains("Hours not published", dayViewModel.AutomationName);
    }

    // Part I: Unavailable never surfaces raw exception/HTTP/endpoint detail — just a concise state.
    [Fact]
    public void DayViewModel_UnavailableState_ShowsUnavailableWithNoTechnicalDetail()
    {
        var day = MakeDay(OperationsCalendarOperatingState.Unavailable, NotCheckedParkHoursView());
        var dayViewModel = new OperationsCalendarDayViewModel(day, isSelected: false, _ => { });

        Assert.Equal("UNAVAILABLE", dayViewModel.OperatingStateLabel);
        Assert.Contains("Hours unavailable", dayViewModel.AutomationName);
        Assert.DoesNotContain("Exception", dayViewModel.AutomationName);
        Assert.DoesNotContain("http", dayViewModel.AutomationName, StringComparison.OrdinalIgnoreCase);
    }

    // Part M: operating-state/hours content coexists with Today and Selected — neither flag is lost
    // and both remain present as text alongside the hours clause.
    [Fact]
    public void DayViewModel_TodayAndSelectedTogether_BothRemainInAutomationNameAlongsideHours()
    {
        var day = new OperationsCalendarDay(
            FixedToday, IsInDisplayedMonth: true, IsToday: true,
            HasFinalizedPassdown: false, HasAssociateDraft: false, RelevantOperationalNoteCount: 0,
            OperationsCalendarOperatingState.CustomHours, CustomParkHours(new TimeOnly(9, 0), new TimeOnly(23, 0)));

        var dayViewModel = new OperationsCalendarDayViewModel(day, isSelected: true, _ => { });

        Assert.True(dayViewModel.IsToday);
        Assert.True(dayViewModel.IsSelected);
        Assert.Contains("Today", dayViewModel.AutomationName);
        Assert.Contains("Selected", dayViewModel.AutomationName);
        Assert.Contains("Custom Hours, 9 AM to 11 PM", dayViewModel.AutomationName);
    }

    private static OperationsCalendarDay MakeDay(OperationsCalendarOperatingState state, AssociateParkHoursView parkHours) => new(
        new DateOnly(2026, 8, 22), IsInDisplayedMonth: true, IsToday: false,
        HasFinalizedPassdown: false, HasAssociateDraft: false, RelevantOperationalNoteCount: 0,
        state, parkHours);

    private static AssociateParkHoursView OpenParkHours(TimeOnly opening, TimeOnly closing) => new(
        ParkHoursCalculator.Calculate(false, opening, closing, false, null, null, false, null, null),
        AssociateVerificationInfo.NotCheckedYet, OfficialIsClosed: false, opening, closing);

    private static AssociateParkHoursView ClosedParkHours() => new(
        ParkHoursCalculator.Calculate(true, null, null, false, null, null, false, null, null),
        AssociateVerificationInfo.NotCheckedYet, OfficialIsClosed: true, OfficialOpening: null, OfficialClosing: null);

    private static AssociateParkHoursView CustomParkHours(TimeOnly opening, TimeOnly closing) => new(
        ParkHoursCalculator.Calculate(false, new TimeOnly(10, 0), new TimeOnly(22, 0), true, opening, closing, false, null, null),
        AssociateVerificationInfo.NotCheckedYet, OfficialIsClosed: false, new TimeOnly(10, 0), new TimeOnly(22, 0));

    private static AssociateParkHoursView BuyoutParkHours(TimeOnly opening, TimeOnly closing) => new(
        ParkHoursCalculator.Calculate(true, null, null, false, null, null, true, opening, closing),
        AssociateVerificationInfo.NotCheckedYet, OfficialIsClosed: true, OfficialOpening: null, OfficialClosing: null);

    private static AssociateParkHoursView NotCheckedParkHoursView() => FakeOperationsCalendarService.NotCheckedParkHours;
}
