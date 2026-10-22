using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Carowinds.Passdown.Application.Diagnostics;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Desktop;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Ui.Tests.TestSupport;

// Phase 22F synthetic-fixture screenshot harness. Usage: README.md.
// args: <outDir> [label] [scenario substring filter] [sizes: all|main] [appearance: forced|system|all]
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();

// Phase 23C: every theme goes through the real AppearanceThemeController and ApplicationThemeVariantTarget,
// with an in-memory preference store and a simulated operating-system appearance — never the real
// profile's appearance.json and never the test machine's own light/dark setting.
var appearanceStore = new FakeAppearancePreferenceStore();
var simulatedOs = new FakeSystemAppearanceSource();
var appearance = new AppearanceThemeController(appearanceStore, simulatedOs, new ApplicationThemeVariantTarget());
appearance.Initialize();
Fixture.Appearance = appearance;

var outDir = args.Length > 0 ? args[0] : "visual-fixtures-out";
var label = args.Length > 1 ? args[1] : "shot";
var filter = args.Length > 2 && args[2] != "*" ? args[2] : null;
var sizes = args.Length > 3 && args[3] == "main"
    ? new (int W, int H)[] { (1280, 860) }
    : new (int W, int H)[] { (960, 640), (1280, 690), (1280, 860), (1920, 1000) };
// Forced modes run against the opposite simulated OS, proving the preference (not the OS) decides.
var appearanceModes = (args.Length > 4 ? args[4] : "forced") switch
{
    "system" => new[] { ("SystemOsLight", AppearancePreference.System, EffectiveAppearance.Light), ("SystemOsDark", AppearancePreference.System, EffectiveAppearance.Dark) },
    "all" => new[]
    {
        ("Light", AppearancePreference.Light, EffectiveAppearance.Dark), ("Dark", AppearancePreference.Dark, EffectiveAppearance.Light),
        ("SystemOsLight", AppearancePreference.System, EffectiveAppearance.Light), ("SystemOsDark", AppearancePreference.System, EffectiveAppearance.Dark),
    },
    _ => new[] { ("Light", AppearancePreference.Light, EffectiveAppearance.Dark), ("Dark", AppearancePreference.Dark, EffectiveAppearance.Light) },
};
Directory.CreateDirectory(outDir);
var report = new List<string>();

