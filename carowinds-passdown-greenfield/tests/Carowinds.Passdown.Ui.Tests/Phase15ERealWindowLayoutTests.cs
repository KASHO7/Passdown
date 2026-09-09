using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
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
        var settings = new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncService());
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

        var actionNames = new[] { "Previous day", "Next day", "Check this date online", "Export Diagnostics", "Create PDF" };
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

        window.Close();
    }

    // ==================================================================================
    // Dashboard header (regression: DashboardView.axaml's flexible-title-column fix)
    // ==================================================================================

    [Theory]
    [InlineData(1280d, 860d)]
    [InlineData(960d, 640d)]
    public void DashboardView_Header_RefreshActionStaysWithinWindowBoundsWithARealisticLongDateLabel(double width, double height)
    {
        // FakeOperationsDashboardService's default snapshot runs through the same production
        // OperationsDashboardPresentation.Create formatter real data uses, so OperatingDayLabel is
        // already a realistic long value ("Friday, August 21, 2026") — not a fabricated stress case.
        var viewModel = new OperationsDashboardViewModel(new FakeOperationsDashboardService());
        var view = new DashboardView { DataContext = viewModel };
        var window = RealizeInWindow(view, width, height);

        var refreshButton = window.GetVisualDescendants().OfType<Button>()
            .Single(b => AutomationProperties.GetName(b) == "Refresh today's operations summary");
        var bounds = AbsoluteBounds(refreshButton, window);
        var clientWidth = window.ClientSize.Width;

        Assert.True(bounds.Width > 0, "Refresh action realized with zero width — it is unreachable.");
        Assert.True(bounds.Right <= clientWidth + 1.0,
            $"Refresh action right edge {bounds.Right} exceeds window client width {clientWidth}.");

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
