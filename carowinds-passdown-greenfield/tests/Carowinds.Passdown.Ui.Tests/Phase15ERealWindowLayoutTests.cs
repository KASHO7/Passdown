using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Media;
using Avalonia.VisualTree;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
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
    [InlineData(1280d, 690d)]
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
    // Phase 22B — Fury banner and section navigation (real-window geometry, keyboard, theme)
    // ==================================================================================

    private static MainWindow ShowMainWindow(MainViewModel viewModel, double width, double height)
    {
        var window = new MainWindow { DataContext = viewModel, Width = width, Height = height };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static List<Button> SectionButtons(Window window) =>
        window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("appSectionItem")).ToList();

    private static Control Named(Window window, string name) =>
        window.GetVisualDescendants().OfType<Control>().Single(c => c.Name == name);

    [Theory]
    [InlineData(1280d, 860d)]
    [InlineData(960d, 640d)]
    [InlineData(1280d, 690d)]
    [InlineData(1920d, 1040d)]
    public void MainWindow_Shell_IsCompactAndGeometryStaysFixedAcrossEverySection(double width, double height)
    {
        var viewModel = CreateMainViewModel();
        var window = ShowMainWindow(viewModel, width, height);

        var banner = Named(window, "BrandBanner");
        var navigation = Named(window, "AppNavigation");
        Assert.Equal(72, banner.Bounds.Height, 1);
        Assert.Equal(window.ClientSize.Width, banner.Bounds.Width, 1);
        // This assembly's headless platform measures text with fake metrics (wider than real fonts),
        // so the strip may wrap here where Skia/Windows fonts fit one row (see
        // docs/phase22b-fury-banner-navigation.md §13). Assert the per-row budget, not a row count.
        var rows = SectionButtons(window).Select(b => Math.Round(AbsoluteBounds(b, window).Top)).Distinct().Count();
        Assert.InRange(rows, 1, 2);
        Assert.InRange(navigation.Bounds.Height / rows, 28, 40);
        var shellHeight = banner.Bounds.Height + navigation.Bounds.Height;
        Assert.True(shellHeight <= 72 + 40 * rows, $"shell is {shellHeight}px tall for {rows} navigation row(s)");

        var wordmark = Named(window, "BrandWordmark");
        var wordmarkBounds = AbsoluteBounds(wordmark, window);
        Assert.True(wordmarkBounds.Top >= 0 && wordmarkBounds.Bottom <= banner.Bounds.Height, "wordmark clipped vertically");
        Assert.True(wordmarkBounds.Right < window.ClientSize.Width, "wordmark clipped horizontally");

        var firstLeft = new List<double>();
        foreach (var section in Enum.GetValues<AppSection>())
        {
            viewModel.SelectedSection = section;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var buttons = SectionButtons(window);
            Assert.Equal(6, buttons.Count);
            Assert.Single(buttons, b => b.Classes.Contains("active"));
            var positions = buttons.Select(b => AbsoluteBounds(b, window)).ToList();
            Assert.Equal(rows, positions.Select(p => Math.Round(p.Top)).Distinct().Count());
            for (var i = 1; i < positions.Count; i++)
            {
                Assert.False(positions[i].Intersects(positions[i - 1]), $"navigation items {i - 1} and {i} overlap");
            }
            Assert.Equal(72, banner.Bounds.Height, 1);
            Assert.Equal(shellHeight, banner.Bounds.Height + navigation.Bounds.Height, 1);

            // Selecting a section (SemiBold) never moves any item.
            var lefts = positions.Select(p => p.Left).ToList();
            if (firstLeft.Count == 0) firstLeft = lefts;
            Assert.Equal(firstLeft, lefts);
        }

        window.Close();
    }

    [Fact]
    public void MainWindow_ActiveDestination_FollowsTheSelectedSection_AndOnlyOneIsActive()
    {
        var viewModel = CreateMainViewModel();
        var window = ShowMainWindow(viewModel, 1280, 860);
        var expected = new Dictionary<AppSection, string>
        {
            // Phase 22C: short visible labels; Settings is the gear (identified by its accessible name).
            [AppSection.Dashboard] = "Dashboard", [AppSection.OperationsCalendar] = "Calendar",
            [AppSection.AssociatePassdown] = "Passdown", [AppSection.OperationalNotes] = "Notes",
            [AppSection.History] = "History", [AppSection.Settings] = "Settings",
        };

        foreach (var button in SectionButtons(window).AsEnumerable().Reverse())
        {
            button.Command!.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var active = Assert.Single(SectionButtons(window), b => b.Classes.Contains("active"));
            Assert.Same(button, active);
            Assert.Equal(expected[viewModel.SelectedSection], active.Content as string ?? AutomationProperties.GetName(active));
            Assert.Equal("Current page", AutomationProperties.GetItemStatus(active));
            Assert.All(SectionButtons(window).Where(b => b != active), b => Assert.Null(AutomationProperties.GetItemStatus(b)));
            Assert.Equal(FontWeight.SemiBold, active.FontWeight);
        }

        window.Close();
    }

    [Fact]
    public void MainWindow_Navigation_IsKeyboardReachableInOrder_AndEnterActivatesTheFocusedItem()
    {
        var viewModel = CreateMainViewModel();
        var window = ShowMainWindow(viewModel, 1280, 860);
        var buttons = SectionButtons(window);
        Assert.All(buttons, b => Assert.True(b.Focusable && b.IsTabStop && b.IsEffectivelyEnabled, $"{b.Content} not keyboard reachable"));

        Assert.True(buttons[0].Focus(NavigationMethod.Tab));
        for (var i = 1; i < buttons.Count; i++)
        {
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Same(buttons[i], window.FocusManager!.GetFocusedElement());
        }

        Assert.True(buttons[4].Focus(NavigationMethod.Tab)); // History
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(AppSection.History, viewModel.SelectedSection);
        Assert.True(buttons[4].IsFocused, "activating a section must not drop keyboard focus");

        window.Close();
    }

    // Phase 22C: Settings is a trailing gear — right edge of the strip, after History, reachable by Tab
    // from History, activated by Space, and announced as the current page when selected.
    [Theory]
    [InlineData(1280d, 860d)]
    [InlineData(960d, 640d)]
    public void MainWindow_SettingsGear_IsTrailing_KeyboardOperable_AndShowsTheSelectedState(double width, double height)
    {
        var viewModel = CreateMainViewModel();
        var window = ShowMainWindow(viewModel, width, height);
        var buttons = SectionButtons(window);
        var history = buttons[4];
        var gear = buttons[5];

        Assert.Equal("SettingsNavigationItem", gear.Name);
        Assert.Equal("Settings", AutomationProperties.GetName(gear));
        Assert.Equal("Settings", ToolTip.GetTip(gear));
        Assert.IsType<PathIcon>(gear.Content);
        var gearBounds = AbsoluteBounds(gear, window);
        var navigation = AbsoluteBounds(Named(window, "AppNavigation"), window);
        Assert.Equal(navigation.Right - 10, gearBounds.Right, 1); // strip padding (PassdownNavigationStripPadding) on the right
        Assert.True(gearBounds.Left >= AbsoluteBounds(history, window).Right, "gear must sit after History");
        Assert.True(gearBounds.Height <= AbsoluteBounds(history, window).Height + 0.5, "gear must not make the strip taller");

        Assert.True(history.Focus(NavigationMethod.Tab));
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Same(gear, window.FocusManager!.GetFocusedElement());

        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(AppSection.Settings, viewModel.SelectedSection);
        Assert.Contains("active", gear.Classes);
        Assert.Equal("Current page", AutomationProperties.GetItemStatus(gear));
        Assert.Equal(FontWeight.SemiBold, gear.FontWeight);

        window.Close();
    }

    [Fact]
    public void MainWindow_ThemeSwitch_RestylesNavigation_WhileTheBannerStaysDark()
    {
        var viewModel = CreateMainViewModel();
        var window = ShowMainWindow(viewModel, 1280, 860);
        var navigation = (Border)Named(window, "AppNavigation");
        var banner = (Border)Named(window, "BrandBanner");

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark, ThemeVariant.Light })
        {
            window.RequestedThemeVariant = variant;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            IBrush Token(string key) => window.TryFindResource(key, variant, out var value) ? (IBrush)value! : throw new Xunit.Sdk.XunitException(key);
            var buttons = SectionButtons(window);
            var active = Assert.Single(buttons, b => b.Classes.Contains("active"));
            Assert.Same(Token("PassdownSurfaceBrush"), navigation.Background);
            Assert.Same(Token("PassdownDividerBrush"), navigation.BorderBrush);
            Assert.Same(Token("PassdownTextPrimaryBrush"), active.Foreground);
            Assert.Same(Token("PassdownAccentForegroundBrush"), active.BorderBrush);
            Assert.All(buttons.Where(b => b != active), b => Assert.Same(Token("PassdownTextSecondaryBrush"), b.Foreground));
            Assert.Same(Token("PassdownBrandDarkBrush"), banner.Background);
        }

        window.Close();
    }

    [Fact]
    public void MainWindow_BannerPhotograph_LoadsFromTheBundledResource_AndFillsTheBanner()
    {
        var window = ShowMainWindow(CreateMainViewModel(), 960, 640);
        var image = (Image)Named(window, "BrandBannerImage");
        // A decoded Bitmap from the avares resource (pixel size is not asserted: this assembly's
        // headless drawing platform uses stub bitmaps; the asset's 1920×320 header is checked by
        // Phase22BShellNavigationTests).
        Assert.IsAssignableFrom<Avalonia.Media.Imaging.Bitmap>(image.Source);
        Assert.Equal(Stretch.UniformToFill, image.Stretch);
        Assert.Equal(window.ClientSize.Width, image.Bounds.Width, 1);
        Assert.Equal(72, image.Bounds.Height, 1);
        Assert.False(image.IsHitTestVisible);
        window.Close();
    }

    [Theory]
    [InlineData(960d, 640d)]
    [InlineData(1280d, 690d)]
    public void MainWindow_SectionContent_StartsDirectlyBelowTheShell_AndKeepsUsableHeight(double width, double height)
    {
        var viewModel = CreateMainViewModel();
        var window = ShowMainWindow(viewModel, width, height);
        var navigation = Named(window, "AppNavigation");
        var shellBottom = AbsoluteBounds(navigation, window).Bottom;

        foreach (var section in Enum.GetValues<AppSection>())
        {
            viewModel.SelectedSection = section;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var host = window.GetVisualDescendants().OfType<Border>()
                .Single(b => b.IsVisible && b.Child is UserControl && b.GetVisualParent() is Grid g && g.GetVisualParent() is DockPanel);
            var bounds = AbsoluteBounds(host, window);
            Assert.Equal(shellBottom, bounds.Top, 1);
            Assert.Equal(window.ClientSize.Width, bounds.Width, 1);
            Assert.Equal(window.ClientSize.Height - shellBottom, bounds.Height, 1);
            Assert.True(bounds.Height >= height * 0.75, $"{section}: only {bounds.Height}px of content height");
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
    [InlineData(1920d, 1000d)] // Phase 22D
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
    // Phase 22C — Dashboard Operations Overview (real-window geometry and keyboard disclosure)
    // ==================================================================================

    private static (Window Window, DashboardView View, OperationsDashboardViewModel ViewModel, FakeOperationsDashboardService Service) RealizeBusyDashboard(double width, double height)
    {
        var service = new FakeOperationsDashboardService { SnapshotFactory = Phase22CDashboardFixtures.BusyDayWithPartialFailure };
        var viewModel = new OperationsDashboardViewModel(service);
        var view = new DashboardView { DataContext = viewModel };
        var window = RealizeInWindow(view, width, height);
        viewModel.PendingLoad.GetAwaiter().GetResult();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, view, viewModel, service);
    }

    [Theory]
    [InlineData(960d, 640d, false)]
    [InlineData(960d, 640d, true)]
    [InlineData(1280d, 690d, false)]
    [InlineData(1280d, 860d, true)]
    [InlineData(1920d, 1000d, false)]
    public void DashboardView_OperationsOverview_NoControlClipsHorizontally_CollapsedOrExpanded(double width, double height, bool expanded)
    {
        var (window, _, viewModel, _) = RealizeBusyDashboard(width, height);
        viewModel.IsSourceDetailsExpanded = expanded;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        AssertNoControlClipsHorizontally(window);

        // The four principal areas sit in two columns at every supported width (MinWidth is 960).
        var overview = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "OperationsOverview");
        var tiles = overview.GetVisualDescendants().OfType<StackPanel>().Where(p => p.Classes.Contains("overviewTile")).ToList();
        Assert.Equal(4, tiles.Count);
        var tileBounds = tiles.Select(t => AbsoluteBounds(t, window)).ToList();
        Assert.Equal(tileBounds[0].Top, tileBounds[1].Top, 1);
        Assert.True(tileBounds[1].Left > tileBounds[0].Right, "admission must sit beside park hours");
        Assert.True(tileBounds[2].Top >= tileBounds[0].Bottom, "passdown must sit under park hours");
        Assert.All(tileBounds, b => Assert.True(b.Right <= window.ClientSize.Width + 1.0));

        // A genuine source problem stays visible while the details are collapsed.
        var attention = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "DataAttentionText");
        Assert.True(attention.IsEffectivelyVisible);
        Assert.True(attention.Bounds.Height > 0);

        var expander = window.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "SourceDetailsExpander");
        Assert.Equal(expanded, expander.IsExpanded);

        window.Close();
    }

    [Fact]
    public void DashboardView_SourceDetails_ToggleWithTheKeyboard_ReportsExpandCollapse_AndNeverCallsTheService()
    {
        var (window, _, viewModel, service) = RealizeBusyDashboard(1280, 860);
        var loads = service.LoadCalls.Count;
        var checks = service.CheckOnlineCallCount;
        var expander = window.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "SourceDetailsExpander");
        var header = expander.GetVisualDescendants().OfType<ToggleButton>().Single(t => t.Name == "DisclosureHeader");
        var peer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(expander);
        var expandCollapse = Assert.IsAssignableFrom<Avalonia.Automation.Provider.IExpandCollapseProvider>(peer);

        Assert.False(viewModel.IsSourceDetailsExpanded);
        Assert.Equal(ExpandCollapseState.Collapsed, expandCollapse.ExpandCollapseState);
        Assert.True(header.Focusable && header.IsTabStop);
        Assert.Equal("Source details", AutomationProperties.GetName(header));

        Assert.True(header.Focus(NavigationMethod.Tab));
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.IsSourceDetailsExpanded);
        Assert.True(expander.IsExpanded);
        Assert.Equal(ExpandCollapseState.Expanded, expandCollapse.ExpandCollapseState);
        Assert.Equal("Hide Source Details", viewModel.SourceDetailsToggleText);
        Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetName(b) == "Run Deep Refresh" && b.IsEffectivelyVisible);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(viewModel.IsSourceDetailsExpanded);
        Assert.True(header.IsFocused, "toggling must keep keyboard focus on the disclosure");

        Assert.Equal(loads, service.LoadCalls.Count);
        Assert.Equal(checks, service.CheckOnlineCallCount);
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

    // ==================================================================================
    // Phase 22D: Associate workspace refinement
    // ==================================================================================

    private static async Task<(Window Window, AssociateViewModel ViewModel, FakeAssociatePassdownService Service)> RealizeBusyAssociateAsync(double width, double height, ThemeVariant theme)
    {
        var service = new FakeAssociatePassdownService
        {
            WorkspaceFactory = date =>
            {
                var workspace = FakeAssociatePassdownService.DefaultWorkspace(date);
                return workspace with
                {
                    Draft = workspace.Draft with
                    {
                        ProjectedAttendance = 38500,
                        PreSolds = 21240,
                        DailyFeatureText = @"Synthetic: what is \(\frac{1}{2} + x^2\)?",
                        OperationalNotes = ["Synthetic note line one.", "Synthetic note line two."],
                    },
                };
            },
        };
        var viewModel = new AssociateViewModel(service, CreateClock());
        await viewModel.PendingLoad;
        viewModel.ManualHoursOverrideEnabled = true;
        viewModel.PrivateBuyoutEnabled = true;

        var window = RealizeInWindow(new AssociateView { DataContext = viewModel }, width, height);
        window.RequestedThemeVariant = theme;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, viewModel, service);
    }

    [Theory]
    [InlineData(1280d, 860d, "Light")]
    [InlineData(960d, 640d, "Light")]
    [InlineData(1280d, 690d, "Dark")]
    [InlineData(1920d, 1000d, "Dark")]
    [InlineData(960d, 640d, "Dark")]
    public async Task AssociateView_EverySection_NoControlClipsHorizontally(double width, double height, string theme)
    {
        var (window, viewModel, _) = await RealizeBusyAssociateAsync(width, height, theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);

        AssertNoControlClipsHorizontally(window); // Overview
        viewModel.ShowProductsSectionCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AssertNoControlClipsHorizontally(window);
        viewModel.ShowNotesSectionCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AssertNoControlClipsHorizontally(window);

        window.Close();
    }

    [Theory]
    [InlineData(1280d, 860d)]
    [InlineData(960d, 640d)]
    public async Task AssociateView_Overview_ParkHoursAndAttendanceShareTheFirstRow(double width, double height)
    {
        var (window, _, _) = await RealizeBusyAssociateAsync(width, height, ThemeVariant.Light);
        var hours = window.GetVisualDescendants().OfType<StackPanel>().Single(p => AutomationProperties.GetName(p) == "Park hours");
        var attendance = window.GetVisualDescendants().OfType<StackPanel>().Single(p => AutomationProperties.GetName(p) == "Attendance");

        var hoursBounds = AbsoluteBounds(hours, window);
        var attendanceBounds = AbsoluteBounds(attendance, window);
        Assert.True(Math.Abs(hoursBounds.Top - attendanceBounds.Top) < 1, "the two official/manual regions should start on one row.");
        Assert.True(hoursBounds.Right <= attendanceBounds.Left, "park hours and attendance overlap.");
        Assert.True(attendanceBounds.Bottom < height, "attendance is not above the fold.");

        window.Close();
    }

    [Fact]
    public async Task AssociateView_CountBox_RegroupsWhenFocusLeaves_WithoutAnExtraSave()
    {
        var (window, viewModel, service) = await RealizeBusyAssociateAsync(1280, 860, ThemeVariant.Light);
        var box = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "ProjectedAttendanceTextBox");
        var other = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PreSoldsTextBox");
        Assert.Equal("38,500", box.Text);

        box.Focus();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        box.Text = "40000";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("40000", box.Text); // nothing rewritten while focused.

        other.Focus();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("40,000", box.Text);
        Assert.Equal("40,000", viewModel.ProjectedAttendanceText);
        Assert.Equal(AssociateAutosaveState.Unsaved, viewModel.AutosaveState); // the typed edit, not a new one.

        window.Close();
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task PriceHistory_ProductList_PaintsThePopupSurface_InBothThemes(string theme)
    {
        var history = new PriceHistoryViewModel(new FakeSeasonPriceHistoryService()
            .With("gold", "2027 Synthetic Gold Pass", active: true, PriceHistoryFamily.SeasonPass,
                (99m, PriceKind.StandardCatalog, new DateTimeOffset(2026, 8, 12, 16, 0, 0, TimeSpan.Zero)))
            .With("silver", "2027 Synthetic Silver Pass", active: true, PriceHistoryFamily.SeasonPass,
                (79m, PriceKind.StandardCatalog, new DateTimeOffset(2026, 8, 12, 16, 0, 0, TimeSpan.Zero))));
        await history.OpenAsync("gold");
        var variant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

        var window = RealizeInWindow(new PriceHistoryView { DataContext = history }, 1280, 860);
        window.RequestedThemeVariant = variant;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "ProductList");
        Assert.True(window.TryFindResource("PassdownPopupSurfaceBrush", variant, out var surface));
        Assert.Same(surface, list.Background);
        Assert.Equal(2, list.GetVisualDescendants().OfType<ListBoxItem>().Count());
        AssertNoControlClipsHorizontally(window);

        window.Close();
    }

    // ==================================================================================
    // Phase 26: the official source link — keyboard activation, both themes, narrow and wide.
    // ==================================================================================

    private sealed class RecordingLinkLauncher : Carowinds.Passdown.Desktop.Services.IExternalLinkLauncher
    {
        public List<Uri> Opened { get; } = [];

        public Task<bool> OpenAsync(Uri officialPage, CancellationToken cancellationToken = default)
        {
            Opened.Add(officialPage);
            return Task.FromResult(true);
        }
    }

    [Theory]
    [InlineData("Light", 1280)]
    [InlineData("Dark", 1280)]
    [InlineData("Light", 760)]
    [InlineData("Dark", 760)]
    public async Task PriceHistory_OfficialSourceLink_IsATabStop_EnterOpensIt_InBothThemesAndWidths(string theme, int width)
    {
        var page = new Uri("https://www.sixflags.com/carowinds/memberships-and-season-passes");
        var service = new FakeSeasonPriceHistoryService()
            .With("gold", "2027 Synthetic Gold Pass", active: true, PriceHistoryFamily.SeasonPass,
                (99m, PriceKind.StandardCatalog, new DateTimeOffset(2026, 8, 12, 16, 0, 0, TimeSpan.Zero)));
        service.Products[0] = service.Products[0] with { OfficialSourceUrl = page };
        var launcher = new RecordingLinkLauncher();
        var history = new PriceHistoryViewModel(service, linkLauncher: launcher);
        await history.OpenAsync("gold");
        var variant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

        var window = RealizeInWindow(new PriceHistoryView { DataContext = history }, width, 860);
        window.RequestedThemeVariant = variant;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var link = window.GetVisualDescendants().OfType<Button>()
            .Single(b => AutomationProperties.GetName(b) == "View Official Memberships & Season Passes Page");
        Assert.True(link.Focusable && link.IsTabStop && link.IsEffectivelyEnabled && link.IsEffectivelyVisible);
        Assert.True(link.Bounds.Width > 0);
        Assert.True(window.TryFindResource("PassdownAccentForegroundBrush", variant, out var accent));
        Assert.Same(accent, link.Foreground);
        AssertNoControlClipsHorizontally(window);

        Assert.True(link.Focus(NavigationMethod.Tab));
        Assert.True(link.IsFocused);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal([page], launcher.Opened);
        Assert.True(link.IsFocused, "opening the page must not drop keyboard focus");

        window.Close();
    }

    // ==================================================================================
    // Phase 22E: compact Calendar Shows, Notes rows and History rows in a real window.
    // ==================================================================================

    private static readonly DateOnly Phase22EBusyDate = new(2026, 8, 22);

    private static (Window Window, OperationsCalendarViewModel ViewModel, FakeOperationsCalendarService Service) RealizeBusyCalendar(double width, double height, ThemeVariant theme)
    {
        static TimeOnly T(int h, int m = 0) => new(h, m);
        Carowinds.Passdown.Application.Retrieval.ScheduledShow[] shows =
        [
            new("r1", "Synthetic Resurgence", "Plaza Stage", [T(18, 30)]),
            new("r2", "Synthetic Wake the Night", "Carowinds Theatre", [T(19), T(20), T(21, 30), T(22, 30)]),
            new("r3", "Synthetic Monster Stomp Revue Featuring the Entire Midway Ensemble", "Midway Stage", [T(12), T(14), T(16), T(18), T(20)]),
            new("r4", "Synthetic Drumline", null, [T(13, 15), T(15, 15), T(17, 15)]),
            new("r5", "Synthetic Street Creatures", "Harmony Hall", [T(19, 30), T(20, 30), T(21, 30), T(22, 30), T(23, 30)]),
            new("r6", "Synthetic Late Night Howl", "County Fair Stage", [T(21), T(22, 15), T(23, 30), T(0, 30)]),
        ];
        var service = new FakeOperationsCalendarService
        {
            SnapshotFactory = (month, selected) =>
            {
                var snapshot = FakeOperationsCalendarService.DefaultSnapshot(month, selected);
                var view = selected == Phase22EBusyDate
                    ? new Carowinds.Passdown.Application.Shows.ShowScheduleView(selected, Carowinds.Passdown.Application.Shows.ShowScheduleState.Scheduled, shows, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
                    : Carowinds.Passdown.Application.Shows.ShowScheduleView.NotCheckedFor(selected);
                return snapshot with { Selected = snapshot.Selected with { Shows = view } };
            },
        };
        var viewModel = new OperationsCalendarViewModel(service, CreateClock());
        viewModel.PendingLoad.GetAwaiter().GetResult();
        viewModel.SelectDateCommand.Execute(Phase22EBusyDate);
        viewModel.PendingLoad.GetAwaiter().GetResult();

        var window = RealizeInWindow(new OperationsCalendarView { DataContext = viewModel }, width, height);
        window.RequestedThemeVariant = theme;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, viewModel, service);
    }

    /// <summary>
    /// The number of distinct text lines-of-blocks in a list row (visible TextBlocks grouped by their top
    /// edge) and the height those blocks need — headless fonts use fake metrics, so row density is asserted
    /// structurally (how many blocks, and no padding beyond the style's) rather than in absolute pixels.
    /// </summary>
    private static (int Blocks, double ContentHeight) Phase22ERowContent(ListBoxItem item, Window window)
    {
        var lines = new List<(double Top, double Height)>();
        foreach (var text in item.GetVisualDescendants().OfType<TextBlock>()
                     .Where(t => t.IsEffectivelyVisible && t.Bounds.Height > 0)
                     .OrderBy(t => AbsoluteBounds(t, window).Top))
        {
            var top = AbsoluteBounds(text, window).Top;
            if (lines.Count > 0 && top - lines[^1].Top <= 4)
            {
                lines[^1] = (lines[^1].Top, Math.Max(lines[^1].Height, text.Bounds.Height)); // same line (e.g. a tag beside the title)
            }
            else
            {
                lines.Add((top, text.Bounds.Height));
            }
        }

        return (lines.Count, lines.Sum(l => l.Height));
    }

    private static (double Top, double Bottom) Phase22EShowsSection(Window window)
    {
        var section = window.GetVisualDescendants().OfType<StackPanel>().Single(p => AutomationProperties.GetName(p) == "Shows");
        var bounds = AbsoluteBounds(section, window);
        return (bounds.Top, bounds.Bottom);
    }

    [Fact]
    public void CalendarView_ShowSchedule_TogglesWithTheKeyboard_ReportsExpandCollapse_AndNeverLoads()
    {
        var (window, viewModel, service) = RealizeBusyCalendar(1280, 860, ThemeVariant.Light);
        var loads = service.LoadCalls.Count;
        var expander = window.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "ShowScheduleExpander");
        var header = expander.GetVisualDescendants().OfType<ToggleButton>().Single(t => t.Name == "DisclosureHeader");
        var peer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(expander);
        var expandCollapse = Assert.IsAssignableFrom<Avalonia.Automation.Provider.IExpandCollapseProvider>(peer);
        ItemsControl? Rows() => window.GetVisualDescendants().OfType<ItemsControl>().SingleOrDefault(i => AutomationProperties.GetName(i) == "Shows for the selected date");

        Assert.False(viewModel.IsShowScheduleExpanded);
        Assert.Equal(ExpandCollapseState.Collapsed, expandCollapse.ExpandCollapseState);
        Assert.True(Rows() is null || !Rows()!.IsEffectivelyVisible, "collapsed: no schedule rows are presented");
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Synthetic Wake the Night" && t.IsEffectivelyVisible);
        Assert.Equal("Show schedule", AutomationProperties.GetName(header));
        Assert.True(header.Focusable && header.IsTabStop);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "6 scheduled shows · 22 performances" && t.IsEffectivelyVisible);

        Assert.True(header.Focus(NavigationMethod.Tab));
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.IsShowScheduleExpanded);
        Assert.Equal(ExpandCollapseState.Expanded, expandCollapse.ExpandCollapseState);
        var rows = Assert.IsAssignableFrom<ItemsControl>(Rows());
        Assert.True(rows.IsEffectivelyVisible);
        Assert.Equal(6, rows.GetRealizedContainers().Count());
        Assert.Contains(rows.GetVisualDescendants().OfType<Border>(),
            b => AutomationProperties.GetName(b) == "Synthetic Wake the Night, 4 performances: 7:00, 8:00, 9:30, 10:30 PM, Carowinds Theatre");

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(viewModel.IsShowScheduleExpanded);
        Assert.True(header.IsFocused, "toggling must keep keyboard focus on the disclosure");

        Assert.Equal(loads, service.LoadCalls.Count);
        window.Close();
    }

    [Theory]
    [InlineData(960d, 640d, "Light")]
    [InlineData(960d, 640d, "Dark")]
    [InlineData(1280d, 690d, "Light")]
    [InlineData(1280d, 860d, "Dark")]
    [InlineData(1920d, 1000d, "Light")]
    public void CalendarView_Shows_CollapsedIsCompact_ExpandedWrapsInsideThePanel(double width, double height, string theme)
    {
        var (window, viewModel, _) = RealizeBusyCalendar(width, height, theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);
        var panel = window.GetVisualDescendants().OfType<Border>().Single(b => AutomationProperties.GetName(b) == "Selected date summary");
        var panelBounds = AbsoluteBounds(panel, window);

        var collapsed = Phase22EShowsSection(window);
        AssertNoControlClipsHorizontally(window);

        viewModel.IsShowScheduleExpanded = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var expanded = Phase22EShowsSection(window);
        AssertNoControlClipsHorizontally(window);

        // Collapsed: heading + summary + toggle + freshness — a few compact rows, far smaller than the full list.
        Assert.True(collapsed.Bottom - collapsed.Top < 110, $"collapsed Shows is {collapsed.Bottom - collapsed.Top:F0}px");
        Assert.True(expanded.Bottom - expanded.Top > 3 * (collapsed.Bottom - collapsed.Top), "expanding reveals the full schedule");

        // Every show line (long names, many times) wraps inside the panel — nothing pushed past its edge.
        var section = window.GetVisualDescendants().OfType<StackPanel>().Single(p => AutomationProperties.GetName(p) == "Shows");
        foreach (var text in section.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Bounds.Width > 0))
        {
            var bounds = AbsoluteBounds(text, window);
            Assert.True(bounds.Right <= panelBounds.Right + 1.0, $"'{text.Text}' right edge {bounds.Right} exceeds the panel's {panelBounds.Right}");
        }

        window.Close();
    }

    [Theory]
    [InlineData(960d, 640d, "Light")]
    [InlineData(1280d, 860d, "Dark")]
    public async Task OperationalNotesView_Rows_AreCompact_AndStayWithinTheList(double width, double height, string theme)
    {
        var service = new FakeOperationalNotesService();
        var today = FixedDate;
        for (var i = 0; i < 8; i++)
        {
            service.Seed(new Carowinds.Passdown.Application.OperationalNotes.OperationalNote(
                Guid.NewGuid(), i == 3 ? "Synthetic: a long operational note title that has to wrap onto a second line in the list" : $"Synthetic note {i}", null,
                Carowinds.Passdown.Application.OperationalNotes.OperationalNoteCategory.General,
                i % 3 == 0 ? Carowinds.Passdown.Application.OperationalNotes.OperationalNotePriority.Important : Carowinds.Passdown.Application.OperationalNotes.OperationalNotePriority.Normal,
                today.AddDays(-1), today.AddDays(i), Carowinds.Passdown.Application.OperationalNotes.OperationalNoteStatus.Active,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null));
        }

        var viewModel = new OperationalNotesViewModel(service, CreateClock());
        await viewModel.PendingLoad;
        var window = RealizeInWindow(new OperationalNotesView { DataContext = viewModel }, width, height);
        window.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        AssertNoControlClipsHorizontally(window);
        var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "Operational notes list");
        var items = list.GetVisualDescendants().OfType<ListBoxItem>().ToList();
        Assert.NotEmpty(items);
        Assert.All(items, item =>
        {
            // Title, "Category · Priority", "Lifecycle · range": three blocks (was five plus a chip), 16px padding + spacing.
            var (blocks, content) = Phase22ERowContent(item, window);
            Assert.Equal(3, blocks);
            Assert.True(item.Bounds.Height <= content + 22, $"note row {item.Bounds.Height:F0}px for {content:F0}px of text");
        });
        var listRight = AbsoluteBounds(list, window).Right;
        Assert.All(list.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Bounds.Width > 0),
            t => Assert.True(AbsoluteBounds(t, window).Right <= listRight + 1.0, $"'{t.Text}' overflows the list"));

        window.Close();
    }

    [Theory]
    [InlineData(960d, 640d, "Dark")]
    [InlineData(1280d, 860d, "Light")]
    public async Task HistoryView_Rows_AreTwoLines_AndThePdfActionsStayReachable(double width, double height, string theme)
    {
        var service = new FakePassdownHistoryService();
        for (var i = 1; i <= 9; i++)
        {
            var date = FixedDate.AddDays(-i);
            var summary = new FinalizedPassdownSummary(date, new DateTimeOffset(date.AddDays(1).ToDateTime(new TimeOnly(10, 2)), TimeSpan.Zero), FinalizationOrigin.Scheduled, i != 3, i != 3 ? 1000 : null);
            service.Summaries.Add(summary);
            service.Details[date] = new Application.History.PassdownHistoryDetail(summary, null, null);
        }

        var viewModel = new PassdownHistoryViewModel(service, new FakeFinalizedPassdownPdfRetrievalService());
        await viewModel.PendingLoad;
        viewModel.SelectedItem = viewModel.HistoryItems[0];
        await viewModel.PendingDetailLoad;
        var window = RealizeInWindow(new HistoryView { DataContext = viewModel }, width, height);
        window.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        AssertNoControlClipsHorizontally(window);
        var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "Finalized passdown history");
        var items = list.GetVisualDescendants().OfType<ListBoxItem>().ToList();
        Assert.NotEmpty(items);
        Assert.All(items, item =>
        {
            // Date (+ PDF tag on the same line) and "Finalized … · origin": two lines, no chip row.
            var (blocks, content) = Phase22ERowContent(item, window);
            Assert.Equal(2, blocks);
            Assert.True(item.Bounds.Height <= content + 20, $"history row {item.Bounds.Height:F0}px for {content:F0}px of text");
        });
        Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetName(b) == "Open Final PDF" && b.IsEffectivelyVisible);

        window.Close();
    }

    // ==================================================================================
    // Phase 22F — Settings layout, current-section status, Calendar date-change panel
    // ==================================================================================

    private static PersonalSyncSettingsViewModel CreateBusySettings()
    {
        var settingsService = new FakePersonalSyncSettingsService
        {
            PersonalSyncEnabled = true,
            SyncRootPath = @"C:\Users\associate\OneDrive - Personal\Carowinds Operations Sync\A Deliberately Long Folder Name That Must Wrap Inside The Panel",
            Status = Application.PersonalSync.PersonalSyncStatus.FolderUnavailable,
            StatusDetail = "The sync folder can't be reached. Work is saved on this computer and will sync once the folder is back.",
        };
        var coordinator = new FakePersonalSyncCoordinator
        {
            CurrentStatus = Application.PersonalSync.PersonalSyncAutomationSnapshot.Initial with
            {
                State = Application.PersonalSync.PersonalSyncAutomationState.Error,
                LastErrorMessage = "Couldn't write to the sync folder (access denied).",
                LastSuccessfulSyncUtc = new DateTimeOffset(2026, 8, 20, 14, 0, 0, TimeSpan.Zero),
            },
        };
        return new PersonalSyncSettingsViewModel(settingsService, new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), coordinator);
    }

    [Theory]
    [InlineData(960d, 640d, "Light")]
    [InlineData(960d, 640d, "Dark")]
    [InlineData(1280d, 690d, "Dark")]
    [InlineData(1280d, 860d, "Light")]
    [InlineData(1920d, 1000d, "Dark")]
    public async Task SettingsView_EveryGroupAndControl_FitsTheWindow_WithLongPathsWrapped(double width, double height, string theme)
    {
        var viewModel = CreateBusySettings();
        await viewModel.PendingLoad;
        var window = RealizeInWindow(new PersonalSyncSettingsView { DataContext = viewModel }, width, height);
        window.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        AssertNoControlClipsHorizontally(window);
        foreach (var group in new[] { "Appearance", "Data refresh", "Personal Sync", "Diagnostics", "About this build" })
        {
            Assert.Contains(window.GetVisualDescendants().OfType<Border>(), b => AutomationProperties.GetName(b) == group);
        }

        foreach (var name in new[] { "Choose Personal Sync Folder", "Sync Now", "Export Diagnostics" })
        {
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetName(b) == name);
        }

        var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "SettingsScroll");
        Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1, $"horizontal overflow {scroll.Extent.Width - scroll.Viewport.Width:F0}px");

        var path = window.GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => t.Text == viewModel.SyncRootPath);
        Assert.True(AbsoluteBounds(path, window).Right <= window.ClientSize.Width + 1);
        if (width <= 960)
        {
            Assert.True(path.Bounds.Height > path.FontSize * 1.8, "a long sync-folder path wraps rather than running off the panel");
        }

        window.Close();
    }

    [Fact]
    public async Task SettingsView_TabOrder_RunsThroughTheEditableControlsTopToBottom()
    {
        var viewModel = new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator());
        await viewModel.PendingLoad;
        var window = RealizeInWindow(new PersonalSyncSettingsView { DataContext = viewModel }, 1280, 860);

        var checkBox = window.GetVisualDescendants().OfType<CheckBox>().Single(c => AutomationProperties.GetName(c) == "Enable Personal Sync");
        checkBox.Focus();
        var seen = new List<string?> { AutomationProperties.GetName(checkBox) };
        for (var i = 0; i < 8; i++)
        {
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            if (window.FocusManager!.GetFocusedElement() is Button button)
            {
                seen.Add(AutomationProperties.GetName(button));
            }
        }

        var choose = seen.IndexOf("Choose Personal Sync Folder");
        var export = seen.IndexOf("Export Diagnostics");
        Assert.True(choose > 0, string.Join(" → ", seen));
        Assert.True(export > choose, string.Join(" → ", seen));
        Assert.False(viewModel.PersonalSyncEnabled); // Tab alone never toggles anything

        window.Close();
    }

    [Fact]
    public void MainWindow_AndAssociateRail_ExposeTheCurrentSectionAsItemStatus()
    {
        var viewModel = CreateMainViewModel();
        var window = ShowMainWindow(viewModel, 1280, 860);

        string? Status(string name) => AutomationProperties.GetItemStatus(
            window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == name));

        Assert.Equal("Current page", Status("Dashboard section"));
        Assert.Null(Status("Settings"));

        viewModel.SelectedSection = AppSection.Settings;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("Current page", Status("Settings"));
        Assert.Null(Status("Dashboard section"));

        viewModel.SelectedSection = AppSection.AssociatePassdown;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var rail = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Overview section");
        var products = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Products section");
        Assert.Equal("Current section", AutomationProperties.GetItemStatus(rail));
        Assert.Null(AutomationProperties.GetItemStatus(products));

        window.Close();
    }

    [Theory]
    [InlineData(960d, 640d, "Light")]
    [InlineData(1280d, 860d, "Dark")]
    public async Task CalendarView_WhileANewDateLoads_ShowsItsHeadingAndNoPreviousDetails(double width, double height, string theme)
    {
        var service = new FakeOperationsCalendarService();
        var viewModel = new OperationsCalendarViewModel(service, CreateClock());
        await viewModel.PendingLoad;
        var window = RealizeInWindow(new OperationsCalendarView { DataContext = viewModel }, width, height);
        window.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var details = window.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "SelectedDateDetails");
        var placeholder = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "SelectedDetailPlaceholder");
        var heading = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "SelectedDateHeading");
        Assert.True(details.IsEffectivelyVisible);
        Assert.False(placeholder.IsEffectivelyVisible);

        service.Gate = new TaskCompletionSource<bool>();
        viewModel.NextDayCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("Saturday, August 22, 2026", heading.Text);
        Assert.False(details.IsEffectivelyVisible);
        Assert.True(placeholder.IsEffectivelyVisible);
        Assert.Equal("Loading details…", placeholder.Text);
        AssertNoControlClipsHorizontally(window);

        service.Gate.SetResult(true);
        await viewModel.PendingLoad;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(details.IsEffectivelyVisible);
        Assert.False(placeholder.IsEffectivelyVisible);

        window.Close();
    }

    // ==================================================================================
    // Phase 23B — Dashboard Today's Shows (stacked entries, "+N more" disclosure) and the
    // title-only Fury banner: real rendered bounds, mouse and keyboard activation.
    // ==================================================================================

    private static (Window Window, OperationsDashboardViewModel ViewModel, FakeOperationsDashboardService Service) RealizeShowsDashboard(double width, double height, ThemeVariant theme)
    {
        var service = new FakeOperationsDashboardService { SnapshotFactory = Phase22CDashboardFixtures.BusyDayWithPartialFailure };
        Phase23BDashboardShowsFixtures.Apply(service, Phase23BDashboardShowsFixtures.Scheduled(Phase23BDashboardShowsFixtures.Busy));
        var viewModel = new OperationsDashboardViewModel(service);
        var window = RealizeInWindow(new DashboardView { DataContext = viewModel }, width, height);
        viewModel.PendingLoad.GetAwaiter().GetResult();
        window.RequestedThemeVariant = theme;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, viewModel, service);
    }

    private static Border ShowsPanel(Window window) =>
        window.GetVisualDescendants().OfType<Border>().Single(b => AutomationProperties.GetName(b) == "Today's shows");

    private static List<Border> ShowCells(Window window) =>
        ShowsPanel(window).GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("dashboardShowRow") && b.IsEffectivelyVisible).ToList();

    private static ToggleButton TimesToggle(Window window, string showName) =>
        ShowsPanel(window).GetVisualDescendants().OfType<ToggleButton>()
            .Single(t => (AutomationProperties.GetName(t) ?? string.Empty).EndsWith($"performance times for {showName}", StringComparison.Ordinal));

    /// <summary>
    /// Every visible text block and toggle in Today's Shows: inside its own entry, inside the panel, and
    /// intersecting nothing else — the reported defect was the times line running under the venue column.
    /// </summary>
    private static void AssertShowsDoNotOverlap(Window window)
    {
        var panel = AbsoluteBounds(ShowsPanel(window), window);
        foreach (var cell in ShowCells(window))
        {
            var cellBounds = AbsoluteBounds(cell, window);
            Assert.True(cellBounds.Right <= panel.Right + 1.0, $"show entry right edge {cellBounds.Right} exceeds the panel's {panel.Right}");
            var parts = cell.GetVisualDescendants().OfType<Control>()
                .Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0 && (c is TextBlock || c is ToggleButton))
                .Where(c => c is not TextBlock || !c.GetVisualAncestors().OfType<ToggleButton>().Any())
                .Select(c => (Control: c, Bounds: AbsoluteBounds(c, window)))
                .ToList();
            foreach (var part in parts)
            {
                Assert.True(part.Bounds.Right <= cellBounds.Right + 1.0, $"'{(part.Control as TextBlock)?.Text}' right edge {part.Bounds.Right} exceeds its entry's {cellBounds.Right}");
                Assert.True(part.Bounds.Bottom <= cellBounds.Bottom + 1.0, $"'{(part.Control as TextBlock)?.Text}' runs below its entry");
            }

            for (var i = 0; i < parts.Count; i++)
            {
                for (var j = i + 1; j < parts.Count; j++)
                {
                    var overlap = parts[i].Bounds.Intersect(parts[j].Bounds);
                    Assert.False(overlap.Width > 1 && overlap.Height > 1,
                        $"'{(parts[i].Control as TextBlock)?.Text ?? AutomationProperties.GetName(parts[i].Control)}' overlaps '{(parts[j].Control as TextBlock)?.Text ?? AutomationProperties.GetName(parts[j].Control)}'");
                }
            }
        }

        var cells = ShowCells(window).Select(c => AbsoluteBounds(c, window)).ToList();
        for (var i = 0; i < cells.Count; i++)
        {
            for (var j = i + 1; j < cells.Count; j++)
            {
                var overlap = cells[i].Intersect(cells[j]);
                Assert.False(overlap.Width > 1 && overlap.Height > 1, $"show entries {i} and {j} overlap");
            }
        }
    }

    [Theory]
    [InlineData(960d, 640d, "Light")]
    [InlineData(960d, 640d, "Dark")]
    [InlineData(1280d, 690d, "Light")]
    [InlineData(1280d, 860d, "Dark")]
    [InlineData(1920d, 1000d, "Light")]
    [InlineData(1920d, 1000d, "Dark")]
    public void DashboardView_Shows_NeverOverlap_CollapsedOrFullyExpanded(double width, double height, string theme)
    {
        var (window, viewModel, _) = RealizeShowsDashboard(width, height, theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);

        Assert.Equal(viewModel.ShowRows.Count, ShowCells(window).Count);
        AssertShowsDoNotOverlap(window);
        AssertNoControlClipsHorizontally(window);
        var collapsedHeight = ShowsPanel(window).Bounds.Height;

        foreach (var row in viewModel.ShowRows.Where(r => r.CanExpand))
        {
            row.IsExpanded = true;
        }

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AssertShowsDoNotOverlap(window);
        AssertNoControlClipsHorizontally(window);
        Assert.True(ShowsPanel(window).Bounds.Height >= collapsedHeight, "expanding never shrinks the panel");

        var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.IsEffectivelyVisible);
        Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1.0, "no horizontal scrolling");
        window.Close();
    }

    [Theory]
    [InlineData(960d, 640d)]
    [InlineData(1920d, 1000d)]
    public void DashboardView_ShowEntry_ReadsNameThenTimesThenVenue_TopToBottom(double width, double height)
    {
        var (window, _, _) = RealizeShowsDashboard(width, height, ThemeVariant.Light);

        foreach (var cell in ShowCells(window))
        {
            var texts = cell.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !t.GetVisualAncestors().OfType<ToggleButton>().Any()).ToList();
            var name = AbsoluteBounds(texts.Single(t => t.Classes.Contains("showName")), window);
            var times = AbsoluteBounds(texts.Single(t => t.Classes.Contains("showTimes")), window);
            Assert.True(times.Top >= name.Bottom - 0.5, "times start under the name");
            if (texts.SingleOrDefault(t => t.Classes.Contains("showVenue")) is { } venue)
            {
                Assert.True(AbsoluteBounds(venue, window).Top >= times.Bottom - 0.5, "venue starts under the times — never beside them");
            }
        }

        window.Close();
    }

    [Theory]
    [InlineData(960d, 640d, 2)]
    [InlineData(1280d, 860d, 3)]
    [InlineData(1920d, 1000d, 3)]
    public void DashboardView_Shows_FillColumnsRowByRow_InScheduleOrder(double width, double height, int columns)
    {
        var (window, viewModel, _) = RealizeShowsDashboard(width, height, ThemeVariant.Light);
        var cells = ShowCells(window).Select(c => AbsoluteBounds(c, window)).ToList();

        Assert.Equal(columns, cells.Select(c => Math.Round(c.Left)).Distinct().Count());
        for (var i = 1; i < cells.Count; i++)
        {
            var sameLine = i % columns != 0;
            Assert.True(sameLine ? Math.Abs(cells[i].Top - cells[i - 1].Top) < 0.5 && cells[i].Left > cells[i - 1].Right : cells[i].Top >= cells[i - 1].Bottom - 0.5,
                $"entry {i} ({viewModel.ShowRows[i].Name}) is out of row-by-row order");
        }

        window.Close();
    }

    [Fact]
    public void DashboardView_ExpandedTimes_WrapInsideTheirColumn()
    {
        var (window, _, _) = RealizeShowsDashboard(960, 640, ThemeVariant.Light);
        var toggle = TimesToggle(window, "Synthetic Camp Meet and Greet");
        var cell = toggle.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("dashboardShowRow"));
        var times = cell.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("showTimes"));
        var collapsedLines = times.TextLayout.TextLines.Count;

        toggle.IsChecked = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(times.TextLayout.TextLines.Count > collapsedLines, "fourteen times need more than one line at this width");
        Assert.True(AbsoluteBounds(times, window).Right <= AbsoluteBounds(cell, window).Right + 1.0);
        Assert.Equal(14, times.Text!.Split(" · ").Length);
        AssertShowsDoNotOverlap(window);
        window.Close();
    }

    [Fact]
    public void DashboardView_ShowsToggle_WorksWithTheMouse_ReportsItsState_AndNeverLoads()
    {
        var (window, viewModel, service) = RealizeShowsDashboard(1280, 860, ThemeVariant.Light);
        var loads = service.LoadCalls.Count;
        var toggle = TimesToggle(window, "Synthetic Camp Meet and Greet");
        var peer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(toggle);
        var state = Assert.IsAssignableFrom<Avalonia.Automation.Provider.IToggleProvider>(peer);
        Assert.Equal(Avalonia.Automation.Provider.ToggleState.Off, state.ToggleState);
        Assert.Equal("Show all 14 performance times for Synthetic Camp Meet and Greet", AutomationProperties.GetName(toggle));

        var center = AbsoluteBounds(toggle, window).Center;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var row = viewModel.ShowRows.Single(r => r.Key == "syn-10");
        Assert.True(row.IsExpanded);
        Assert.Equal(Avalonia.Automation.Provider.ToggleState.On, state.ToggleState);
        Assert.Equal("Show fewer performance times for Synthetic Camp Meet and Greet", AutomationProperties.GetName(toggle));
        Assert.Equal(loads, service.LoadCalls.Count);
        Assert.Equal(0, service.CheckOnlineCallCount);
        window.Close();
    }

    [Fact]
    public void DashboardView_ShowsToggle_WorksWithEnterAndSpace_AndKeepsFocus()
    {
        var (window, viewModel, service) = RealizeShowsDashboard(1280, 860, ThemeVariant.Dark);
        var loads = service.LoadCalls.Count;
        var toggle = TimesToggle(window, "Synthetic Character Dance Party");
        Assert.True(toggle.Focusable && toggle.IsTabStop);

        Assert.True(toggle.Focus(NavigationMethod.Tab));
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.ShowRows.Single(r => r.Key == "syn-12").IsExpanded);
        Assert.True(toggle.IsFocused, "toggling must keep keyboard focus on the control");

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(viewModel.ShowRows.Single(r => r.Key == "syn-12").IsExpanded);
        Assert.True(toggle.IsFocused);
        Assert.Equal(loads, service.LoadCalls.Count);
        window.Close();
    }

    [Fact]
    public void DashboardView_ShortTimeLists_HaveNoToggle()
    {
        var (window, viewModel, _) = RealizeShowsDashboard(1280, 860, ThemeVariant.Light);

        var toggles = ShowsPanel(window).GetVisualDescendants().OfType<ToggleButton>().Where(t => t.IsEffectivelyVisible).ToList();
        Assert.Equal(viewModel.ShowRows.Count(r => r.CanExpand), toggles.Count);
        Assert.Equal(4, toggles.Count);
        window.Close();
    }

    [Theory]
    [InlineData(960d, 640d)]
    [InlineData(1280d, 690d)]
    [InlineData(1920d, 1000d)]
    public void MainWindow_Banner_IsTitleOnly_AlignedToTheContentMargin_OnOneLine(double width, double height)
    {
        var window = ShowMainWindow(CreateMainViewModel(), width, height);
        var banner = Named(window, "BrandBanner");
        var wordmark = (TextBlock)Named(window, "BrandWordmark");
        var bounds = AbsoluteBounds(wordmark, window);

        Assert.Equal(24, bounds.Left, 1);
        Assert.Single(wordmark.TextLayout.TextLines);
        Assert.Empty(banner.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Shape>());
        Assert.Empty(banner.GetVisualDescendants().OfType<PathIcon>());
        Assert.Equal(72, banner.Bounds.Height, 1);
        Assert.True(bounds.Top >= 0 && bounds.Bottom <= banner.Bounds.Height);
        Assert.Equal(banner.Bounds.Height / 2, (bounds.Top + bounds.Bottom) / 2, 2); // vertically centered
        Assert.NotNull(window.Icon); // the window/taskbar icon is a separate asset, unchanged
        window.Close();
    }

    // ==================================================================================
    // Phase 23C — Appearance preference (System / Light / Dark): live switching through the real
    // controller and ApplicationThemeVariantTarget on real windows. These set the application-wide
    // theme, so they live in this run-alone class and always restore it.
    // ==================================================================================

    private sealed class AppearanceHarness : IDisposable
    {
        private readonly ThemeVariant? _original = Avalonia.Application.Current!.RequestedThemeVariant;

        public FakeAppearancePreferenceStore Store { get; }
        public FakeSystemAppearanceSource Os { get; }
        public Desktop.Services.AppearanceThemeController Controller { get; }

        public AppearanceHarness(Application.Preferences.AppearancePreference stored = Application.Preferences.AppearancePreference.System,
            Application.Preferences.EffectiveAppearance os = Application.Preferences.EffectiveAppearance.Light)
        {
            Store = new FakeAppearancePreferenceStore { Stored = stored };
            Os = new FakeSystemAppearanceSource(os);
            Controller = new Desktop.Services.AppearanceThemeController(Store, Os, new Desktop.Services.ApplicationThemeVariantTarget());
            Controller.Initialize();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            Controller.Dispose();
            Avalonia.Application.Current!.RequestedThemeVariant = _original;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }

    private static MainViewModel CreateAppearanceMainViewModel(Desktop.Services.AppearanceThemeController controller)
    {
        var dashboard = new FakeOperationsDashboardService();
        Phase23BDashboardShowsFixtures.Apply(dashboard, Phase23BDashboardShowsFixtures.Scheduled(Phase23BDashboardShowsFixtures.Busy));
        var calendar = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), CreateClock());
        var associate = new AssociateViewModel(new FakeAssociatePassdownService(), CreateClock());
        var notes = new OperationalNotesViewModel(new FakeOperationalNotesService(), new SystemClock());
        var historyService = new FakePassdownHistoryService
        {
            Summaries = [new FinalizedPassdownSummary(FixedDate, DateTimeOffset.UtcNow, FinalizationOrigin.Scheduled, true, 100)],
        };
        var history = new PassdownHistoryViewModel(historyService, new FakeFinalizedPassdownPdfRetrievalService());
        var settings = new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator(),
            appearance: new AppearanceSettingsViewModel(controller));
        var prices = new FakeSeasonPriceHistoryService().With("gold-pass", "Gold Pass", true, PriceHistoryFamily.SeasonPass,
            (125m, PriceKind.StandardCatalog, new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero)),
            (110m, PriceKind.StandardCatalog, new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero)));
        return new MainViewModel(new OperationsDashboardViewModel(dashboard), calendar, associate, notes, history, settings, new PriceHistoryViewModel(prices));
    }

    private static readonly Lazy<string[]> ThemeTokenKeys = new(() =>
    {
        var xaml = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Themes/PassdownTokens.axaml"));
        var start = xaml.IndexOf("<ResourceDictionary x:Key=\"Light\">", StringComparison.Ordinal);
        var end = xaml.IndexOf("</ResourceDictionary>", start, StringComparison.Ordinal);
        return System.Text.RegularExpressions.Regex.Matches(xaml[start..end], "x:Key=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();
    });

    /// <summary>
    /// Every visible brush in <paramref name="roots"/> that belongs only to the theme NOT shown — a surface,
    /// text or border that kept the previous palette after a switch. Brushes are compared by instance
    /// against the Phase 22A theme dictionaries, so no color value is read or hard-coded.
    /// </summary>
    private static List<string> WrongThemeBrushes(ThemeVariant shown, params Visual[] roots)
    {
        var app = Avalonia.Application.Current!;
        var other = shown == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
        var otherOnly = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
        foreach (var key in ThemeTokenKeys.Value)
        {
            if (app.TryGetResource(key, other, out var o) && app.TryGetResource(key, shown, out var s) && o is IBrush && !ReferenceEquals(o, s))
            {
                otherOnly[o] = key;
            }
        }

        var found = new List<string>();
        foreach (var visual in roots.SelectMany(r => r.GetVisualDescendants().Prepend(r)).Distinct())
        {
            if (!visual.IsEffectivelyVisible)
            {
                continue;
            }

            IEnumerable<IBrush?> brushes = visual switch
            {
                Border b => [b.Background, b.BorderBrush],
                TemplatedControl t => [t.Background, t.Foreground, t.BorderBrush],
                TextBlock t => [t.Foreground, t.Background],
                Panel p => [p.Background],
                Avalonia.Controls.Shapes.Shape sh => [sh.Fill, sh.Stroke],
                _ => [],
            };
            foreach (var brush in brushes)
            {
                if (brush is not null && otherOnly.TryGetValue(brush, out var key))
                {
                    found.Add($"{visual.GetType().Name}{(visual is Control { Name: { } n } ? "#" + n : string.Empty)} → {key}");
                }
            }
        }

        return found;
    }

    private static IBrush Token(string key, ThemeVariant variant)
    {
        Assert.True(Avalonia.Application.Current!.TryGetResource(key, variant, out var value));
        return Assert.IsAssignableFrom<IBrush>(value);
    }

    private static RadioButton AppearanceOption(Window window, string name) =>
        window.GetVisualDescendants().OfType<RadioButton>().Single(r => AutomationProperties.GetName(r) == name);

    private static int CheckedAppearanceOptions(Window window) =>
        window.GetVisualDescendants().OfType<RadioButton>().Count(r => r.GroupName == "Appearance" && r.IsChecked == true);

    [Fact]
    public void Appearance_PickingAnOptionInSettings_RestylesTheOpenWindow_WithoutRestarting()
    {
        using var harness = new AppearanceHarness(os: Application.Preferences.EffectiveAppearance.Light);
        var viewModel = CreateAppearanceMainViewModel(harness.Controller);
        var window = ShowMainWindow(viewModel, 1280, 860);
        viewModel.SelectedSection = AppSection.Settings;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var panel = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "AppearanceSection");

        Assert.Equal(ThemeVariant.Light, window.ActualThemeVariant);
        Assert.True(AppearanceOption(window, "Use Windows setting").IsChecked);
        Assert.Same(Token("PassdownSurfaceBrush", ThemeVariant.Light), panel.Background);

        AppearanceOption(window, "Dark").Focus();
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(Application.Preferences.AppearancePreference.Dark, harness.Controller.Preference);
        Assert.Equal(ThemeVariant.Dark, Avalonia.Application.Current!.ActualThemeVariant);
        Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
        Assert.Same(Token("PassdownSurfaceBrush", ThemeVariant.Dark), panel.Background);
        Assert.Equal(1, CheckedAppearanceOptions(window));
        Assert.True(AppearanceOption(window, "Dark").IsChecked);
        Assert.Empty(WrongThemeBrushes(ThemeVariant.Dark, window));
        Assert.Equal([Application.Preferences.AppearancePreference.Dark], harness.Store.Saves);

        AppearanceOption(window, "Light").IsChecked = true; // pointer path: the RadioButton checks itself
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(ThemeVariant.Light, window.ActualThemeVariant);
        Assert.Equal(1, CheckedAppearanceOptions(window));
        Assert.Empty(WrongThemeBrushes(ThemeVariant.Light, window));

        window.Close();
    }

    [Fact]
    public void Appearance_ArrowKeysMoveTheSelection_AndTabLeavesTheGroup()
    {
        using var harness = new AppearanceHarness();
        var viewModel = CreateAppearanceMainViewModel(harness.Controller);
        var window = ShowMainWindow(viewModel, 1280, 860);
        viewModel.SelectedSection = AppSection.Settings;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var system = AppearanceOption(window, "Use Windows setting");
        system.Focus();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(system.IsFocused);
        Assert.True(system.IsKeyboardFocusWithin);

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var focused = window.FocusManager!.GetFocusedElement() as RadioButton;
        Assert.NotNull(focused);
        Assert.Equal("Light", AutomationProperties.GetName(focused));
        Assert.True(focused.IsChecked); // the arrow selects, like a native radio group
        Assert.Equal(Application.Preferences.AppearancePreference.Light, harness.Controller.Preference);

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(Application.Preferences.AppearancePreference.Dark, harness.Controller.Preference);
        Assert.Equal(1, CheckedAppearanceOptions(window));

        window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(Application.Preferences.AppearancePreference.Light, harness.Controller.Preference);

        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.IsNotType<RadioButton>(window.FocusManager!.GetFocusedElement()); // one Tab stop for the group
        Assert.Equal(Application.Preferences.AppearancePreference.Light, harness.Controller.Preference); // Tab never changes it

        window.Close();
    }

    [Theory]
    [InlineData(Application.Preferences.AppearancePreference.System, "Use Windows setting")]
    [InlineData(Application.Preferences.AppearancePreference.Dark, "Dark")]
    public void Appearance_TabIntoTheGroup_LandsOnTheCheckedOption_AndSpaceKeepsIt(Application.Preferences.AppearancePreference stored, string expected)
    {
        using var harness = new AppearanceHarness(stored);
        var viewModel = CreateAppearanceMainViewModel(harness.Controller);
        var window = ShowMainWindow(viewModel, 1280, 860);
        viewModel.SelectedSection = AppSection.Settings;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Shift+Tab back from the next control on the page (Enable Personal Sync).
        window.GetVisualDescendants().OfType<CheckBox>().Single(c => AutomationProperties.GetName(c) == "Enable Personal Sync").Focus();
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Shift);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var focused = Assert.IsType<RadioButton>(window.FocusManager!.GetFocusedElement());
        Assert.Equal(expected, AutomationProperties.GetName(focused));
        Assert.True(focused.IsFocused);

        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(stored, harness.Controller.Preference); // Space on the checked option leaves it checked
        Assert.Equal(1, CheckedAppearanceOptions(window));
        Assert.Empty(harness.Store.Saves);

        window.Close();
    }

    [Fact]
    public void Appearance_EveryPrimaryScreen_RestylesLive_InBothDirections()
    {
        using var harness = new AppearanceHarness();
        var viewModel = CreateAppearanceMainViewModel(harness.Controller);
        var window = ShowMainWindow(viewModel, 1280, 860);

        var screens = new (string Name, Action Go)[]
        {
            ("Dashboard", () => viewModel.SelectedSection = AppSection.Dashboard),
            ("Calendar", () => viewModel.SelectedSection = AppSection.OperationsCalendar),
            ("Associate Overview", () => { viewModel.SelectedSection = AppSection.AssociatePassdown; viewModel.Associate.ShowOverviewSectionCommand.Execute(null); }),
            ("Associate Products", () => { viewModel.SelectedSection = AppSection.AssociatePassdown; viewModel.Associate.ShowProductsSectionCommand.Execute(null); }),
            ("Associate Notes and Shows", () => { viewModel.SelectedSection = AppSection.AssociatePassdown; viewModel.Associate.ShowNotesSectionCommand.Execute(null); }),
            ("Operational Notes", () => viewModel.SelectedSection = AppSection.OperationalNotes),
            ("History", () => viewModel.SelectedSection = AppSection.History),
            ("Settings", () => viewModel.SelectedSection = AppSection.Settings),
        };

        foreach (var (name, go) in screens)
        {
            go();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            foreach (var preference in new[] { Application.Preferences.AppearancePreference.Dark, Application.Preferences.AppearancePreference.Light })
            {
                harness.Controller.SetPreferenceAsync(preference);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var shown = preference == Application.Preferences.AppearancePreference.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
                Assert.Equal(shown, window.ActualThemeVariant);
                var wrong = WrongThemeBrushes(shown, window);
                Assert.True(wrong.Count == 0, $"{name} in {shown}: {string.Join("; ", wrong.Take(6))}");
                AssertNoControlClipsHorizontally(window);
            }
        }

        window.Close();
    }

    [Fact]
    public async Task Appearance_SeasonPriceHistoryOverlay_DashboardShowtimes_AndCalendarShows_RestyleWhileOpen()
    {
        using var harness = new AppearanceHarness();
        var viewModel = CreateAppearanceMainViewModel(harness.Controller);
        var window = ShowMainWindow(viewModel, 1280, 860);

        // Dashboard "+N more" showtimes expanded.
        await viewModel.Dashboard.PendingLoad;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var toggle = window.GetVisualDescendants().OfType<ToggleButton>()
            .First(t => t.IsEffectivelyVisible && (AutomationProperties.GetName(t) ?? string.Empty).StartsWith("Show all ", StringComparison.Ordinal));
        toggle.IsChecked = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        await harness.Controller.SetPreferenceAsync(Application.Preferences.AppearancePreference.Dark);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(toggle.IsChecked);
        Assert.Empty(WrongThemeBrushes(ThemeVariant.Dark, window));

        // Calendar Shows expansion.
        viewModel.SelectedSection = AppSection.OperationsCalendar;
        viewModel.Calendar.IsShowScheduleExpanded = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        await harness.Controller.SetPreferenceAsync(Application.Preferences.AppearancePreference.Light);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Empty(WrongThemeBrushes(ThemeVariant.Light, window));

        // Season Price History overlay, opened over Associate Products.
        viewModel.SelectedSection = AppSection.AssociatePassdown;
        viewModel.Associate.ShowProductsSectionCommand.Execute(null);
        await viewModel.PriceHistory!.OpenAsync("gold-pass");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.PriceHistory.IsOpen);
        await harness.Controller.SetPreferenceAsync(Application.Preferences.AppearancePreference.Dark);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.PriceHistory.IsOpen); // not closed by the switch
        Assert.Empty(WrongThemeBrushes(ThemeVariant.Dark, window));

        window.Close();
    }

    [Theory]
    [InlineData("Operating date")]
    [InlineData("Copy for Teams")]
    [InlineData("More passdown actions")]
    [InlineData("Insert math symbol or equation")]
    public void Appearance_AnOpenPopup_RestylesWhenTheThemeChanges(string buttonName)
    {
        using var harness = new AppearanceHarness();
        var viewModel = CreateAppearanceMainViewModel(harness.Controller);
        var window = ShowMainWindow(viewModel, 1280, 860);
        viewModel.SelectedSection = AppSection.AssociatePassdown;
        viewModel.Associate.ShowOverviewSectionCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var button = window.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == buttonName);
        var flyout = Assert.IsAssignableFrom<FlyoutBase>(button.Flyout);
        flyout.ShowAt(button);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(flyout.IsOpen);

        foreach (var preference in new[] { Application.Preferences.AppearancePreference.Dark, Application.Preferences.AppearancePreference.Light, Application.Preferences.AppearancePreference.Dark })
        {
            harness.Controller.SetPreferenceAsync(preference);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var shown = preference == Application.Preferences.AppearancePreference.Dark ? ThemeVariant.Dark : ThemeVariant.Light;

            Assert.True(flyout.IsOpen, "the switch does not close the popup");
            var roots = PopupRoots(flyout);
            Assert.NotEmpty(roots);
            Assert.All(roots, r => Assert.Equal(shown, ((Control)r).ActualThemeVariant));
            var wrong = WrongThemeBrushes(shown, roots);
            Assert.True(wrong.Count == 0, $"{buttonName} in {shown}: {string.Join("; ", wrong.Take(6))}");
        }

        flyout.Hide();
        window.Close();
    }

    private static Visual[] PopupRoots(FlyoutBase flyout) => flyout switch
    {
        MenuFlyout menu => menu.Items.OfType<Visual>().ToArray(),
        Flyout { Content: Visual content } => [content],
        _ => [],
    };

    [Fact]
    public void Appearance_SystemMode_FollowsASimulatedOsSwitch_AndForcedModesIgnoreIt()
    {
        using var harness = new AppearanceHarness(os: Application.Preferences.EffectiveAppearance.Light);
        var viewModel = CreateAppearanceMainViewModel(harness.Controller);
        var window = ShowMainWindow(viewModel, 1280, 860);
        viewModel.SelectedSection = AppSection.Settings;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var hint = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text?.StartsWith("Windows is currently set to", StringComparison.Ordinal) == true);

        harness.Os.Switch(Application.Preferences.EffectiveAppearance.Dark);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
        Assert.Equal("Windows is currently set to Dark.", hint.Text);
        Assert.Empty(WrongThemeBrushes(ThemeVariant.Dark, window));

        AppearanceOption(window, "Light").IsChecked = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        harness.Os.Switch(Application.Preferences.EffectiveAppearance.Light);
        harness.Os.Switch(Application.Preferences.EffectiveAppearance.Dark);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(ThemeVariant.Light, window.ActualThemeVariant); // forced Light ignores the OS

        AppearanceOption(window, "Use Windows setting").IsChecked = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant); // back to System: the OS's current Dark at once
        Assert.Equal(1, CheckedAppearanceOptions(window));

        window.Close();
    }

    [Theory]
    [InlineData(Application.Preferences.AppearancePreference.Light, "Light")]
    [InlineData(Application.Preferences.AppearancePreference.Dark, "Dark")]
    public void Appearance_ASavedForcedChoice_IsTheFirstThemeTheWindowEverShows(Application.Preferences.AppearancePreference stored, string expected)
    {
        // The opposite OS appearance proves the saved choice — not the OS — decides the first frame.
        var os = stored == Application.Preferences.AppearancePreference.Dark ? Application.Preferences.EffectiveAppearance.Light : Application.Preferences.EffectiveAppearance.Dark;
        using var harness = new AppearanceHarness(stored, os);
        var viewModel = CreateAppearanceMainViewModel(harness.Controller);
        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 860 };
        var first = new List<ThemeVariant>();
        window.Opened += (_, _) => first.Add(window.ActualThemeVariant);
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal([expected == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light], first);
        viewModel.SelectedSection = AppSection.Settings;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(AppearanceOption(window, expected).IsChecked);
        Assert.Equal(1, CheckedAppearanceOptions(window));
        window.Close();
    }

    [Fact]
    public void Appearance_FuryBanner_IsIdenticalInBothThemes()
    {
        using var harness = new AppearanceHarness();
        var window = ShowMainWindow(CreateAppearanceMainViewModel(harness.Controller), 1280, 860);
        var banner = (Border)Named(window, "BrandBanner");
        var image = (Image)Named(window, "BrandBannerImage");
        var wordmark = (TextBlock)Named(window, "BrandWordmark");
        var overlay = banner.GetVisualDescendants().OfType<Border>().Single(b => b != banner && b.Background is not null);

        (object? Source, IBrush? Overlay, IBrush? Text, double Height, Stretch Stretch) Snapshot() =>
            (image.Source, overlay.Background, wordmark.Foreground, banner.Bounds.Height, image.Stretch);

        var light = Snapshot();
        harness.Controller.SetPreferenceAsync(Application.Preferences.AppearancePreference.Dark);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var dark = Snapshot();

        Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
        Assert.Same(light.Source, dark.Source);
        Assert.Same(light.Overlay, dark.Overlay);
        Assert.Same(light.Text, dark.Text);
        Assert.Equal(72, dark.Height, 1);
        Assert.Equal(light.Stretch, dark.Stretch);
        Assert.Empty(banner.GetVisualDescendants().OfType<PathIcon>()); // title-only (Phase 23B)
        window.Close();
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Appearance_CheckedOption_IsDistinguishable_WithoutColorAlone_AndMeetsNonTextContrast(string theme)
    {
        using var harness = new AppearanceHarness(theme == "Dark" ? Application.Preferences.AppearancePreference.Dark : Application.Preferences.AppearancePreference.Light);
        var viewModel = CreateAppearanceMainViewModel(harness.Controller);
        var window = ShowMainWindow(viewModel, 1280, 860);
        viewModel.SelectedSection = AppSection.Settings;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var checkedOption = AppearanceOption(window, theme);
        var uncheckedOption = AppearanceOption(window, "Use Windows setting");
        var panel = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "AppearanceSection");
        var surface = ((ISolidColorBrush)panel.Background!).Color;

        // Fluent draws the checked state as an extra inner dot (shape, not only color) on an accent ring.
        int VisibleDots(RadioButton r) => r.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Count(e => e.IsEffectivelyVisible && e.Opacity > 0);
        Assert.True(VisibleDots(checkedOption) > VisibleDots(uncheckedOption), "the checked option shows its dot");

        var accent = checkedOption.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>()
            .Where(e => e.IsEffectivelyVisible && e.Fill is ISolidColorBrush { Color.A: > 200 })
            .Select(e => ((ISolidColorBrush)e.Fill!).Color)
            .OrderByDescending(c => ContrastRatio(c, surface))
            .First();
        Assert.True(ContrastRatio(accent, surface) >= 3.0, $"{theme}: checked indicator {accent} on {surface} is {ContrastRatio(accent, surface):F2}:1");

        window.Close();
    }

    private static double ContrastRatio(Color a, Color b)
    {
        static double Channel(byte c) { var s = c / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        static double Luminance(Color c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [Theory]
    [InlineData(960d, 640d, "Light")]
    [InlineData(960d, 640d, "Dark")]
    [InlineData(1280d, 690d, "Dark")]
    [InlineData(1280d, 860d, "Light")]
    [InlineData(1920d, 1000d, "Dark")]
    public void Appearance_Selector_FitsEveryWindowSize_WithoutClippingOrHorizontalScroll(double width, double height, string theme)
    {
        using var harness = new AppearanceHarness(theme == "Dark" ? Application.Preferences.AppearancePreference.Dark : Application.Preferences.AppearancePreference.Light);
        var viewModel = CreateAppearanceMainViewModel(harness.Controller);
        var window = ShowMainWindow(viewModel, width, height);
        viewModel.SelectedSection = AppSection.Settings;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var panel = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "AppearanceSection");
        var options = new[] { "Use Windows setting", "Light", "Dark" }.Select(n => AppearanceOption(window, n)).ToList();
        foreach (var option in options)
        {
            var bounds = AbsoluteBounds(option, window);
            Assert.True(option.IsEffectivelyVisible);
            Assert.True(bounds.Width > 0 && bounds.Height >= 20, $"'{option.Content}' is {bounds.Width}×{bounds.Height}");
            Assert.True(bounds.Right <= AbsoluteBounds(panel, window).Right + 1);
        }

        // Stacked top to bottom in the stated order, never overlapping.
        Assert.True(AbsoluteBounds(options[0], window).Bottom <= AbsoluteBounds(options[1], window).Top + 0.5);
        Assert.True(AbsoluteBounds(options[1], window).Bottom <= AbsoluteBounds(options[2], window).Top + 0.5);

        var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "SettingsScroll");
        Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1, $"horizontal overflow {scroll.Extent.Width - scroll.Viewport.Width:F0}px");
        AssertNoControlClipsHorizontally(window);
        Assert.Empty(WrongThemeBrushes(theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light, window));
        window.Close();
    }

    // ==================================================================================
    // Phase 23D — Application Updates and What's New in System / Light / Dark. Uses the Phase 23C harness
    // (application-wide theme), so it lives in this run-alone class; the stored appearance is never changed.
    // ==================================================================================

    [Theory]
    [InlineData("System-Light")]
    [InlineData("System-Dark")]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Updates_SettingsPanelAndWhatsNew_FollowEveryAppearance_WithoutClipping_AndNeverChangeTheTheme(string mode)
    {
        var (stored, os, shown) = mode switch
        {
            "System-Light" => (Application.Preferences.AppearancePreference.System, Application.Preferences.EffectiveAppearance.Light, ThemeVariant.Light),
            "System-Dark" => (Application.Preferences.AppearancePreference.System, Application.Preferences.EffectiveAppearance.Dark, ThemeVariant.Dark),
            "Light" => (Application.Preferences.AppearancePreference.Light, Application.Preferences.EffectiveAppearance.Dark, ThemeVariant.Light),
            _ => (Application.Preferences.AppearancePreference.Dark, Application.Preferences.EffectiveAppearance.Light, ThemeVariant.Dark),
        };
        using var harness = new AppearanceHarness(stored, os);

        var coordinator = new FakeApplicationUpdateCoordinator();
        var notes = new UpdateNotesSource(new Application.Updates.ReleaseNotesContent("0.1.0-rc.7", "Redesigned look", new DateOnly(2026, 10, 17),
            Application.Updates.ReleaseNotesDocument.Parse("## Highlights\n- A refreshed light and dark design.\n- Settings is one organized page.")));
        var whatsNew = new WhatsNewViewModel(notes, coordinator);
        var updates = new ApplicationUpdatesViewModel(coordinator, whatsNew: whatsNew, dispatcher: new ImmediateUiDispatcher(), installedVersion: "0.1.0-rc.7");
        var baseline = CreateAppearanceMainViewModel(harness.Controller);
        var settings = new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator(),
            appearance: new AppearanceSettingsViewModel(harness.Controller), updates: updates);
        var viewModel = new MainViewModel(baseline.Dashboard, baseline.Calendar, baseline.Associate, baseline.Notes, baseline.History, settings, baseline.PriceHistory);
        var window = ShowMainWindow(viewModel, 960, 640);

        coordinator.Publish(new Application.Updates.UpdateAvailabilitySnapshot(Application.Updates.UpdateAvailabilityStatus.UpdateAvailable,
            FakeApplicationUpdateCoordinator.Offer(), DateTimeOffset.UnixEpoch, null, null, false));
        viewModel.SelectedSection = AppSection.Settings;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(shown, window.ActualThemeVariant);
        var panel = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ApplicationUpdatesSection");
        Assert.True(panel.IsEffectivelyVisible);
        var dot = window.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single(e => e.Name == "UpdateIndicatorDot");
        Assert.True(dot.IsEffectivelyVisible);
        var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "SettingsScroll");
        Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1, $"horizontal overflow {scroll.Extent.Width - scroll.Viewport.Width:F0}px");
        AssertNoControlClipsHorizontally(window);
        Assert.Empty(WrongThemeBrushes(shown, window));

        updates.ShowWhatsNewCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var card = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "WhatsNewCard");
        Assert.True(card.IsEffectivelyVisible);
        Assert.Empty(WrongThemeBrushes(shown, window));
        var continueButton = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "WhatsNewContinue");
        Assert.True(continueButton.IsEffectivelyVisible && continueButton.Bounds.Width > 0);

        whatsNew.CloseCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(card.IsEffectivelyVisible);
        Assert.Equal("0.1.0-rc.7", coordinator.LastAcknowledgedReleaseNotesVersion);

        // Updates never touch the appearance preference.
        Assert.Equal(stored, harness.Store.Stored);
        Assert.Empty(harness.Store.Saves);
        Assert.Equal(shown, window.ActualThemeVariant);
        window.Close();
    }

    private sealed class UpdateNotesSource(Application.Updates.ReleaseNotesContent content) : Application.Updates.IBundledReleaseNotesSource
    {
        public Application.Updates.ReleaseNotesContent? LoadCurrent() => content;
    }
}