var scenarios = new List<(string Name, Func<Fixture> Create, Action<Fixture> Arrange)>
{
    ("Dashboard", () => new Fixture(), f => Go(f, AppSection.Dashboard)),
    // Phase 23B: Today's Shows density/overlap. "-Top" keeps the page at the top (above-the-fold view);
    // the others scroll the Shows panel into view.
    ("DashboardShows-TypicalTop", () => new Fixture(dashboardShows: Phase23BDashboardShowsFixtures.Scheduled(Phase23BDashboardShowsFixtures.Typical)), f => Go(f, AppSection.Dashboard)),
    ("DashboardShows-BusyTop", () => new Fixture(dashboardShows: Phase23BDashboardShowsFixtures.Scheduled(Phase23BDashboardShowsFixtures.Busy)), f => Go(f, AppSection.Dashboard)),
    ("DashboardShows-Typical", () => new Fixture(dashboardShows: Phase23BDashboardShowsFixtures.Scheduled(Phase23BDashboardShowsFixtures.Typical)), f => { Go(f, AppSection.Dashboard); ScrollToName(f.Window, "Today's shows"); }),
    ("DashboardShows-Busy", () => new Fixture(dashboardShows: Phase23BDashboardShowsFixtures.Scheduled(Phase23BDashboardShowsFixtures.Busy)), f => { Go(f, AppSection.Dashboard); ScrollToName(f.Window, "Today's shows"); }),
    ("DashboardShows-BusyExpanded", () => new Fixture(dashboardShows: Phase23BDashboardShowsFixtures.Scheduled(Phase23BDashboardShowsFixtures.Busy)), f =>
    {
        Go(f, AppSection.Dashboard);
        ExpandFirstShowTimes(f.Window);
        ScrollToName(f.Window, "Today's shows");
    }),
    ("DashboardShows-One", () => new Fixture(dashboardShows: Phase23BDashboardShowsFixtures.Scheduled(Phase23BDashboardShowsFixtures.Busy[0])), f => { Go(f, AppSection.Dashboard); ScrollToName(f.Window, "Today's shows"); }),
    ("DashboardShows-NoShows", () => new Fixture(dashboardShows: Phase23BDashboardShowsFixtures.Empty(Carowinds.Passdown.Application.Shows.ShowScheduleState.NoShows)), f => { Go(f, AppSection.Dashboard); ScrollToName(f.Window, "Today's shows"); }),
    ("DashboardShows-NotPublished", () => new Fixture(dashboardShows: Phase23BDashboardShowsFixtures.Empty(Carowinds.Passdown.Application.Shows.ShowScheduleState.NotPublished)), f => { Go(f, AppSection.Dashboard); ScrollToName(f.Window, "Today's shows"); }),
    ("DashboardShows-Stale", () => new Fixture(dashboardShows: Phase23BDashboardShowsFixtures.Stale()), f => { Go(f, AppSection.Dashboard); ScrollToName(f.Window, "Today's shows"); }),
    ("DashboardShows-FailedLkg", () => new Fixture(dashboardShows: Phase23BDashboardShowsFixtures.FailedWithLastKnownGood()), f => { Go(f, AppSection.Dashboard); ScrollToName(f.Window, "Today's shows"); }),
    ("Calendar-Busy", () => new Fixture(), f => { Go(f, AppSection.OperationsCalendar); Select(f, Fixtures.Busy); }),
    ("Calendar-BusyExpanded", () => new Fixture(), f => { Go(f, AppSection.OperationsCalendar); Select(f, Fixtures.Busy); f.Vm.Calendar.IsShowScheduleExpanded = true; Pump(6); }),
    ("Calendar-WinterFest", () => new Fixture(), f => { Go(f, AppSection.OperationsCalendar); Select(f, Fixtures.WinterFest); }),
    ("Calendar-NewYearsEve", () => new Fixture(), f => { Go(f, AppSection.OperationsCalendar); Select(f, Fixtures.NewYearsEve); }),
    ("Calendar-Historical", () => new Fixture(), f => { Go(f, AppSection.OperationsCalendar); Select(f, Fixtures.Historical); }),
    ("Calendar-NotPublished", () => new Fixture(), f => { Go(f, AppSection.OperationsCalendar); Select(f, Fixtures.FutureNotPublished); }),
    ("Calendar-Stale", () => new Fixture(), f => { Go(f, AppSection.OperationsCalendar); Select(f, Fixtures.Stale); }),
    ("Calendar-ShowCheckFailed", () => new Fixture(), f => { Go(f, AppSection.OperationsCalendar); Select(f, Fixtures.Error); }),
    // Phase 22F: a date change held mid-load — the new heading with no previous-date details under it.
    ("Calendar-SwitchingDates", () => new Fixture(), f =>
    {
        Go(f, AppSection.OperationsCalendar); Select(f, Fixtures.Busy);
        f.CalendarService.Gate = new TaskCompletionSource<bool>();
        f.Vm.Calendar.SelectDateCommand.Execute(Fixtures.Other); Pump(8);
    }),
    ("Calendar-LoadFailed", () => new Fixture(), f =>
    {
        Go(f, AppSection.OperationsCalendar); Select(f, Fixtures.Busy);
        f.CalendarService.Exception = new InvalidOperationException("synthetic local read failure");
        Select(f, Fixtures.Other);
    }),
    ("Associate-Overview", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowOverviewSectionCommand.Execute(null); Pump(8); }),
    ("Associate-Products", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowProductsSectionCommand.Execute(null); Pump(8); }),
    // Phase 25: the Season Passes and Memberships groups (scrolled into view).
    ("Associate-Memberships", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowProductsSectionCommand.Execute(null); Pump(8); ScrollToEnd(f.Window); }),
    ("Associate-Notes", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowNotesSectionCommand.Execute(null); Pump(8); }),
    ("Associate-Shows", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowNotesSectionCommand.Execute(null); Pump(8); ScrollToName(f.Window, "Shows"); }),
    ("Associate-SaveFailed", () => new Fixture(), f =>
    {
        Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowOverviewSectionCommand.Execute(null); Pump(8);
        f.AssociateService.SaveException = new IOException("synthetic disk write failure");
        f.Vm.Associate.ProjectedAttendanceText = "39,000"; Pump(4);
        Pump(6); f.Vm.Associate.PendingAutosave.Wait(2000); Pump(8);
    }),
    ("Associate-PriceHistory", () => new Fixture(), f =>
    {
        Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowProductsSectionCommand.Execute(null); Pump(8);
        f.Vm.PriceHistory!.OpenAsync(Fixtures.GoldPassKey).Wait(2000); Pump(10);
    }),
    ("Popup-DatePicker", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); OpenFlyout(f.Window, "Operating date"); }),
    ("Popup-Copy", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); OpenFlyout(f.Window, "Copy for Teams"); }),
    ("Popup-More", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); OpenFlyout(f.Window, "More passdown actions"); }),
    ("Popup-Math", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowOverviewSectionCommand.Execute(null); Pump(6); OpenFlyout(f.Window, "Insert math symbol or equation"); }),
    ("Notes", () => new Fixture(), f =>
    {
        Go(f, AppSection.OperationalNotes);
        f.Vm.Notes.SetFilterCommand.Execute(OperationalNoteLifecycleFilter.All); Pump(6);
        if (f.Vm.Notes.Notes.Count > 0) { f.Vm.Notes.SelectedNote = f.Vm.Notes.Notes[0]; Pump(6); }
    }),
    ("Notes-Empty", () => new Fixture(emptyNotes: true), f => Go(f, AppSection.OperationalNotes)),
    ("History", () => new Fixture(), f =>
    {
        Go(f, AppSection.History);
        if (f.Vm.History.HistoryItems.Count > 0) { f.Vm.History.SelectedItem = f.Vm.History.HistoryItems[0]; Pump(10); }
    }),
    ("History-Empty", () => new Fixture(emptyHistory: true), f => Go(f, AppSection.History)),
    ("Settings-Off", () => new Fixture(), f => Go(f, AppSection.Settings)),
    ("Settings-Syncing", () => new Fixture(sync: SyncFixture.UpToDate), f => Go(f, AppSection.Settings)),
    ("Settings-Conflict", () => new Fixture(sync: SyncFixture.Conflict), f => { Go(f, AppSection.Settings); f.Vm.Settings.ReviewConflictsCommand.Execute(null); Pump(8); }),
    ("Settings-Error", () => new Fixture(sync: SyncFixture.Error), f =>
    {
        Go(f, AppSection.Settings);
        f.Vm.Settings.Diagnostics.ExportDiagnosticsCommand.ExecuteAsync(null).Wait(2000); Pump(6);
        ScrollToEnd(f.Window);
    }),
    // Phase 23C: the Appearance selector itself, and live theme switches with screens and popups already open.
    // Each LiveSwitch-* capture is taken AFTER flipping to the opposite theme of its file-name mode.
    ("Appearance-Settings", () => new Fixture(), f => Go(f, AppSection.Settings)),
    ("LiveSwitch-Settings", () => new Fixture(), f => { Go(f, AppSection.Settings); CheckAppearance(f.Window, Opposite()); }),
    ("Appearance-SaveFailed", () => new Fixture(), f =>
    {
        Go(f, AppSection.Settings);
        appearanceStore.SaveException = new IOException("synthetic read-only profile");
        CheckAppearance(f.Window, Opposite()); Pump(6);
    }),
    ("LiveSwitch-Dashboard", () => new Fixture(dashboardShows: Phase23BDashboardShowsFixtures.Scheduled(Phase23BDashboardShowsFixtures.Busy)), f =>
    {
        Go(f, AppSection.Dashboard); ExpandFirstShowTimes(f.Window); ScrollToName(f.Window, "Today's shows"); Flip();
    }),
    ("LiveSwitch-Calendar", () => new Fixture(), f => { Go(f, AppSection.OperationsCalendar); Select(f, Fixtures.Busy); f.Vm.Calendar.IsShowScheduleExpanded = true; Pump(6); Flip(); }),
    ("LiveSwitch-Products", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowProductsSectionCommand.Execute(null); Pump(8); Flip(); }),
    ("LiveSwitch-PriceHistory", () => new Fixture(), f =>
    {
        Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowProductsSectionCommand.Execute(null); Pump(8);
        f.Vm.PriceHistory!.OpenAsync(Fixtures.GoldPassKey).Wait(2000); Pump(10); Flip();
    }),
    ("LiveSwitch-DatePicker", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); OpenFlyout(f.Window, "Operating date"); Flip(); }),
    ("LiveSwitch-Copy", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); OpenFlyout(f.Window, "Copy for Teams"); Flip(); }),
    ("LiveSwitch-More", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); OpenFlyout(f.Window, "More passdown actions"); Flip(); }),
    ("LiveSwitch-Math", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowOverviewSectionCommand.Execute(null); Pump(6); OpenFlyout(f.Window, "Insert math symbol or equation"); Flip(); }),
    ("LiveSwitch-SystemOs", () => new Fixture(), f =>
    {
        // System mode following a simulated OS switch while Settings is open (forced modes: no change expected).
        Go(f, AppSection.Settings);
        simulatedOs.Switch(simulatedOs.Current == EffectiveAppearance.Dark ? EffectiveAppearance.Light : EffectiveAppearance.Dark); Pump(8);
    }),
};

