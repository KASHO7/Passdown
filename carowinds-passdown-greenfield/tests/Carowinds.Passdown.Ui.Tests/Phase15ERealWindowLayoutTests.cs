using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Infrastructure.Time;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 15E Part 17: extends the real-window layout evidence Phase 15B introduced for
/// <c>OperationsCalendarView</c> to the app shell and the remaining major pages. A plain
/// <c>Measure()</c>/<c>Arrange()</c> pass (used by the pre-15E layout tests) proves layout doesn't
/// throw, but does not catch a control being pushed past the window's own right edge — which is
/// exactly the class of defect this phase found and fixed in three places (the app-shell section
/// strip, the Associate action bar, and the Dashboard header) before any test here existed. A real,
/// off-screen headless <see cref="Window"/> is required to realize actual control <see cref="Rect"/>
/// bounds; see <c>OperationsCalendarViewTests.RealizeCalendarLayoutAsync</c>'s own doc comment for why
/// this assembly's headless platform needs a real Window, why every Window-touching test must run on
/// the same OS thread the assembly's single, non-parallel test collection normally guarantees, and why
/// that guarantee is occasionally violated (a known, pre-existing, documented flake — not something
/// this phase introduced or was able to eliminate without a riskier mechanism that deadlocked outright).
/// </summary>
public class Phase15ERealWindowLayoutTests
{
    private static readonly DateOnly FixedDate = new(2026, 8, 21);

    private static FakeClock CreateClock() =>
        new(new DateTimeOffset(FixedDate.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero));

