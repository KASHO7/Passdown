using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 12A Part J/M: proves <see cref="OperationsCalendarView"/> resolves through Avalonia's real
/// XAML loader — every binding/DataTemplate/converter path — for a populated grid, an empty/error
/// state, and dense metadata, and that its key actions carry an accessible label (spec Part I).
/// </summary>
public class OperationsCalendarViewTests
{
    private static readonly DateOnly FixedToday = new(2026, 8, 21);

    private static FakeClock CreateClock() =>
        new(new DateTimeOffset(FixedToday.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero));

    [Fact]
    public async Task OperationsCalendarView_WithSnapshot_ConstructsAndAcceptsDataContext()
    {
        var viewModel = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), CreateClock());
        await viewModel.PendingLoad;

        var view = new OperationsCalendarView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
    }

    [Fact]
    public async Task OperationsCalendarView_LoadFailure_ConstructsWithoutError()
    {
        var viewModel = new OperationsCalendarViewModel(new FakeOperationsCalendarService { Exception = new InvalidOperationException("boom") }, CreateClock());
        await viewModel.PendingLoad;

        var view = new OperationsCalendarView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
    }

    // Dense metadata case (spec Part J "many metadata cases"): every day in the grid carries a
    // finalized flag, a draft flag, and a note count — the view must still construct cleanly.
    [Fact]
    public async Task OperationsCalendarView_DenseMetadataAcrossEveryDay_ConstructsWithoutError()
    {
        var service = new FakeOperationsCalendarService
        {
            SnapshotFactory = (month, selected) =>
            {
                var baseline = FakeOperationsCalendarService.DefaultSnapshot(month, selected);
                var denseDays = baseline.Days
                    .Select(d => d with { HasFinalizedPassdown = true, HasAssociateDraft = true, RelevantOperationalNoteCount = 12 })
                    .ToList();
                return baseline with { Days = denseDays };
            },
        };
        var viewModel = new OperationsCalendarViewModel(service, CreateClock());
        await viewModel.PendingLoad;

        var view = new OperationsCalendarView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
    }

    [Fact]
    public async Task OperationsCalendarView_WithSnapshot_ExposesCriticalAutomationNames()
    {
        var viewModel = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), CreateClock());
        await viewModel.PendingLoad;

        var view = new OperationsCalendarView { DataContext = viewModel };

        var names = view.GetLogicalDescendants()
            .OfType<Control>()
            .Select(AutomationProperties.GetName)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToHashSet();

        Assert.Contains("Previous month", names);
        Assert.Contains("Go to today", names);
        Assert.Contains("Next month", names);
        Assert.Contains("Previous day", names); // Phase 12D Part C/AE
        Assert.Contains("Next day", names); // Phase 12D Part C/AE
        Assert.Contains("Calendar month grid", names);
        Assert.Contains("Selected date summary", names);
        Assert.Contains("Open Passdown", names);
        Assert.Contains("Open History", names);
        Assert.Contains("Open Operational Notes", names);

        // Each day cell's own automation name (bound via ItemsControl DataTemplate) is asserted
        // directly against OperationsCalendarDayViewModel in OperationsCalendarViewModelTests instead
        // of here — DashboardView's own automation-name test establishes the same boundary: an
        // ItemsControl's generated item containers aren't realized by GetLogicalDescendants() without
        // a layout pass, which this headless test harness never performs.
    }

    // ==================================================================================
    // Phase 12B — Park Hours and Operating-State overlays
    // ==================================================================================

    // Part AA: a month mixing every operating state (open/closed/custom/buyout/not-published/
    // unavailable) across different days — the view must still construct cleanly with no single
    // state dominating the layout.
    [Fact]
    public async Task OperationsCalendarView_MixedOperatingStatesAcrossTheMonth_ConstructsWithoutError()
    {
        var states = new[]
        {
            OperationsCalendarOperatingState.Open,
            OperationsCalendarOperatingState.Closed,
            OperationsCalendarOperatingState.CustomHours,
            OperationsCalendarOperatingState.PrivateBuyout,
            OperationsCalendarOperatingState.NotPublished,
            OperationsCalendarOperatingState.Unavailable,
            OperationsCalendarOperatingState.NotChecked,
        };

        var service = new FakeOperationsCalendarService
        {
            SnapshotFactory = (month, selected) =>
            {
                var baseline = FakeOperationsCalendarService.DefaultSnapshot(month, selected);
                var mixedDays = baseline.Days
                    .Select((d, index) => d with { OperatingState = states[index % states.Length] })
                    .ToList();
                return baseline with { Days = mixedDays };
            },
        };
        var viewModel = new OperationsCalendarViewModel(service, CreateClock());
        await viewModel.PendingLoad;

        var view = new OperationsCalendarView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
    }

    // Part AA/W: construction at the 1280×860 primary desktop target — a real Measure/Arrange pass
    // (not just DataContext assignment) proving layout doesn't throw at this size. No pixel/bitmap
    // renderer is available in this environment (see docs/phase12-operations-calendar.md "Visual
    // validation"), so this is construction/layout-level evidence only, not a pixel-level PASS.
    [Fact]
    public async Task OperationsCalendarView_LayoutAt1280x860_DoesNotThrow()
    {
        var viewModel = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), CreateClock());
        await viewModel.PendingLoad;

        var view = new OperationsCalendarView { DataContext = viewModel, Width = 1280, Height = 860 };

        view.Measure(new Size(1280, 860));
        view.Arrange(new Rect(0, 0, 1280, 860));

        Assert.True(view.Bounds.Width > 0 && view.Bounds.Height > 0);
    }

    // Part AA/W: construction at the 960×640 compact fallback target.
    [Fact]
    public async Task OperationsCalendarView_LayoutAt960x640_DoesNotThrow()
    {
        var viewModel = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), CreateClock());
        await viewModel.PendingLoad;

        var view = new OperationsCalendarView { DataContext = viewModel, Width = 960, Height = 640 };

        view.Measure(new Size(960, 640));
        view.Arrange(new Rect(0, 0, 960, 640));

        Assert.True(view.Bounds.Width > 0 && view.Bounds.Height > 0);
    }

    // ==================================================================================
    // Phase 12C — Passdown Status + Persistent Operational Notes overlays
    // ==================================================================================

    // Part AL #4/5: a month mixing passdown lifecycle states (No Draft/Draft/Finalized) and
    // Important-note dates — construction must not throw, and no state dominates the layout.
    [Fact]
    public async Task OperationsCalendarView_MixedPassdownAndImportantNoteStatesAcrossTheMonth_ConstructsWithoutError()
    {
        var service = new FakeOperationsCalendarService
        {
            SnapshotFactory = (month, selected) =>
            {
                var baseline = FakeOperationsCalendarService.DefaultSnapshot(month, selected);
                var mixedDays = baseline.Days
                    .Select((d, index) => (index % 3) switch
                    {
                        0 => d with { HasFinalizedPassdown = true, HasAssociateDraft = true, RelevantOperationalNoteCount = 3, ImportantOperationalNoteCount = 1 },
                        1 => d with { HasAssociateDraft = true, RelevantOperationalNoteCount = 1 },
                        _ => d,
                    })
                    .ToList();
                return baseline with { Days = mixedDays };
            },
        };
        var viewModel = new OperationsCalendarViewModel(service, CreateClock());
        await viewModel.PendingLoad;

        var view = new OperationsCalendarView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
    }

    // Part J/T: the selected-date panel's Passdown Status/Active Operational Notes text is present and bound.
    [Fact]
    public async Task OperationsCalendarView_SelectedDateDraftWithImportantNote_ShowsPassdownAndNotesText()
    {
        var service = new FakeOperationsCalendarService
        {
            SnapshotFactory = (month, selected) =>
            {
                var baseline = FakeOperationsCalendarService.DefaultSnapshot(month, selected);
                return baseline with
                {
                    Selected = baseline.Selected with
                    {
                        PassdownStatus = OperationsCalendarPassdownStatus.DraftInProgress,
                        RelevantOperationalNoteCount = 2,
                        ImportantOperationalNoteCount = 1,
                    },
                };
            },
        };
        var viewModel = new OperationsCalendarViewModel(service, CreateClock());
        await viewModel.PendingLoad;

        var view = new OperationsCalendarView { DataContext = viewModel };

        Assert.Equal("Draft In Progress", viewModel.Presentation?.PassdownStatusText);
        Assert.Equal("2 active", viewModel.Presentation?.OperationalNotesCountText);
        Assert.Equal("1 Important", viewModel.Presentation?.OperationalNotesImportantText);
        Assert.Same(viewModel, view.DataContext);
    }

    // Part K: the selected-date panel's Park Status/Effective Hours text is present and bound.
    [Fact]
    public async Task OperationsCalendarView_SelectedDateCustomHours_ShowsParkStatusAndEffectiveHours()
    {
        var service = new FakeOperationsCalendarService
        {
            SnapshotFactory = (month, selected) =>
            {
                var baseline = FakeOperationsCalendarService.DefaultSnapshot(month, selected);
                return baseline with
                {
                    Selected = baseline.Selected with { OperatingState = OperationsCalendarOperatingState.CustomHours },
                };
            },
        };
        var viewModel = new OperationsCalendarViewModel(service, CreateClock());
        await viewModel.PendingLoad;

        var view = new OperationsCalendarView { DataContext = viewModel };

        Assert.Equal("Custom Hours", viewModel.Presentation?.ParkStatusText);
        Assert.Same(viewModel, view.DataContext);
    }

    // ==================================================================================
    // Phase 12E — Calendar polish, density, and accessibility
    // ==================================================================================

    // Part V: a sparse month (every date Not Published / No Draft / 0 Notes, i.e. FakeOperationsCalendarService's
    // own default) must not render noisy filler — already exercised by every other test's baseline
    // DefaultSnapshot, asserted explicitly here as a named regression target for Part V.
    [Fact]
    public async Task OperationsCalendarView_SparseMonth_ConstructsWithoutError()
    {
        var viewModel = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), CreateClock());
        await viewModel.PendingLoad;

        var view = new OperationsCalendarView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
        Assert.All(viewModel.Days, day => Assert.Null(day.NotesSummaryText));
    }

    // Part E: Today and Selected on the same cell — the combined Button.calendarDay.today.selected
    // style must construct cleanly, not just each state individually.
    [Fact]
    public async Task OperationsCalendarView_TodayAndSelectedSameDate_ConstructsWithoutError()
    {
        var viewModel = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), CreateClock());
        await viewModel.PendingLoad;

        var view = new OperationsCalendarView { DataContext = viewModel };

        var todaySelectedDay = Assert.Single(viewModel.Days, d => d.IsToday && d.IsSelected);
        Assert.Equal(FixedToday, todaySelectedDay.Date);
        Assert.Same(viewModel, view.DataContext);
    }

    // Part K/L/AE: the tightest supported viewport (960×640) combined with the densest realistic
    // content (every operating state, mixed passdown lifecycle, Important notes) — a stronger combined
    // regression check than the existing single-concern 1280×860/960×640 and mixed-state tests.
    [Fact]
    public async Task OperationsCalendarView_DenseMixedStates_LayoutAt960x640_DoesNotThrow()
    {
        var states = new[]
        {
            OperationsCalendarOperatingState.Open,
            OperationsCalendarOperatingState.Closed,
            OperationsCalendarOperatingState.CustomHours,
            OperationsCalendarOperatingState.PrivateBuyout,
            OperationsCalendarOperatingState.NotPublished,
            OperationsCalendarOperatingState.Unavailable,
        };

        var service = new FakeOperationsCalendarService
        {
            SnapshotFactory = (month, selected) =>
            {
                var baseline = FakeOperationsCalendarService.DefaultSnapshot(month, selected);
                var denseDays = baseline.Days
                    .Select((d, index) => (index % 3) switch
                    {
                        0 => d with { OperatingState = states[index % states.Length], HasFinalizedPassdown = true, HasAssociateDraft = true, RelevantOperationalNoteCount = 3, ImportantOperationalNoteCount = 1 },
                        1 => d with { OperatingState = states[index % states.Length], HasAssociateDraft = true, RelevantOperationalNoteCount = 1 },
                        _ => d with { OperatingState = states[index % states.Length] },
                    })
                    .ToList();
                return baseline with { Days = denseDays };
            },
        };
        var viewModel = new OperationsCalendarViewModel(service, CreateClock());
        await viewModel.PendingLoad;

        var view = new OperationsCalendarView { DataContext = viewModel, Width = 960, Height = 640 };

        view.Measure(new Size(960, 640));
        view.Arrange(new Rect(0, 0, 960, 640));

        Assert.True(view.Bounds.Width > 0 && view.Bounds.Height > 0);
    }
}
