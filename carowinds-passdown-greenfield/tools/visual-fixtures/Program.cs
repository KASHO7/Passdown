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
using Carowinds.Passdown.Desktop;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Ui.Tests.TestSupport;

// Phase 22F synthetic-fixture screenshot harness. Usage: README.md.
// args: <outDir> [label] [scenario substring filter] [sizes: all|main]
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();

var outDir = args.Length > 0 ? args[0] : "visual-fixtures-out";
var label = args.Length > 1 ? args[1] : "shot";
var filter = args.Length > 2 && args[2] != "*" ? args[2] : null;
var sizes = args.Length > 3 && args[3] == "main"
    ? new (int W, int H)[] { (1280, 860) }
    : new (int W, int H)[] { (960, 640), (1280, 690), (1280, 860), (1920, 1000) };
Directory.CreateDirectory(outDir);
var report = new List<string>();

var scenarios = new List<(string Name, Func<Fixture> Create, Action<Fixture> Arrange)>
{
    ("Dashboard", () => new Fixture(), f => Go(f, AppSection.Dashboard)),
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
};

foreach (var (w, h) in sizes)
{
    foreach (var theme in new[] { "Light", "Dark" })
    {
        Avalonia.Application.Current!.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        foreach (var (name, create, arrange) in scenarios)
        {
            if (filter is not null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

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
    return $"pageScroll={vertical:F0} horizontalOverflow=[{string.Join(",", horizontal)}] buttonsPastRightEdge=[{string.Join(",", clipped)}]";
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
    public FakeOperationsCalendarService CalendarService { get; } = new();
    public FakeAssociatePassdownService AssociateService { get; } = new();
    public MainViewModel Vm { get; }
    public MainWindow Window { get; }

    public Fixture(bool emptyNotes = false, bool emptyHistory = false, SyncFixture sync = SyncFixture.Off)
    {
        var clock = new FakeClock(Fixtures.Now);
        var dashboard = new FakeOperationsDashboardService();
        BusyDayFixtures.ApplyBusyDay(dashboard);
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
            new PersonalSyncSettingsViewModel(settingsService, new FakeSyncFolderPicker(), conflicts, coordinator, buildIdentity: build, diagnostics: diagnostics),
            new PriceHistoryViewModel(priceHistory));
        Window = new MainWindow { DataContext = Vm };
    }
}