    private static Window RealizeInWindow(Control content, double width, double height)
    {
        var window = new Window { Width = width, Height = height, Content = content };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Rect AbsoluteBounds(Visual control, Window window) =>
        new(control.TranslatePoint(new Point(0, 0), window) ?? default, control.Bounds.Size);

    /// <summary>
    /// No realized, non-collapsed <see cref="Button"/> or <see cref="TextBox"/> may have its right edge
    /// past the window's own client width, and every one must have a non-zero width — the general
    /// shape of every defect this phase found (a control silently pushed off-window by an
    /// Auto/Auto/Auto row with no reflow fallback). A small tolerance absorbs normal sub-pixel
    /// rounding, matching <c>OperationsCalendarViewTests</c>'s own established convention.
    /// </summary>
    private static void AssertNoControlClipsHorizontally(Window window)
    {
        var clientWidth = window.ClientSize.Width;
        var controls = window.GetVisualDescendants().OfType<Button>().Cast<Control>()
            .Concat(window.GetVisualDescendants().OfType<TextBox>());

        foreach (var control in controls)
        {
            if (control.Bounds.Width <= 0 && control.Bounds.Height <= 0)
                continue; // collapsed (IsVisible="False" ancestor) — not a reachability concern.

            var bounds = AbsoluteBounds(control, window);
            var label = AutomationProperties.GetName(control) ?? (control as Button)?.Content as string ?? control.GetType().Name;
            Assert.True(bounds.Width > 0, $"'{label}' realized with zero width — it is unreachable.");
            Assert.True(bounds.Right <= clientWidth + 1.0,
                $"'{label}' right edge {bounds.Right} exceeds window client width {clientWidth}.");
        }
    }

    // ==================================================================================
    // App shell — section strip (regression: MainWindow.axaml's WrapPanel fix)
    // ==================================================================================

    private static MainViewModel CreateMainViewModel()
    {
        var dashboard = new OperationsDashboardViewModel(new FakeOperationsDashboardService());
        var calendar = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), CreateClock());
        var associate = new AssociateViewModel(new FakeAssociatePassdownService(), CreateClock());
        var notes = new OperationalNotesViewModel(new FakeOperationalNotesService(), new SystemClock());
        var historyService = new FakePassdownHistoryService
        {
            Summaries = [new FinalizedPassdownSummary(FixedDate, DateTimeOffset.UtcNow, FinalizationOrigin.Scheduled, true, 100)],
        };
        var history = new PassdownHistoryViewModel(historyService, new FakeFinalizedPassdownPdfRetrievalService());
        var settings = new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator());
        return new MainViewModel(dashboard, calendar, associate, notes, history, settings);
    }

    [Theory]
    [InlineData(1280d, 860d)]
    [InlineData(960d, 640d)]
    public void MainWindow_SectionStrip_EveryButtonReachableWithinWindowBounds(double width, double height)
    {
        // MainWindow is itself a Window (unlike every other view under test here, which is a
        // UserControl hosted inside a plain diagnostic Window) — show it directly rather than
        // nesting it as another Window's Content, which Avalonia rejects.
        var window = new MainWindow { DataContext = CreateMainViewModel(), Width = width, Height = height };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var sectionButtons = window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("appSectionItem")).ToList();
        Assert.Equal(6, sectionButtons.Count);

        var clientWidth = window.ClientSize.Width;
        foreach (var button in sectionButtons)
        {
            var bounds = AbsoluteBounds(button, window);
            Assert.True(bounds.Width > 0, $"section button '{button.Content}' realized with zero width.");
            Assert.True(bounds.Right <= clientWidth + 1.0,
                $"section button '{button.Content}' right edge {bounds.Right} exceeds window client width {clientWidth}.");
        }

        window.Close();
    }

    // ==================================================================================
    // Associate action bar (regression: AssociateView.axaml's WrapPanel fix)
    // ==================================================================================

    [Theory]
    [InlineData(1280d, 860d)]
    [InlineData(960d, 640d)]
    [InlineData(1280d, 690d)] // ≈ a maximized window on a 1920×1080 display at 150% scaling.
    public async Task AssociateView_ActionBar_EveryActionReachableWithinWindowBounds(double width, double height)
    {
        var service = new FakeAssociatePassdownService();
        var viewModel = new AssociateViewModel(service, CreateClock());
        await viewModel.PendingLoad;
        viewModel.ManualHoursOverrideEnabled = true;
        viewModel.ManualOpeningTimeText = "9:00 AM";
        viewModel.ManualClosingTimeText = "10:00 PM";
        viewModel.PrivateBuyoutEnabled = true;
        viewModel.PrivateBuyoutOpeningTimeText = "6:00 PM";
        viewModel.PrivateBuyoutClosingTimeText = "11:00 PM";

        var view = new AssociateView { DataContext = viewModel };
        var window = RealizeInWindow(view, width, height);

        // Phase 21B: date navigation (‹ date › Today) plus the passdown actions; Export Diagnostics now
        // lives in Settings → Diagnostics and must not be on this bar at all.
        var actionNames = new[] { "Previous day", "Operating date", "Next day", "Today", "Check this date online", "Create PDF", "Copy for Teams", "More passdown actions" };
        var buttonsByName = window.GetVisualDescendants().OfType<Button>()
            .ToDictionary(b => AutomationProperties.GetName(b) ?? string.Empty, b => b);

        var clientWidth = window.ClientSize.Width;
        foreach (var name in actionNames)
        {
            Assert.True(buttonsByName.TryGetValue(name, out var button), $"expected action '{name}' was not realized at all.");
            var bounds = AbsoluteBounds(button!, window);
            Assert.True(bounds.Width > 0, $"action '{name}' realized with zero width — it is unreachable.");
            Assert.True(bounds.Right <= clientWidth + 1.0,
                $"action '{name}' right edge {bounds.Right} exceeds window client width {clientWidth}.");
        }

        Assert.DoesNotContain("Export Diagnostics", buttonsByName.Keys);

        // No two toolbar controls overlap, however the bar wraps.
        var barBounds = actionNames.Select(name => (name, bounds: AbsoluteBounds(buttonsByName[name], window))).ToList();
        for (var i = 0; i < barBounds.Count; i++)
        {
            for (var j = i + 1; j < barBounds.Count; j++)
            {
                var overlap = barBounds[i].bounds.Intersect(barBounds[j].bounds);
                Assert.True(overlap.Width <= 0.5 || overlap.Height <= 0.5,
                    $"'{barBounds[i].name}' overlaps '{barBounds[j].name}'.");
            }
        }

        window.Close();
    }

    // ==================================================================================
    // Phase 21B: Associate date popup (real window — the Calendar's day cells only exist once the
    // popup's template is realized)
    // ==================================================================================

    private static async Task<(Window Window, AssociateView View, AssociateViewModel ViewModel, FakeAssociatePassdownService Service)> RealizeAssociateAsync(double width = 1280d, double height = 860d)
    {
        var service = new FakeAssociatePassdownService();
        var viewModel = new AssociateViewModel(service, CreateClock(), templatePreferenceStore: new FakeAssociatePassdownTemplatePreferenceStore());
        await viewModel.PendingLoad;
        var view = new AssociateView { DataContext = viewModel };
        return (RealizeInWindow(view, width, height), view, viewModel, service);
    }

    private static Calendar OpenDatePopup(AssociateView view)
    {
        var dateButton = view.FindControl<Button>("DateButton")!;
        dateButton.Flyout!.ShowAt(dateButton);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(dateButton.Flyout.IsOpen);
        return view.FindControl<Calendar>("DateCalendar")!;
    }

    [Theory]
    [InlineData(1280d, 860d)]
    [InlineData(960d, 640d)]
    public async Task DatePopup_OpensOnTheSelectedDate_StaysOnScreen_AndMarksSelectedAndToday(double width, double height)
    {
        var (window, view, viewModel, _) = await RealizeAssociateAsync(width, height);
        viewModel.SelectedDate = FixedDate.AddDays(-40); // a different month than today.
        await viewModel.PendingLoad;

        var calendar = OpenDatePopup(view);

        Assert.Equal(CalendarMode.Month, calendar.DisplayMode);
        Assert.Equal(FixedDate.AddDays(-40).ToDateTime(TimeOnly.MinValue), calendar.SelectedDate);
        Assert.Equal((FixedDate.AddDays(-40).Year, FixedDate.AddDays(-40).Month), (calendar.DisplayDate.Year, calendar.DisplayDate.Month));

        var dayButtons = calendar.GetVisualDescendants().OfType<CalendarDayButton>().ToList();
        Assert.Equal(42, dayButtons.Count); // six full weeks: a consistent seven-column grid.
        Assert.Single(dayButtons, b => b.Classes.Contains(":selected"));
        Assert.Contains(dayButtons, b => b.Classes.Contains(":inactive")); // adjacent-month days are present (muted).

        var popupBounds = AbsoluteBounds(calendar, window);
        Assert.True(popupBounds.X >= 0 && popupBounds.Y >= 0, $"popup at {popupBounds} starts off-window.");
        Assert.True(popupBounds.Right <= window.ClientSize.Width + 1.0 && popupBounds.Bottom <= window.ClientSize.Height + 1.0,
            $"popup at {popupBounds} extends past the {window.ClientSize} window.");

        view.FindControl<Button>("DateButton")!.Flyout!.Hide();
        window.Close();
    }

    [Fact]
    public async Task DatePopup_ClickingADay_SelectsItExactlyOnce_AndCloses()
    {
        var (window, view, viewModel, service) = await RealizeAssociateAsync();
        var calendar = OpenDatePopup(view);
        var loadsBefore = service.LoadCalls.Count;

        var target = calendar.GetVisualDescendants().OfType<CalendarDayButton>()
            .Single(b => !b.Classes.Contains(":inactive") && Equals(b.DataContext, FixedDate.AddDays(3).ToDateTime(TimeOnly.MinValue)));
        var center = AbsoluteBounds(target, window).Center;
        window.MouseDown(center, Avalonia.Input.MouseButton.Left);
        window.MouseUp(center, Avalonia.Input.MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        await viewModel.PendingLoad;

        Assert.Equal(FixedDate.AddDays(3), viewModel.SelectedDate);
        Assert.Equal([FixedDate.AddDays(3)], service.LoadCalls.Skip(loadsBefore));
        Assert.False(view.FindControl<Button>("DateButton")!.Flyout!.IsOpen);
        window.Close();
    }

    [Fact]
    public async Task DatePopup_MonthAndYearNavigation_NeverSelectsADate()
    {
        var (window, view, viewModel, service) = await RealizeAssociateAsync();
        var calendar = OpenDatePopup(view);
        var loadsBefore = service.LoadCalls.Count;

        // Month → year view (as the heading button does), browse to another year, and page months.
        calendar.DisplayMode = CalendarMode.Year;
        calendar.DisplayDate = calendar.DisplayDate.AddYears(1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        calendar.DisplayMode = CalendarMode.Month;
        calendar.DisplayDate = calendar.DisplayDate.AddMonths(2);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(FixedDate, viewModel.SelectedDate);
        Assert.Equal(loadsBefore, service.LoadCalls.Count);
        Assert.True(view.FindControl<Button>("DateButton")!.Flyout!.IsOpen);

        // Reopening starts again on the selected date's month, not the abandoned browse position.
        view.FindControl<Button>("DateButton")!.Flyout!.Hide();
        calendar = OpenDatePopup(view);
        Assert.Equal((FixedDate.Year, FixedDate.Month), (calendar.DisplayDate.Year, calendar.DisplayDate.Month));
        Assert.Equal(CalendarMode.Month, calendar.DisplayMode);

        view.FindControl<Button>("DateButton")!.Flyout!.Hide();
        window.Close();
    }

    // Phase 21B popup commit/dismiss rules, driven directly (no Window needed). They live in this
    // run-alone class only because Calendar.SelectedDate must be set on the dispatcher thread, which
    // the mixed UI partition does not guarantee (see the class summary).

    [Fact]
    public async Task DatePopup_EnterCommitsTheHighlightedDay_EscapeAndBrowsingChangeNothing()
    {
        var service = new FakeAssociatePassdownService();
        var viewModel = new AssociateViewModel(service, CreateClock(), templatePreferenceStore: new FakeAssociatePassdownTemplatePreferenceStore());
        await viewModel.PendingLoad;
        var view = new AssociateView { DataContext = viewModel };
        var loads = service.LoadCalls.Count;

        view.DateFlyout_OnOpening(null, EventArgs.Empty);
        Assert.Equal(FixedDate.ToDateTime(TimeOnly.MinValue), view.DateCalendar.SelectedDate);
        Assert.Equal(CalendarMode.Month, view.DateCalendar.DisplayMode);

        // Browsing (arrow keys move the Calendar's own highlight) then Escape: nothing is selected.
        view.DateCalendar.SelectedDate = FixedDate.AddDays(5).ToDateTime(TimeOnly.MinValue);
        view.DateCalendar.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        await viewModel.PendingLoad;
        Assert.Equal(FixedDate, viewModel.SelectedDate);
        Assert.Equal(loads, service.LoadCalls.Count);

        // Enter in a year view drills down; it never commits.
        view.DateFlyout_OnOpening(null, EventArgs.Empty);
        view.DateCalendar.DisplayMode = CalendarMode.Year;
        view.DateCalendar.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.Equal(FixedDate, viewModel.SelectedDate);

        // Reopen resets to the current selection; Enter in the month view commits exactly once.
        view.DateFlyout_OnOpening(null, EventArgs.Empty);
        Assert.Equal(FixedDate.ToDateTime(TimeOnly.MinValue), view.DateCalendar.SelectedDate);
        view.DateCalendar.SelectedDate = FixedDate.AddDays(-3).ToDateTime(TimeOnly.MinValue);
        view.DateCalendar.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        await viewModel.PendingLoad;

        Assert.Equal(FixedDate.AddDays(-3), viewModel.SelectedDate);
        Assert.Equal([FixedDate.AddDays(-3)], service.LoadCalls.Skip(loads));
    }

    [Fact]
    public async Task DatePopup_CommittingTheAlreadySelectedDay_DoesNotReload()
    {
        var service = new FakeAssociatePassdownService();
        var viewModel = new AssociateViewModel(service, CreateClock(), templatePreferenceStore: new FakeAssociatePassdownTemplatePreferenceStore());
        await viewModel.PendingLoad;
        var view = new AssociateView { DataContext = viewModel };
        var loads = service.LoadCalls.Count;

        view.DateFlyout_OnOpening(null, EventArgs.Empty);
        view.CommitCalendarSelection();
        await viewModel.PendingLoad;

        Assert.Equal(loads, service.LoadCalls.Count);
    }

    // ==================================================================================
    // Dashboard header (regression: DashboardView.axaml's flexible-title-column fix)
    // ==================================================================================

    [Theory]
    [InlineData(1280d, 860d)]
    [InlineData(960d, 640d)]
    public void DashboardView_Header_OfficialDataStatusStaysWithinWindowBoundsWithARealisticLongDateLabel(double width, double height)
    {
        // Phase 19A: the header's former Refresh button is replaced by the shared official-data
        // freshness line; its longest realistic state (partial failure, with the failed-source detail
        // line) must stay fully inside the window next to a realistic long operating-day label
        // ("Friday, August 21, 2026", from the same production presentation formatter real data uses).
        var coordinator = new FakeOfficialDataRefreshCoordinator();
        var checkedAt = new DateTimeOffset(2026, 8, 21, 16, 41, 0, TimeSpan.Zero);
        coordinator.SetStatus(new OfficialDataStatus(OfficialDataCheckState.PartiallyFailed, checkedAt, null, checkedAt,
        [
            new OfficialSourceIssue("dated-fast-lane.official", SourceCategory.DatedFastLane),
            new OfficialSourceIssue("military-first-responder.official", SourceCategory.MilitaryFirstResponder),
            new OfficialSourceIssue("dated-bring-a-friend.official", SourceCategory.DatedBringAFriend),
        ]));
        var status = new OfficialDataStatusViewModel(coordinator, new FakeClock(checkedAt), new ImmediateUiDispatcher());
        var viewModel = new OperationsDashboardViewModel(new FakeOperationsDashboardService(), officialDataStatus: status);
        var view = new DashboardView { DataContext = viewModel };
        var window = RealizeInWindow(view, width, height);

        var statusText = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "OfficialDataStatusText");
        var bounds = AbsoluteBounds(statusText, window);
        var clientWidth = window.ClientSize.Width;

        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetName(b) == "Refresh today's operations summary");
        Assert.True(bounds.Width > 0, "Official-data status realized with zero width — it is unreadable.");
        Assert.True(bounds.Right <= clientWidth + 1.0,
            $"Official-data status right edge {bounds.Right} exceeds window client width {clientWidth}.");

        window.Close();
    }

    // ==================================================================================
    // General clip sweep across the remaining major pages (Part 17/18): no Button/TextBox pushed
    // past the window's own right edge at the compact 960×640 fallback target.
    // ==================================================================================

    [Fact]
    public void OperationalNotesView_EditorOpen_NoControlClipsHorizontallyAt960x640()
    {
        var viewModel = new OperationalNotesViewModel(new FakeOperationalNotesService(), new SystemClock());
        viewModel.NewNoteCommand.Execute(null);

        var view = new OperationalNotesView { DataContext = viewModel };
        var window = RealizeInWindow(view, 960, 640);

        AssertNoControlClipsHorizontally(window);

        window.Close();
    }

    [Fact]
    public async Task HistoryView_WithSelectedDetail_NoControlClipsHorizontallyAt960x640()
    {
        var snapshot = new FinalizedPassdownSnapshot(
            FinalizedPassdownSnapshot.CurrentSchemaVersion, FixedDate, "Friday, August 21, 2026",
            new FinalizedParkHoursSnapshot(FinalizedParkHoursStatus.Open, "9:00 AM - 10:00 PM", "Open", "North Gate 9:00 AM", "Avenue 9:00 AM", null, null),
            new FinalizedAttendanceSnapshot(12000, 300),
            new FinalizedEarlyRideTimeSnapshot(false, null, []),
            new FinalizedDailyFeatureSnapshot(null, null),
            ["An operational note with a moderate amount of realistic detail text."],
            [new FinalizedProductCategorySnapshot("Daily Admission", FinalizedProductSectionTier.KeySelling,
                [new FinalizedProductRowSnapshot("Celebrate America! Bring-A-Friend Duo Offer", "$50.00 for 2 friends", "$80.00 Gate Price", "Starting From", null)])],
            false,
            "8/21/2026 5:58 PM");

        var service = new FakePassdownHistoryService
        {
            Summaries = [new FinalizedPassdownSummary(FixedDate, DateTimeOffset.UtcNow, FinalizationOrigin.Scheduled, true, 100)],
        };
        service.Details[FixedDate] = new Application.History.PassdownHistoryDetail(service.Summaries[0], snapshot, null);

        var viewModel = new PassdownHistoryViewModel(service, new FakeFinalizedPassdownPdfRetrievalService());
        await viewModel.PendingLoad;
        viewModel.SelectedItem = viewModel.HistoryItems[0];
        await viewModel.PendingDetailLoad;

        var view = new HistoryView { DataContext = viewModel };
        var window = RealizeInWindow(view, 960, 640);

        AssertNoControlClipsHorizontally(window);

        window.Close();
    }

    [Theory]
    [InlineData(1280d, 860d)]
    [InlineData(960d, 640d)]
    public async Task AssociateView_ProductsSection_NoControlClipsHorizontally(double width, double height)
    {
        var service = new FakeAssociatePassdownService();
        var viewModel = new AssociateViewModel(service, CreateClock());
        await viewModel.PendingLoad;
        viewModel.ShowProductsSectionCommand.Execute(null);

        var view = new AssociateView { DataContext = viewModel };
        var window = RealizeInWindow(view, width, height);

        AssertNoControlClipsHorizontally(window);

        window.Close();
    }
}