// Phase 23C helpers: the forced option opposite to what is shown now, applied through the controller
// (Flip) or by checking that radio button in Settings, exactly as a user would (CheckAppearance).
string Opposite() => appearance.EffectiveAppearance == EffectiveAppearance.Dark ? "Light" : "Dark";

void Flip()
{
    _ = appearance.SetPreferenceAsync(appearance.EffectiveAppearance == EffectiveAppearance.Dark ? AppearancePreference.Light : AppearancePreference.Dark);
    Pump(10);
}

static void CheckAppearance(Window window, string option)
{
    var radio = window.GetVisualDescendants().OfType<RadioButton>().FirstOrDefault(r => AutomationProperties.GetName(r) == option);
    if (radio is not null)
    {
        radio.IsChecked = true;
        Pump(10);
    }
}

foreach (var (w, h) in sizes)
{
    foreach (var (theme, preference, os) in appearanceModes)
    {
        foreach (var (name, create, arrange) in scenarios)
        {
            if (filter is not null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Reset before every scenario: LiveSwitch-* scenarios change the appearance mid-arrange.
            appearanceStore.SaveException = null;
            simulatedOs.Switch(os);
            _ = appearance.SetPreferenceAsync(preference);
            Pump(2);

            var fixture = create();
            fixture.Window.Width = w;
            fixture.Window.Height = h;
            fixture.Window.Show();
            Pump(10);
            arrange(fixture);
            Pump(6);
            var shot = $"{name}-{theme}-{w}x{h}";
            Shot(fixture.Window, shot);
            report.Add($"{shot}: {Geometry(fixture.Window)}");
            fixture.CalendarService.Gate?.TrySetResult(true);
            fixture.Window.Close();
            Pump(2);
        }
    }
}

File.WriteAllLines(Path.Combine(outDir, $"{label}-geometry.txt"), report);
Console.WriteLine(string.Join(Environment.NewLine, report));

void Shot(Window window, string name)
{
    using var frame = window.CaptureRenderedFrame();
    frame?.Save(Path.Combine(outDir, $"{label}-{name}.png"));
}

static void Go(Fixture f, AppSection section)
{
    f.Vm.SelectedSection = section;
    Pump(10);
}

static void Select(Fixture f, DateOnly date)
{
    f.Vm.Calendar.SelectDateCommand.Execute(date);
    Pump(4);
    f.Vm.Calendar.PendingLoad.Wait(2000);
    Pump(10);
}

static void OpenFlyout(Window window, string automationName)
{
    var button = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == automationName);
    button?.Flyout?.ShowAt(button);
    Pump(12);
}

