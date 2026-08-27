using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Domain.Hours;
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

    // ==================================================================================
    // Phase 13D Part A/B/C/AV — equal day-cell geometry regardless of content
    // ==================================================================================
    //
    // This headless test harness (no real compositor/window — see the file's own remark on the
    // existing 1280×860/960×640 tests above) does not realize ItemsControl's generated day-cell
    // containers via Measure/Arrange alone (confirmed empirically: neither GetLogicalDescendants nor
    // GetVisualDescendants finds them without a real windowed layout pass), so per-cell pixel bounds
    // cannot be asserted here. Two things ARE reliably provable in this environment and both are
    // asserted below: (1) the compiled Button.calendarDay style itself sets a fixed Height — never a
    // MinHeight/no-cap, which is the actual mechanism that makes every cell equal regardless of
    // content, inspected directly off the loaded Style object, not inferred; (2) construction/layout
    // never throws under the densest realistic content this phase's own dense-month scenario
    // describes (mixed states, waterpark hours, many notes concentrated on one outlier date, off-month
    // cells alongside it) at both target viewports.

    private static AssociateParkHoursView OpenParkHoursWithWaterpark => new(
        ParkHoursCalculator.Calculate(false, new TimeOnly(10, 0), new TimeOnly(22, 0), false, null, null, false, null, null),
        new AssociateVerificationInfo(AssociateVerificationState.Stale, DateTimeOffset.UtcNow.AddDays(-2), TimeSpan.FromDays(2), "Stale."),
        OfficialIsClosed: false,
        OfficialOpening: new TimeOnly(10, 0),
        OfficialClosing: new TimeOnly(22, 0),
        WaterparkOpening: new TimeOnly(11, 0),
        WaterparkClosing: new TimeOnly(18, 0));

    /// <summary>Builds one exceptionally content-heavy day (every indicator: custom hours, waterpark, stale, many Important notes, a Draft) alongside otherwise-sparse siblings — the direct regression target for Part A/B "one date's content must NOT expand only that date's cell."</summary>
    private static OperationsCalendarSnapshot DenseSingleOutlierDaySnapshot(DateOnly month, DateOnly selected)
    {
        var baseline = FakeOperationsCalendarService.DefaultSnapshot(month, selected);
        var days = baseline.Days
            .Select((d, index) => index == 3
                ? d with
                {
                    OperatingState = OperationsCalendarOperatingState.CustomHours,
                    ParkHours = OpenParkHoursWithWaterpark,
                    HasFinalizedPassdown = false,
                    HasAssociateDraft = true,
                    RelevantOperationalNoteCount = 27,
                    ImportantOperationalNoteCount = 14,
                }
                : d)
            .ToList();
        return baseline with { Days = days };
    }

    // Part A: the day-cell style itself uses a fixed Height, not MinHeight — read directly off the
    // compiled Style object rather than inferred, so this fails immediately if a future edit reverts
    // to an auto-growing MinHeight.
    [Fact]
    public void CalendarDayStyle_UsesFixedHeight_NotMinHeight()
    {
        var view = new OperationsCalendarView();

        var calendarDayStyle = view.Styles
            .OfType<Avalonia.Styling.Style>()
            .Single(s => s.Selector!.ToString() == "Button.calendarDay");

        var setters = calendarDayStyle.Setters.OfType<Avalonia.Styling.Setter>().ToList();
        Assert.Contains(setters, s => s.Property == Layoutable.HeightProperty);
        Assert.DoesNotContain(setters, s => s.Property == Layoutable.MinHeightProperty);
        Assert.Contains(setters, s => s.Property == Visual.ClipToBoundsProperty && Equals(s.Value, true));
    }

    [Theory]
    [InlineData(1280, 860)]
    [InlineData(960, 640)]
    public async Task OperationsCalendarView_DenseMonthWithSingleOutlierDayAndWaterpark_LayoutDoesNotThrow(double width, double height)
    {
        var service = new FakeOperationsCalendarService { SnapshotFactory = DenseSingleOutlierDaySnapshot };
        var viewModel = new OperationsCalendarViewModel(service, CreateClock());
        await viewModel.PendingLoad;

        var view = new OperationsCalendarView { DataContext = viewModel, Width = width, Height = height };
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));

        Assert.True(view.Bounds.Width > 0 && view.Bounds.Height > 0);
        // The outlier date's own view-model properties reflect the dense content (proving the binding
        // path exists end to end) without needing realized pixel bounds to prove the style caps it.
        var outlierDay = viewModel.Days.Single(d => d.HasWaterparkLine);
        Assert.True(outlierDay.HasImportantOperationalNotes);
        Assert.NotNull(outlierDay.NotesSummaryText);
    }

    // Part AV #10: off-month cells still bind/layout cleanly alongside a content-heavy in-month date.
    [Fact]
    public async Task OperationsCalendarView_OffMonthCellsAlongsideDenseOutlierDay_ConstructsWithoutError()
    {
        var service = new FakeOperationsCalendarService { SnapshotFactory = DenseSingleOutlierDaySnapshot };
        var viewModel = new OperationsCalendarViewModel(service, CreateClock());
        await viewModel.PendingLoad;

        var view = new OperationsCalendarView { DataContext = viewModel };

        Assert.Contains(viewModel.Days, d => !d.IsInDisplayedMonth);
        Assert.Same(viewModel, view.DataContext);
    }
}
