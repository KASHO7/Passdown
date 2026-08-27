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

    // #3: PreviousMonth/NextMonth move DisplayedMonth and reload, without touching SelectedDate.
    [Fact]
    public async Task PreviousAndNextMonthCommands_MoveDisplayedMonth_PreserveSelectedDate()
    {
        var service = new FakeOperationsCalendarService();
        var vm = new OperationsCalendarViewModel(service, CreateClock());
        await vm.PendingLoad;

        vm.PreviousMonthCommand.Execute(null);
        await vm.PendingLoad;
        Assert.Equal(new DateOnly(2026, 7, 1), vm.DisplayedMonth);
        Assert.Equal(FixedToday, vm.SelectedDate);

        vm.NextMonthCommand.Execute(null);
        vm.NextMonthCommand.Execute(null);
        await vm.PendingLoad;
        Assert.Equal(new DateOnly(2026, 9, 1), vm.DisplayedMonth);
        Assert.Equal(FixedToday, vm.SelectedDate);
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

        bool raised = false;
        vm.OpenHistoryRequested += (_, _) => raised = true;
        vm.OpenHistoryCommand.Execute(null);
        Assert.True(raised);
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

    // #13 (spec Part I): every visual indicator on a day cell has a textual equivalent in its
    // automation name — never color-only. Asserted directly here since an ItemsControl's generated
    // item containers aren't realized by the headless View tests' GetLogicalDescendants() without a
    // layout pass (see OperationsCalendarViewTests).
    [Fact]
    public void DayViewModel_AutomationName_IncludesEveryTextualIndicator()
    {
        var day = new OperationsCalendarDay(
            new DateOnly(2026, 8, 21), IsInDisplayedMonth: true, IsToday: true,
            HasFinalizedPassdown: true, HasAssociateDraft: true, RelevantOperationalNoteCount: 2,
            OperationsCalendarOperatingState.Open, OpenParkHours(new TimeOnly(10, 0), new TimeOnly(22, 0)));

        var dayViewModel = new OperationsCalendarDayViewModel(day, isSelected: true, _ => { });

        Assert.Contains("August 21, 2026", dayViewModel.AutomationName);
        Assert.Contains("Open, 10 AM to 10 PM", dayViewModel.AutomationName);
        Assert.Contains("Today", dayViewModel.AutomationName);
        Assert.Contains("Selected", dayViewModel.AutomationName);
        Assert.Contains("Finalized", dayViewModel.AutomationName);
        Assert.Contains("Draft in progress", dayViewModel.AutomationName);
        Assert.Contains("2 operational notes", dayViewModel.AutomationName);
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