static void ScrollToName(Window window, string automationName)
{
    var target = window.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == automationName);
    target?.BringIntoView();
    Pump(8);
}

// Phase 23B: activates the first "+N more" performance-times toggle in Today's Shows (a no-op on layouts without one).
static void ExpandFirstShowTimes(Window window)
{
    var toggle = window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>()
        .FirstOrDefault(t => t.IsEffectivelyVisible && (AutomationProperties.GetName(t) ?? string.Empty).StartsWith("Show all ", StringComparison.Ordinal));
    if (toggle is not null)
    {
        toggle.IsChecked = true;
        Pump(8);
    }
}

static void ScrollToEnd(Window window)
{
    var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.IsEffectivelyVisible).OrderByDescending(s => s.Extent.Height).FirstOrDefault();
    if (scroll is not null)
    {
        scroll.Offset = new Vector(0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
        Pump(8);
    }
}

// Clipping / overflow evidence that pixels alone can hide: horizontal overflow of the visible page and any
// visible button whose text is cut by its own bounds or by the window's right edge.
static string Geometry(Window window)
{
    var scrolls = window.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.IsEffectivelyVisible).ToList();
    var horizontal = scrolls.Where(s => s.Extent.Width > s.Viewport.Width + 1).Select(s => $"{s.Extent.Width - s.Viewport.Width:F0}").ToList();
    var vertical = scrolls.OrderByDescending(s => s.Extent.Height).FirstOrDefault() is { } main ? Math.Max(0, main.Extent.Height - main.Viewport.Height) : 0;
    var clipped = window.GetVisualDescendants().OfType<Button>()
        .Where(b => b.IsEffectivelyVisible && b.Bounds.Width > 0)
        .Where(b => b.TranslatePoint(new Point(b.Bounds.Width, 0), window) is { } right && right.X > window.Bounds.Width + 0.5)
        .Select(b => AutomationProperties.GetName(b) ?? b.Content?.ToString() ?? "?").ToList();
    return $"pageScroll={vertical:F0} horizontalOverflow=[{string.Join(",", horizontal)}] buttonsPastRightEdge=[{string.Join(",", clipped)}]{ShowsGeometry(window)}";
}

