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
public partial class Phase15ERealWindowLayoutTests
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

    // Phase 35A: the sidebar replaced the Phase 22B horizontal strip (whose wrap fix this test guarded). Same
    // guarantee in the new geometry: every destination and the toggle is realized inside the window at every
    // supported size, including the 960×640 minimum.
    [Theory]
    [InlineData(1920d, 1080d)]
    [InlineData(1536d, 960d)]
    [InlineData(1280d, 720d)]
    [InlineData(1280d, 860d)]
    [InlineData(960d, 640d)]
    [InlineData(1280d, 690d)]
    public void MainWindow_Sidebar_EveryDestinationReachableWithinWindowBounds(double width, double height)
    {
        var window = ShowMainWindow(CreateMainViewModel(), width, height);

        var items = SectionButtons(window).Cast<Control>().Append(Named(window, "NavigationToggle")).ToList();
        Assert.Equal(7, items.Count);
        foreach (var item in items)
        {
            var bounds = AbsoluteBounds(item, window);
            var label = AutomationProperties.GetName(item);
            Assert.True(bounds.Width > 0 && bounds.Height > 0, $"'{label}' realized with zero size.");
            Assert.True(bounds.Right <= window.ClientSize.Width + 1.0, $"'{label}' right edge {bounds.Right} exceeds {window.ClientSize.Width}.");
            Assert.True(bounds.Bottom <= window.ClientSize.Height + 1.0, $"'{label}' bottom {bounds.Bottom} exceeds {window.ClientSize.Height}.");
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

    // Phase 35A: the six sidebar destinations (Settings last); the toggle is an iconAction, not a navItem.
    private static List<Button> SectionButtons(Window window) =>
        window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("navItem")).ToList();

    private static TextBlock NavLabel(Button item) =>
        item.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("navLabel"));

    private static Border NavIndicator(Button item) =>
        item.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("navIndicator"));

    private static Control Named(Window window, string name) =>
        window.GetVisualDescendants().OfType<Control>().Single(c => c.Name == name);

    // Phase 35A (replaces the Phase 22B banner/strip geometry test): the sidebar spans the full client height,
    // has the expanded width at normal widths and the icon width below the compaction threshold, and its items
    // never move, overlap or change size when the selected section changes.
    [Theory]
    [InlineData(1920d, 1040d, 208d)]
    [InlineData(1536d, 960d, 208d)]
    [InlineData(1280d, 860d, 208d)]
    [InlineData(1280d, 690d, 208d)]
    [InlineData(960d, 640d, 56d)]
    public void MainWindow_Sidebar_GeometryStaysFixedAcrossEverySection(double width, double height, double sidebarWidth)
    {
        var viewModel = CreateMainViewModel();
        var window = ShowMainWindow(viewModel, width, height);
        var navigation = Named(window, "AppNavigation");

        Assert.Equal(sidebarWidth, navigation.Bounds.Width, 1);
        Assert.Equal(window.ClientSize.Height, navigation.Bounds.Height, 1);
        Assert.Equal(0, AbsoluteBounds(navigation, window).Left, 1);

        List<Rect>? first = null;
        foreach (var section in Enum.GetValues<AppSection>())
        {
            viewModel.SelectedSection = section;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var buttons = SectionButtons(window);
            Assert.Equal(6, buttons.Count);
            Assert.Single(buttons, b => b.Classes.Contains("active"));
            var positions = buttons.Select(b => AbsoluteBounds(b, window)).ToList();
            for (var i = 1; i < positions.Count; i++)
            {
                Assert.False(positions[i].Intersects(positions[i - 1]), $"navigation items {i - 1} and {i} overlap");
                Assert.True(positions[i].Top > positions[i - 1].Top, "destinations run top to bottom in the established order");
            }

            Assert.All(positions, p => Assert.InRange(p.Height, 32, 44));
            Assert.All(positions, p => Assert.True(p.Right <= sidebarWidth + 0.5, "an item extends past the sidebar"));
            Assert.Equal(sidebarWidth, navigation.Bounds.Width, 1);
            first ??= positions;
            Assert.Equal(first, positions); // selecting a section (SemiBold, fill) never moves or resizes any item
        }

        window.Close();
    }

    [Fact]
    public void MainWindow_ActiveDestination_FollowsTheSelectedSection_AndOnlyOneIsActive()
    {
        var viewModel = CreateMainViewModel();
        var window = ShowMainWindow(viewModel, 1280, 860);
        // Brush assertions need a resolved theme; an earlier test in this run-alone class can leave the
        // application variant unresolved for new windows (the theme-switch test pins it the same way).
        window.RequestedThemeVariant = ThemeVariant.Light;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var expected = new Dictionary<AppSection, string>
        {
            // Phase 35A: sidebar labels (the sidebar has room for "Operational Notes"; Settings is labelled too).
            [AppSection.Dashboard] = "Dashboard", [AppSection.OperationsCalendar] = "Calendar",
            [AppSection.AssociatePassdown] = "Passdown", [AppSection.OperationalNotes] = "Operational Notes",
            [AppSection.History] = "History", [AppSection.Settings] = "Settings",
        };

        foreach (var button in SectionButtons(window).AsEnumerable().Reverse())
        {
            button.Command!.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var active = Assert.Single(SectionButtons(window), b => b.Classes.Contains("active"));
            Assert.Same(button, active);
            Assert.Equal(expected[viewModel.SelectedSection], NavLabel(active).Text);
            Assert.Equal("Current page", AutomationProperties.GetItemStatus(active));
            Assert.All(SectionButtons(window).Where(b => b != active), b => Assert.Null(AutomationProperties.GetItemStatus(b)));
            Assert.Equal(FontWeight.SemiBold, active.FontWeight);
            Assert.Equal(3, NavIndicator(active).Bounds.Width, 0.5); // shape, not colour alone
            Assert.True(NavIndicator(active).Background is ISolidColorBrush { Color.A: 255 }, "the active indicator is drawn");
            Assert.All(SectionButtons(window).Where(b => b != active), b => Assert.True(NavIndicator(b).Background is ISolidColorBrush { Color.A: 0 }, "inactive indicators are invisible"));
        }

        window.Close();
    }

    [Fact]
    public void MainWindow_Navigation_IsKeyboardReachableInOrder_AndEnterActivatesTheFocusedItem()
    {
        var viewModel = CreateMainViewModel();
        var window = ShowMainWindow(viewModel, 1280, 860);
        var buttons = SectionButtons(window);
        var toggle = (Button)Named(window, "NavigationToggle");
        Assert.All(buttons, b => Assert.True(b.Focusable && b.IsTabStop && b.IsEffectivelyEnabled, $"{AutomationProperties.GetName(b)} not keyboard reachable"));

        // Phase 35A: Tab runs toggle → Dashboard → … → History → Settings.
        Assert.True(toggle.Focus(NavigationMethod.Tab));
        foreach (var expected in buttons)
        {
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Same(expected, window.FocusManager!.GetFocusedElement());
            Assert.Contains(":focus-visible", ((Avalonia.Controls.Primitives.TemplatedControl)expected).Classes); // visible keyboard focus
        }

        Assert.True(buttons[4].Focus(NavigationMethod.Tab)); // History
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(AppSection.History, viewModel.SelectedSection);
        Assert.True(buttons[4].IsFocused, "activating a section must not drop keyboard focus");

        window.Close();
    }

    // Phase 35A: Settings is pinned to the bottom of the sidebar (gear + label), after History in Tab order,
    // activated by Space, and announced as the current page when selected.
    [Theory]
    [InlineData(1280d, 860d)]
    [InlineData(960d, 640d)]
    public void MainWindow_Settings_IsPinnedToTheBottom_KeyboardOperable_AndShowsTheSelectedState(double width, double height)
    {
        var viewModel = CreateMainViewModel();
        var window = ShowMainWindow(viewModel, width, height);
        var buttons = SectionButtons(window);
        var history = buttons[4];
        var gear = buttons[5];

        Assert.Equal("SettingsNavigationItem", gear.Name);
        Assert.Equal("Settings", AutomationProperties.GetName(gear));
        Assert.Equal("Settings", ToolTip.GetTip(gear));
        Assert.Single(gear.GetVisualDescendants().OfType<PathIcon>());
        var gearBounds = AbsoluteBounds(gear, window);
        var navigation = AbsoluteBounds(Named(window, "AppNavigation"), window);
        Assert.True(navigation.Bottom - gearBounds.Bottom <= 16, "Settings must sit at the bottom of the sidebar");
        Assert.True(gearBounds.Top > AbsoluteBounds(history, window).Bottom + 8, "Settings must be separated from the destinations");
        Assert.Equal(AbsoluteBounds(history, window).Height, gearBounds.Height, 0.5);

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
    public void MainWindow_ThemeSwitch_RestylesTheSidebar_WithItsSemanticTokens()
    {
        var viewModel = CreateMainViewModel();
        var window = ShowMainWindow(viewModel, 1280, 860);
        var navigation = (Border)Named(window, "AppNavigation");

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark, ThemeVariant.Light })
        {
            window.RequestedThemeVariant = variant;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            IBrush Token(string key) => window.TryFindResource(key, variant, out var value) ? (IBrush)value! : throw new Xunit.Sdk.XunitException(key);
            var buttons = SectionButtons(window);
            var active = Assert.Single(buttons, b => b.Classes.Contains("active"));
            Assert.Same(Token("PassdownNavigationBackgroundBrush"), navigation.Background);
            Assert.Same(Token("PassdownDividerBrush"), navigation.BorderBrush);
            Assert.Same(Token("PassdownTextPrimaryBrush"), active.Foreground);
            Assert.Same(Token("PassdownNavigationActiveBackgroundBrush"), active.Background);
            Assert.Same(Token("PassdownNavigationActiveIndicatorBrush"), NavIndicator(active).Background);
            Assert.All(buttons.Where(b => b != active), b => Assert.Same(Token("PassdownTextSecondaryBrush"), b.Foreground));
        }

        window.Close();
    }

    // Phase 35A (replaces the Phase 22B banner-photograph test): the compact title area is the native title bar —
    // the original icon and "Carowinds Operations" — with standard window decorations; nothing in the client area
    // repeats the title or shows a photograph.
    [Fact]
    public void MainWindow_TitleArea_IsTheNativeTitleBar_WithTheOriginalIcon_AndNoBanner()
    {
        var window = ShowMainWindow(CreateMainViewModel(), 960, 640);

        Assert.Equal("Carowinds Operations", window.Title);
        Assert.NotNull(window.Icon);
        Assert.False(window.ExtendClientAreaToDecorationsHint);
        Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Control>(), c => c.Name is "BrandBanner" or "BrandWordmark" or "BrandBannerImage");
        Assert.Empty(Named(window, "AppNavigation").GetVisualDescendants().OfType<Image>());
        window.Close();
    }

    // Phase 35A: each page fills everything to the right of the sidebar — full client height, no shell band above.
    [Theory]
    [InlineData(960d, 640d)]
    [InlineData(1280d, 690d)]
    [InlineData(1536d, 960d)]
    public void MainWindow_SectionContent_FillsTheAreaBesideTheSidebar(double width, double height)
    {
        var viewModel = CreateMainViewModel();
        var window = ShowMainWindow(viewModel, width, height);
        var sidebarRight = AbsoluteBounds(Named(window, "AppNavigation"), window).Right;

        foreach (var section in Enum.GetValues<AppSection>())
        {
            viewModel.SelectedSection = section;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var host = window.GetVisualDescendants().OfType<Border>()
                .Single(b => b.IsVisible && b.Child is UserControl && b.GetVisualParent() is Grid { Name: "SectionHost" });
            var bounds = AbsoluteBounds(host, window);
            Assert.Equal(0, bounds.Top, 1);
            Assert.Equal(sidebarRight, bounds.Left, 1);
            Assert.Equal(window.ClientSize.Width - sidebarRight, bounds.Width, 1);
            Assert.Equal(window.ClientSize.Height, bounds.Height, 1);
        }

        window.Close();
    }

    // ==================================================================================
    // Associate action bar (regression: AssociateView.axaml's WrapPanel fix)
    // ==================================================================================

    // Phase 31E: show selection is its own rail section. On the Shows page the panel and every show
    // control fit the window; Overview shows Include in Passdown and no Shows panel.
    [Theory]
    [InlineData(1920d, 1000d)]
    [InlineData(1280d, 860d)]
    [InlineData(1280d, 690d)]
    [InlineData(960d, 640d)]
    public async Task AssociateView_ShowsPage_FitsTheWindow_AndOverviewHasNoShowsPanel(double width, double height)
    {
        var service = new FakeAssociatePassdownService();
        service.ShowSchedules[FixedDate] = new Carowinds.Passdown.Application.Shows.ShowScheduleView(
            FixedDate, Carowinds.Passdown.Application.Shows.ShowScheduleState.Scheduled,
            [new Carowinds.Passdown.Application.Retrieval.ScheduledShow("SHOW-1", "A Show With A Deliberately Long Name For Wrapping Across The Column", "Carowinds Theatre", [new TimeOnly(13), new TimeOnly(16, 30)])],
            false, null, null);
        var viewModel = new AssociateViewModel(service, CreateClock());
        await viewModel.PendingLoad;
        var window = RealizeInWindow(new AssociateView { DataContext = viewModel }, width, height);

        Control NamedControl(string name) => window.GetVisualDescendants().OfType<Control>().Single(c => c.Name == name);
        Assert.True(NamedControl("IncludeInPassdownPanel").IsEffectivelyVisible);
        // A hidden section's content is not even realized; either way no Shows panel is visible on Overview.
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Control>(), c => c.Name == "ShowsPanel" && c.IsEffectivelyVisible);

        viewModel.ShowShowsSectionCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Control>(), c => c.Name == "IncludeInPassdownPanel" && c.IsEffectivelyVisible);
        var panel = AbsoluteBounds(NamedControl("ShowsPanel"), window);
        Assert.True(panel.Width > 0 && panel.Right <= window.ClientSize.Width + 1.0, "Shows panel is clipped on the right.");
        foreach (var name in new[] { "Select every show on the official schedule", "Clear the show selection" })
        {
            var button = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == name);
            Assert.True(button.IsEffectivelyVisible, $"'{name}' is not visible on the Shows page.");
            Assert.True(AbsoluteBounds(button, window).Right <= window.ClientSize.Width + 1.0, $"'{name}' is clipped.");
        }

        var showCheckBox = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.IsEffectivelyVisible && c.DataContext is AssociateShowRowViewModel);
        Assert.True(AbsoluteBounds(showCheckBox, window).Right <= window.ClientSize.Width + 1.0, "The show row is clipped.");
        AssertNoControlClipsHorizontally(window);
        window.Close();
    }

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
        // Phase 35D follow-up: Today shows only off today — use the previous day so the widest toolbar (with Today) is checked.
        viewModel.PreviousDayCommand.Execute(null);
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
        // Phase 35B: Check Online, Print, Save PDF and Copy moved into the Actions menu (asserted below).
        var actionNames = new[] { "Previous day", "Operating date", "Next day", "Today", "Create PDF", "Passdown actions" };
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

        var actionsMenu = Assert.IsType<Avalonia.Controls.MenuFlyout>(buttonsByName["Passdown actions"].Flyout);
        var menuNames = actionsMenu.Items.OfType<MenuItem>().Select(AutomationProperties.GetName).ToList();
        foreach (var name in new[] { "Print Passdown", "Save PDF", "Copy operations summary", "Copy today's pricing", "Copy operations summary and pricing", "Check this date online" })
        {
            Assert.Contains(name, menuNames);
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

        // Phase 35E Dashboard: the four gate areas sit in one row when the content is wide enough and 2 × 2
        // below that; the card columns stay side by side at every supported width (MinWidth is 960).
        var overview = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "OperationsOverview");
        var gates = new[] { "GateHoursColumn", "GateAvenueColumn", "GateNorthColumn", "GateErtColumn" }
            .Select(name => AbsoluteBounds(overview.GetVisualDescendants().OfType<Border>().Single(b => b.Name == name), window)).ToList();
        if (width - 48 >= DashboardView.WideMinWidth)
        {
            Assert.All(gates, g => Assert.Equal(gates[0].Top, g.Top, 1));
            for (var i = 1; i < gates.Count; i++)
            {
                Assert.True(gates[i].Left >= gates[i - 1].Right - 0.5, $"gate area {i} must follow area {i - 1} on the same row");
            }
        }
        else
        {
            Assert.Equal(gates[0].Top, gates[1].Top, 1);
            Assert.True(gates[1].Left >= gates[0].Right - 0.5, "Avenue Entrance sits beside park hours");
            Assert.True(gates[2].Top >= gates[0].Bottom - 0.5, "North Gate starts a second row");
            Assert.Equal(gates[2].Top, gates[3].Top, 1);
        }

        Assert.All(gates, b => Assert.True(b.Right <= window.ClientSize.Width + 1.0));
        var primary = AbsoluteBounds(window.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "DashboardPrimaryColumn"), window);
        var secondary = AbsoluteBounds(window.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "DashboardSecondaryColumn"), window);
        Assert.Equal(primary.Top, secondary.Top, 1);
        Assert.True(secondary.Left >= primary.Right + 15, "pricing, notes and perks sit to the right of the passdown and shows");
        Assert.True(secondary.Right <= window.ClientSize.Width + 1.0);

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
    [InlineData(1920d, 1000d, true)]
    [InlineData(1280d, 860d, true)]
    [InlineData(960d, 640d, false)] // minimum window: park hours above the fold, attendance one scroll away (see below)
    public async Task AssociateView_Overview_ParkHoursThenAttendance_OnOneSheet_AboveTheFold(double width, double height, bool attendanceAboveFold)
    {
        // Phase 35B (approved Overview reference): Park Hours & Gates first, then Attendance & Early Ride Time
        // directly below it on the same editing sheet — replacing Phase 22D's side-by-side first row.
        var (window, _, _) = await RealizeBusyAssociateAsync(width, height, ThemeVariant.Light);
        var hours = window.GetVisualDescendants().OfType<StackPanel>().Single(p => AutomationProperties.GetName(p) == "Park hours");
        var attendance = window.GetVisualDescendants().OfType<StackPanel>().Single(p => AutomationProperties.GetName(p) == "Attendance");
        var sheet = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "OverviewSheet");

        var hoursBounds = AbsoluteBounds(hours, window);
        var attendanceBounds = AbsoluteBounds(attendance, window);
        var sheetBounds = AbsoluteBounds(sheet, window);
        Assert.True(hoursBounds.Bottom <= attendanceBounds.Top, "park hours and attendance overlap.");
        Assert.True(Math.Abs(hoursBounds.Left - attendanceBounds.Left) < 1, "the two regions should share the sheet's left edge.");
        Assert.True(sheetBounds.Contains(hoursBounds) && sheetBounds.Contains(attendanceBounds), "both regions sit on the one Overview sheet.");
        Assert.True(hoursBounds.Top < height, $"park hours must start above the fold (hours {hoursBounds.Top:F0}, window {height:F0}).");
        if (attendanceAboveFold)
        {
            Assert.True(attendanceBounds.Top < height, $"attendance must start above the fold (attendance {attendanceBounds.Top:F0}, window {height:F0}).");
        }
        else
        {
            // The approved layout stacks Park Hours & Gates (with its official-events note) above Attendance. At the
            // 960×640 minimum the headless fake font metrics push Attendance below the fold, so here it must be one
            // vertical scroll away inside the Overview — fully visible, never clipped sideways.
            attendance.BringIntoView();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var scrolled = AbsoluteBounds(attendance, window);
            Assert.True(scrolled.Top >= 0 && scrolled.Bottom <= height + 1, $"attendance is not reachable by scrolling ({scrolled}).");
            Assert.True(scrolled.Right <= width + 1, "attendance is clipped on the right.");
        }

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
            // Phase 35F: title with its lifecycle pill, a text preview (at most two lines), "Category · Priority" with the
            // schedule: three blocks, 22px padding + 2×5px spacing (the approved prototype's row rhythm).
            var (blocks, content) = Phase22ERowContent(item, window);
            Assert.Equal(3, blocks);
            Assert.True(item.Bounds.Height <= content + 36, $"note row {item.Bounds.Height:F0}px for {content:F0}px of text");
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
            // Phase 35F: 24px padding, the PDF pill and 4px between the lines (the approved prototype's row rhythm).
            Assert.True(item.Bounds.Height <= content + 32, $"history row {item.Bounds.Height:F0}px for {content:F0}px of text");
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

        // Phase 35F: Enable Personal Sync is a switch (same PersonalSyncEnabled binding as the earlier check box).
        var checkBox = window.GetVisualDescendants().OfType<ToggleSwitch>().Single(c => AutomationProperties.GetName(c) == "Enable Personal Sync");
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
    public void DashboardView_ShowEntry_ReadsNameThenTimes_WithTheVenueBesideThem_NeverOnTop(double width, double height)
    {
        // Phase 35E Dashboard: the approved row keeps name and times in the left column and the venue in its own
        // right-hand column (Phase 23B's defect — times running under the venue — stays impossible).
        var (window, _, _) = RealizeShowsDashboard(width, height, ThemeVariant.Light);

        foreach (var cell in ShowCells(window))
        {
            var texts = cell.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !t.GetVisualAncestors().OfType<ToggleButton>().Any()).ToList();
            var name = AbsoluteBounds(texts.Single(t => t.Classes.Contains("showName")), window);
            var times = AbsoluteBounds(texts.Single(t => t.Classes.Contains("showTimes")), window);
            Assert.True(times.Top >= name.Bottom - 0.5, "times start under the name");
            if (texts.SingleOrDefault(t => t.Classes.Contains("showVenue")) is { } venueText)
            {
                var venue = AbsoluteBounds(venueText, window);
                Assert.True(venue.Left >= Math.Max(name.Right, times.Right) - 0.5, "the venue sits to the right of the name and times");
                Assert.Equal(name.Top, venue.Top, 1);
            }
        }

        window.Close();
    }

    [Theory]
    [InlineData(960d, 640d)]
    [InlineData(1280d, 860d)]
    [InlineData(1920d, 1000d)]
    public void DashboardView_Shows_AreOneListInScheduleOrder(double width, double height)
    {
        var (window, viewModel, _) = RealizeShowsDashboard(width, height, ThemeVariant.Light);
        var cells = ShowCells(window).Select(c => AbsoluteBounds(c, window)).ToList();

        Assert.Equal(viewModel.ShowRows.Count, cells.Count);
        Assert.Single(cells.Select(c => Math.Round(c.Left)).Distinct());
        for (var i = 1; i < cells.Count; i++)
        {
            Assert.True(cells[i].Top >= cells[i - 1].Bottom - 0.5, $"entry {i} ({viewModel.ShowRows[i].Name}) is out of schedule order");
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

    // Phase 35A (replaces the Phase 23B title-only banner test): no role label, decorative heading or repeated
    // title — the sidebar's only text is the six destination labels, each on one line.
    [Theory]
    [InlineData(1280d, 690d)]
    [InlineData(1920d, 1000d)]
    public void MainWindow_Sidebar_HasOnlyDestinationLabels_NoHeadingOrRoleLabel(double width, double height)
    {
        var window = ShowMainWindow(CreateMainViewModel(), width, height);
        var texts = Named(window, "AppNavigation").GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).ToList();

        Assert.Equal(["Dashboard", "Calendar", "Passdown", "Operational Notes", "History", "Settings"], texts.Select(t => t.Text));
        Assert.All(texts, t => Assert.Contains("navLabel", t.Classes));
        Assert.All(texts, t => Assert.Single(t.TextLayout.TextLines));
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text is "Carowinds Operations" or "CAROWINDS OPERATIONS" or "Admissions Supervision" or "ADMISSIONS" or "OPERATIONS");
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
        // One Tab stop for the group: Tab leaves the theme options. Since Phase 35F the next stop is the Interface
        // animations switch (35A had a second radio group there).
        var next = window.FocusManager!.GetFocusedElement();
        Assert.False(next is RadioButton { GroupName: "Appearance" }, "Tab must leave the theme group");
        Assert.True(next is ToggleSwitch { Name: "InterfaceAnimationsSwitch" }, "Tab lands on the Interface animations switch");

        // Phase 35F: the options sit side by side, so Left / Right move the selection too (Up / Down still do, above).
        AppearanceOption(window, "Light").Focus();
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(Application.Preferences.AppearancePreference.Dark, harness.Controller.Preference);
        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(Application.Preferences.AppearancePreference.Light, harness.Controller.Preference);
        Assert.Equal(1, CheckedAppearanceOptions(window));
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

        // Shift+Tab back from the next stop on the page. Phase 31D put the Passdown defaults checkboxes after
        // Appearance; the Interface animations control sits between them (a radio group in 35A, one switch since
        // 35F), so Shift+Tab goes checkbox → animations switch → theme group.
        window.GetVisualDescendants().OfType<CheckBox>().Single(c => AutomationProperties.GetName(c) == "Select Daily Admission automatically for a new Passdown").Focus();
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Shift);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(window.FocusManager!.GetFocusedElement() is ToggleSwitch { Name: "InterfaceAnimationsSwitch" });
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
            ("Associate Shows", () => { viewModel.SelectedSection = AppSection.AssociatePassdown; viewModel.Associate.ShowShowsSectionCommand.Execute(null); }),
            ("Associate Notes", () => { viewModel.SelectedSection = AppSection.AssociatePassdown; viewModel.Associate.ShowNotesSectionCommand.Execute(null); }),
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
    [InlineData("Passdown actions")] // Phase 35B: the Copy and More menus are now this one Actions menu.
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

    // Phase 35A (replaces the Phase 23C banner-invariance test, the banner is gone): the sidebar follows a live
    // appearance switch in both states, with no brush left from the other theme.
    [Fact]
    public void Appearance_Sidebar_FollowsALiveSwitch_ExpandedAndCollapsed()
    {
        using var harness = new AppearanceHarness();
        var viewModel = CreateAppearanceMainViewModel(harness.Controller);
        var window = ShowMainWindow(viewModel, 1280, 860);
        var navigation = Named(window, "AppNavigation");

        foreach (var (preference, shown) in new[] { (Application.Preferences.AppearancePreference.Dark, ThemeVariant.Dark), (Application.Preferences.AppearancePreference.Light, ThemeVariant.Light) })
        {
            harness.Controller.SetPreferenceAsync(preference);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            foreach (var collapsed in new[] { false, true, false })
            {
                if (viewModel.IsNavigationCollapsed != collapsed)
                {
                    viewModel.ToggleNavigationCommand.Execute(null);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                }

                Assert.Equal(shown, window.ActualThemeVariant);
                Assert.Empty(WrongThemeBrushes(shown, navigation));
            }
        }

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

        // Phase 35F: side by side (as in the approved prototype) in the stated order, never overlapping.
        Assert.True(AbsoluteBounds(options[0], window).Right <= AbsoluteBounds(options[1], window).Left + 0.5);
        Assert.True(AbsoluteBounds(options[1], window).Right <= AbsoluteBounds(options[2], window).Left + 0.5);

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

    // ==================================================================================
    // Phase 35A — sidebar collapse/expand, narrow-window compaction, resize and interface motion on real
    // windows. Motion tests inject an InterfaceMotionController with fakes; nothing reads the real profile.
    // ==================================================================================

    private static MainViewModel CreateShellMainViewModel(Phase35AShellStateTests.FakeSidebarStore? sidebar = null, Desktop.Services.InterfaceMotionController? motion = null)
    {
        var baseline = CreateMainViewModel();
        var settings = new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator(),
            appearance: new AppearanceSettingsViewModel(motion: motion));
        return new MainViewModel(baseline.Dashboard, baseline.Calendar, baseline.Associate, baseline.Notes, baseline.History, settings, baseline.PriceHistory, whatsNew: null, sidebarStore: sidebar ?? new Phase35AShellStateTests.FakeSidebarStore());
    }

    private static Avalonia.Controls.Presenters.ContentPresenter TemplatePresenter(Button button) =>
        button.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");

    [Fact]
    public void Sidebar_CollapseAndExpand_ChangesOnlyTheSidebar_KeepingNamesTooltipsAndPages()
    {
        var viewModel = CreateShellMainViewModel();
        var window = ShowMainWindow(viewModel, 1536, 960);
        var navigation = Named(window, "AppNavigation");
        var hosts = window.GetVisualDescendants().OfType<Border>().Where(b => b.GetVisualParent() is Grid { Name: "SectionHost" }).ToList();
        var pages = hosts.Select(h => h.Child).ToList();
        var names = SectionButtons(window).Select(b => AutomationProperties.GetName(b)).ToList();

        Assert.Equal(208, navigation.Bounds.Width, 1);
        Assert.All(SectionButtons(window), b => Assert.True(NavLabel(b).IsEffectivelyVisible));
        Assert.All(SectionButtons(window), b => Assert.False(ToolTip.GetServiceEnabled(b)));

        viewModel.ToggleNavigationCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(56, navigation.Bounds.Width, 1);
        foreach (var item in SectionButtons(window))
        {
            Assert.False(NavLabel(item).IsEffectivelyVisible, "collapsed labels leave the layout");
            var icon = item.GetVisualDescendants().OfType<PathIcon>().First();
            var bounds = AbsoluteBounds(icon, window);
            Assert.True(icon.IsEffectivelyVisible && bounds.Width > 0);
            Assert.True(bounds.Left >= 0 && bounds.Right <= 56, $"icon of '{AutomationProperties.GetName(item)}' clipped in the collapsed sidebar");
            Assert.Equal(28, (bounds.Left + bounds.Right) / 2, 1.5); // icons centred
            Assert.True(ToolTip.GetServiceEnabled(item), "collapsed items show their label as a tooltip");
            Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(item) as string));
        }

        Assert.Equal(names, SectionButtons(window).Select(b => AutomationProperties.GetName(b)));
        Assert.Equal(pages, hosts.Select(h => h.Child)); // the same page instances — nothing recreated
        Assert.Equal(56, AbsoluteBounds(hosts.Single(h => h.IsVisible), window).Left, 1);

        viewModel.ToggleNavigationCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(208, navigation.Bounds.Width, 1);
        Assert.All(SectionButtons(window), b => Assert.True(NavLabel(b).IsEffectivelyVisible));
        window.Close();
    }

    [Fact]
    public void Sidebar_TogglingFromTheKeyboard_KeepsFocusOnTheToggle_AndAnnouncesItsAction()
    {
        var viewModel = CreateShellMainViewModel();
        var window = ShowMainWindow(viewModel, 1280, 860);
        var toggle = (Button)Named(window, "NavigationToggle");
        Assert.Equal("Collapse navigation", AutomationProperties.GetName(toggle));

        Assert.True(toggle.Focus(NavigationMethod.Tab));
        foreach (var expectedName in new[] { "Expand navigation", "Collapse navigation", "Expand navigation" })
        {
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Same(toggle, window.FocusManager!.GetFocusedElement());
            Assert.Equal(expectedName, AutomationProperties.GetName(toggle));
            Assert.Equal(expectedName, ToolTip.GetTip(toggle));
        }

        Assert.Equal(AppSection.Dashboard, viewModel.SelectedSection);
        window.Close();
    }

    [Fact]
    public void Sidebar_FocusedDestination_KeepsFocusWhenTheSidebarCollapses()
    {
        var viewModel = CreateShellMainViewModel();
        var window = ShowMainWindow(viewModel, 1280, 860);
        var history = SectionButtons(window)[4];
        Assert.True(history.Focus(NavigationMethod.Tab));

        viewModel.ToggleNavigationCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Same(history, window.FocusManager!.GetFocusedElement());
        Assert.True(history.IsEffectivelyVisible);
        window.Close();
    }

    [Fact]
    public void Sidebar_NarrowWindow_ShowsIconsOnly_WithoutChangingTheSavedChoice()
    {
        var store = new Phase35AShellStateTests.FakeSidebarStore();
        var viewModel = CreateShellMainViewModel(store);
        var window = ShowMainWindow(viewModel, 960, 640);
        var navigation = Named(window, "AppNavigation");
        var toggle = (Button)Named(window, "NavigationToggle");

        Assert.Equal(56, navigation.Bounds.Width, 1);
        Assert.False(toggle.IsEffectivelyEnabled);
        Assert.Equal("Navigation labels need a wider window", AutomationProperties.GetName(toggle));
        Assert.False(viewModel.IsNavigationCollapsed);

        window.Width = 1280;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(208, navigation.Bounds.Width, 1);
        Assert.True(toggle.IsEffectivelyEnabled);

        window.Width = 1000;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(56, navigation.Bounds.Width, 1);
        Assert.Empty(store.Saves); // resizing never writes the preference
        window.Close();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sidebar_ResizingAcrossDesktopSizes_NeverClipsAnyPageHorizontally(bool collapsed)
    {
        var viewModel = CreateShellMainViewModel(new Phase35AShellStateTests.FakeSidebarStore { Stored = collapsed ? Application.Preferences.NavigationSidebarState.Collapsed : Application.Preferences.NavigationSidebarState.Expanded });
        var window = ShowMainWindow(viewModel, 1920, 1080);

        foreach (var (width, height) in new[] { (1920d, 1080d), (960d, 640d), (1536d, 960d), (1280d, 720d), (1100d, 700d) })
        {
            window.Width = width;
            window.Height = height;
            foreach (var section in Enum.GetValues<AppSection>())
            {
                viewModel.SelectedSection = section;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                // Only what is showing: hidden pages keep the bounds of their last (wider) layout.
                foreach (var control in window.GetVisualDescendants().OfType<Control>().Where(c => c is Button or TextBox && c.IsEffectivelyVisible && c.Bounds.Width > 0))
                {
                    Assert.True(AbsoluteBounds(control, window).Right <= window.ClientSize.Width + 1.0,
                        $"{section} at {width}x{height}: '{AutomationProperties.GetName(control)}' exceeds the window");
                }

                var host = window.GetVisualDescendants().OfType<Border>().Single(b => b.IsVisible && b.GetVisualParent() is Grid { Name: "SectionHost" });
                Assert.True(AbsoluteBounds(host, window).Right <= window.ClientSize.Width + 1, $"{section} at {width}: page wider than the window");
            }
        }

        window.Close();
    }

    [Fact]
    public void Motion_Off_LeavesNoTransitionInTheShell_IncludingFluentsButtonPress()
    {
        var (controller, _, _) = Phase35AInterfaceMotionTests.Create(stored: Application.Preferences.InterfaceAnimationPreference.Off);
        var window = ShowMainWindow(CreateShellMainViewModel(motion: controller), 1280, 860);

        Assert.Contains("noMotion", window.Classes);
        Assert.DoesNotContain("motion", window.Classes);
        foreach (var item in SectionButtons(window))
        {
            Assert.True(NavLabel(item).Transitions is null || NavLabel(item).Transitions!.Count == 0);
            Assert.True(TemplatePresenter(item).Transitions is null || TemplatePresenter(item).Transitions!.Count == 0);
        }

        Assert.All(window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible),
            b => Assert.True(b.Transitions is null || b.Transitions.Count == 0, $"'{AutomationProperties.GetName(b)}' still animates with motion off"));
        window.Close();
    }

    [Fact]
    public void Motion_On_UsesShortCosmeticTransitions_AndTurningItOffRemovesThemLive()
    {
        var (controller, _, _) = Phase35AInterfaceMotionTests.Create();
        var viewModel = CreateShellMainViewModel(motion: controller);
        var window = ShowMainWindow(viewModel, 1280, 860);
        var item = SectionButtons(window)[1];

        Assert.Contains("motion", window.Classes);
        Assert.DoesNotContain("noMotion", window.Classes);
        var label = Assert.IsType<Avalonia.Animation.DoubleTransition>(Assert.Single(NavLabel(item).Transitions!));
        Assert.Equal(Visual.OpacityProperty, label.Property);
        Assert.InRange(label.Duration.TotalMilliseconds, 100, 180);
        var hover = Assert.IsType<Avalonia.Animation.BrushTransition>(Assert.Single(TemplatePresenter(item).Transitions!));
        Assert.InRange(hover.Duration.TotalMilliseconds, 80, 140);

        viewModel.Settings.Appearance.IsAnimationsOffSelected = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain("motion", window.Classes);
        Assert.True(NavLabel(item).Transitions is null || NavLabel(item).Transitions!.Count == 0);
        Assert.True(TemplatePresenter(item).Transitions is null || TemplatePresenter(item).Transitions!.Count == 0);
        window.Close();
    }

    [Fact]
    public void Motion_WindowsReducedMotion_DisablesTransitions_AndRemovingItRestoresThemOnActivation()
    {
        var (controller, store, os) = Phase35AInterfaceMotionTests.Create(os: Application.Preferences.SystemReducedMotion.Requested);
        var viewModel = CreateShellMainViewModel(motion: controller);
        var window = ShowMainWindow(viewModel, 1280, 860);

        Assert.Contains("noMotion", window.Classes);
        Assert.True(viewModel.Settings.Appearance.IsAnimationsOnSelected); // the saved choice is untouched

        os.Value = Application.Preferences.SystemReducedMotion.NotRequested;
        viewModel.OnWindowActivated();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Contains("motion", window.Classes);
        Assert.Empty(store.Saves);
        window.Close();
    }

    [Fact]
    public void Motion_On_StateChangesTakeEffectImmediately_NothingWaitsForATransition()
    {
        var (controller, _, _) = Phase35AInterfaceMotionTests.Create();
        var viewModel = CreateShellMainViewModel(motion: controller);
        var window = ShowMainWindow(viewModel, 1280, 860);
        var navigation = Named(window, "AppNavigation");

        viewModel.ToggleNavigationCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(56, navigation.Bounds.Width, 1); // width snaps; no layout animation
        Assert.All(SectionButtons(window), b => Assert.False(NavLabel(b).IsEffectivelyVisible));

        viewModel.ShowHistorySectionCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Contains("active", SectionButtons(window)[4].Classes);
        Assert.True(window.GetVisualDescendants().OfType<HistoryView>().Single().IsEffectivelyVisible);
        window.Close();
    }

    [Fact]
    public void Motion_ReducedMotion_DuringRapidNavigation_EndsOnTheLastSection_WithOneActiveItem()
    {
        var (controller, _, _) = Phase35AInterfaceMotionTests.Create(os: Application.Preferences.SystemReducedMotion.Requested);
        var viewModel = CreateShellMainViewModel(motion: controller);
        var window = ShowMainWindow(viewModel, 1280, 860);
        var buttons = SectionButtons(window);

        for (var i = 0; i < 60; i++)
        {
            buttons[i % buttons.Count].Command!.Execute(null);
            if (i % 5 == 0)
            {
                viewModel.ToggleNavigationCommand.Execute(null);
            }

            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Single(SectionButtons(window), b => b.Classes.Contains("active"));
        }

        Assert.Equal(AppSection.Settings, viewModel.SelectedSection); // 59 % 6 == 5
        Assert.Contains("noMotion", window.Classes);
        window.Close();
    }

    [Theory]
    [InlineData(1280d, 860d)]
    [InlineData(960d, 640d)]
    public void Settings_InterfaceAnimations_KeyboardOperable_AppliesLive_AndExplainsTheWindowsOverride(double width, double height)
    {
        var (controller, store, os) = Phase35AInterfaceMotionTests.Create();
        var viewModel = CreateShellMainViewModel(motion: controller);
        var window = ShowMainWindow(viewModel, width, height);
        viewModel.SelectedSection = AppSection.Settings;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Phase 35F: one switch (35A had an On / Off radio group) bound to the same saved choice.
        var toggle = window.GetVisualDescendants().OfType<ToggleSwitch>().Single(r => r.Name == "InterfaceAnimationsSwitch");
        var note = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "InterfaceAnimationsSystemNote");
        Assert.True(toggle.IsChecked);
        Assert.True(toggle.IsEffectivelyVisible && AbsoluteBounds(toggle, window).Right <= window.ClientSize.Width);
        Assert.False(note.IsEffectivelyVisible); // Windows allows animation: nothing to explain
        Assert.Contains("motion", window.Classes);

        // Space on the focused switch turns it Off, applies at once and saves locally.
        Assert.True(toggle.Focus(NavigationMethod.Tab));
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.Settings.Appearance.PendingMotionSave.IsCompletedSuccessfully); // the fake store saves synchronously
        Assert.False(toggle.IsChecked);
        Assert.DoesNotContain("motion", window.Classes);
        Assert.Equal(Application.Preferences.InterfaceAnimationPreference.Off, store.Stored);

        // Back On, then Windows turns Animation effects off: motion off, choice kept, the note explains it.
        toggle.IsChecked = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(Application.Preferences.InterfaceAnimationPreference.On, store.Stored);
        os.Value = Application.Preferences.SystemReducedMotion.Requested;
        viewModel.OnWindowActivated();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(toggle.IsChecked);
        Assert.True(note.IsEffectivelyVisible);
        Assert.Equal(AppearanceSettingsViewModel.MotionOverriddenByWindowsText, note.Text);
        Assert.Contains("noMotion", window.Classes);
        Assert.Equal(AppSection.Settings, viewModel.SelectedSection);
        window.Close();
    }

    // ==================================================================================
    // Phase 35F — Settings section list, Operational Notes editor dialog
    // ==================================================================================

    [Theory]
    [InlineData(1280d, 860d)]
    [InlineData(960d, 640d)]
    public async Task Settings_SectionList_ScrollsTheGroupToTheTop_AndMarksItCurrent_WithoutChangingAnything(double width, double height)
    {
        var viewModel = CreateBusySettings();
        await viewModel.PendingLoad;
        var window = RealizeInWindow(new PersonalSyncSettingsView { DataContext = viewModel }, width, height);
        var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "SettingsScroll");
        Button Entry(string name) => window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == name);
        var enabledBefore = viewModel.PersonalSyncEnabled;

        Assert.Contains("active", Entry("Go to Appearance settings").Classes);
        Assert.Equal("Current section", AutomationProperties.GetItemStatus(Entry("Go to Appearance settings")));

        Entry("Go to Personal Sync settings").Focus(NavigationMethod.Tab);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var section = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PersonalSyncSection");
        var top = AbsoluteBounds(section, window).Top - AbsoluteBounds(scroll, window).Top;
        Assert.True(scroll.Offset.Y > 0, "the page scrolled");
        Assert.InRange(top, 0, 40); // the group starts at the top of the view (the page padding above it)
        Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("settingsNavItem") && b.Classes.Contains("active"));
        Assert.Contains("active", Entry("Go to Personal Sync settings").Classes);
        Assert.True(Entry("Go to Personal Sync settings").IsFocused); // focus stays on the list
        Assert.Equal(enabledBefore, viewModel.PersonalSyncEnabled);

        Entry("Go to About this build").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(scroll.Extent.Height - scroll.Viewport.Height, scroll.Offset.Y, 1.0); // short last group: the page end
        Assert.Contains("active", Entry("Go to About this build").Classes);

        AssertNoControlClipsHorizontally(window);
        window.Close();
    }

    [Fact]
    public async Task OperationalNotes_EditorDialog_FocusesTheTitle_EscapeCancels_AndFocusReturnsToNewNote()
    {
        var service = new FakeOperationalNotesService();
        var viewModel = new OperationalNotesViewModel(service, CreateClock());
        await viewModel.PendingLoad;
        var window = RealizeInWindow(new OperationalNotesView { DataContext = viewModel }, 1280, 860);
        var newNote = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NewNoteButton");

        newNote.Focus(NavigationMethod.Tab);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(viewModel.IsEditing);
        var title = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "EditorTitleBox");
        Assert.True(title.IsFocused, "the dialog opens on its Title box");
        var page = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "NotesPage");
        Assert.False(page.IsEffectivelyEnabled); // nothing behind the dialog can be used
        var dialog = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NoteEditorDialog");
        Assert.True(AbsoluteBounds(dialog, window).Right <= window.ClientSize.Width);

        title.Text = "Typed but cancelled";
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(viewModel.IsEditing);
        Assert.Empty(await service.ListAsync()); // nothing was saved
        Assert.True(newNote.IsFocused, "focus returns to New Note");
        window.Close();
    }
}