// Phase 23B: measured Today's Shows layout — panel top/height (page coordinates, independent of scroll), where
// "Today at a Glance" starts, the space each row gives names/times/venues, and how many text/button rectangles
// inside the panel intersect (the reported defect: times running under the venue).
static string ShowsGeometry(Window window)
{
    var panel = window.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == "Today's shows");
    if (panel is null)
    {
        return string.Empty;
    }

    var glance = window.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == "Today at a Glance");
    var scroll = panel.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
    var offset = scroll?.Offset.Y ?? 0;
    double PageY(Visual v) => (v.TranslatePoint(new Point(0, 0), window)?.Y ?? 0) + offset;

    var rects = panel.GetVisualDescendants().OfType<Control>()
        .Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0 && (c is TextBlock || c is Button || c is Avalonia.Controls.Primitives.ToggleButton))
        .Where(c => c is not TextBlock || c.GetVisualAncestors().OfType<Button>().FirstOrDefault() is null)
        .Select(c => (Control: c, Rect: new Rect(c.TranslatePoint(new Point(0, 0), window) ?? default, c.Bounds.Size)))
        .ToList();
    var overlaps = new List<string>();
    for (var i = 0; i < rects.Count; i++)
    {
        for (var j = i + 1; j < rects.Count; j++)
        {
            if (rects[i].Control.GetVisualAncestors().Contains(rects[j].Control) || rects[j].Control.GetVisualAncestors().Contains(rects[i].Control))
            {
                continue;
            }

            var hit = rects[i].Rect.Intersect(rects[j].Rect);
            if (hit.Width > 1 && hit.Height > 1)
            {
                overlaps.Add($"{Label(rects[i].Control)}|{Label(rects[j].Control)}");
            }
        }
    }

    var rowGrid = panel.GetVisualDescendants().OfType<Grid>().FirstOrDefault(g => g.ColumnDefinitions.Count == 3);
    var columns = rowGrid is null ? string.Empty : $" rowColumns=[{string.Join(",", rowGrid.ColumnDefinitions.Select(c => c.ActualWidth.ToString("F0")))}]";
    var widest = rects.Where(r => r.Control is TextBlock).Select(r => r.Rect.Right).DefaultIfEmpty(0).Max();
    var panelRight = panel.TranslatePoint(new Point(panel.Bounds.Width, 0), window)?.X ?? 0;
    var cells = panel.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("dashboardShowRow") && b.IsEffectivelyVisible).ToList();
    var cellInfo = cells.Count == 0 ? string.Empty : $" showCells={cells.Count} cellWidth={cells[0].Bounds.Width:F0} columns={cells.Select(c => Math.Round(c.TranslatePoint(new Point(0, 0), window)?.X ?? 0)).Distinct().Count()}";
    return $" | showsTop={PageY(panel):F0} showsHeight={panel.Bounds.Height:F0} glanceTop={(glance is null ? "-" : PageY(glance).ToString("F0"))} viewport={scroll?.Viewport.Height ?? 0:F0}"
        + $" panelContentWidth={panel.Bounds.Width - panel.Padding.Left - panel.Padding.Right:F0}{columns}{cellInfo} textPastPanel={(widest > panelRight + 0.5 ? "yes" : "no")}"
        + $" overlaps={overlaps.Count}{(overlaps.Count > 0 ? " [" + string.Join("; ", overlaps.Take(4)) + "]" : string.Empty)}";

    static string Label(Control c) => c switch
    {
        TextBlock t => $"\"{Shorten(t.Text)}\"",
        _ => AutomationProperties.GetName(c) ?? c.GetType().Name,
    };

    static string Shorten(string? text) => text is null ? "" : text.Length <= 24 ? text : text[..24] + "…";
}

static void Pump(int n)
{
    for (var i = 0; i < n; i++)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(10);
    }
}

internal enum SyncFixture { Off, UpToDate, Conflict, Error }

/// <summary>One isolated app shell over fakes — no database, no network, no real profile.</summary>
internal sealed class Fixture
{
    /// <summary>Phase 23C: the harness-wide appearance controller (in-memory store, simulated OS).</summary>
    public static AppearanceThemeController? Appearance { get; set; }

    public FakeOperationsCalendarService CalendarService { get; } = new();
    public FakeAssociatePassdownService AssociateService { get; } = new();
    public MainViewModel Vm { get; }
    public MainWindow Window { get; }

    public Fixture(bool emptyNotes = false, bool emptyHistory = false, SyncFixture sync = SyncFixture.Off, Carowinds.Passdown.Application.Shows.ShowScheduleView? dashboardShows = null)
    {
        var clock = new FakeClock(Fixtures.Now);
        var dashboard = new FakeOperationsDashboardService();
        BusyDayFixtures.ApplyBusyDay(dashboard);
        if (dashboardShows is not null) Phase23BDashboardShowsFixtures.Apply(dashboard, dashboardShows);
        BusyDayFixtures.ApplyBusyAssociate(AssociateService, partialFailure: true);
        Fixtures.ApplyCalendar(CalendarService);
        var notes = new FakeOperationalNotesService { UtcNowFactory = () => Fixtures.Now };
        if (!emptyNotes) Fixtures.SeedNotes(notes);
        var history = new FakePassdownHistoryService();
        if (!emptyHistory) Fixtures.SeedHistory(history);
        var priceHistory = new FakeSeasonPriceHistoryService();
        Fixtures.SeedPriceHistory(priceHistory);

        var (settingsService, coordinator, conflicts) = Fixtures.Sync(sync);
        var diagnostics = new DiagnosticsExportViewModel(new FakeDiagnosticExportService(),
            new FakeDiagnosticExportFilePicker { ResultFactory = () => DiagnosticExportSaveResult.Failed("The folder could not be written to. Choose another location.") });
        var build = BuildIdentity.FromAssembly(typeof(App).Assembly, @"C:\Users\associate\Downloads\Carowinds-Operations-0.1.0-rc.6-win-x64\Carowinds.Passdown.Desktop.exe");

        Vm = new MainViewModel(
            new OperationsDashboardViewModel(dashboard),
            new OperationsCalendarViewModel(CalendarService, clock),
            new AssociateViewModel(AssociateService, clock, autosaveDelay: (_, _) => Task.CompletedTask),
            new OperationalNotesViewModel(notes, clock),
            new PassdownHistoryViewModel(history, new FakeFinalizedPassdownPdfRetrievalService()),
            new PersonalSyncSettingsViewModel(settingsService, new FakeSyncFolderPicker(), conflicts, coordinator, buildIdentity: build, diagnostics: diagnostics,
                appearance: new AppearanceSettingsViewModel(Appearance)),
            new PriceHistoryViewModel(priceHistory));
        Window = new MainWindow { DataContext = Vm };
    }
}
