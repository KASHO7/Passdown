using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Carowinds.Passdown.Application.Diagnostics;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.PdfOutput;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Desktop;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Infrastructure.PdfOutput;
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
// Phase 35A: "shell" renders the four desktop sizes named in the shell review checklist.
var sizes = (args.Length > 3 ? args[3] : "all") switch
{
    "main" => new (int W, int H)[] { (1280, 860) },
    "shell" => new (int W, int H)[] { (1920, 1080), (1536, 960), (1280, 720), (960, 640) },
    // Phase 35G: an explicit list such as "1280x720,960x640".
    var list when list.Contains('x') => list.Split(',').Select(s => s.Split('x')).Select(p => (int.Parse(p[0]), int.Parse(p[1]))).ToArray(),
    _ => new (int W, int H)[] { (960, 640), (1280, 690), (1280, 860), (1920, 1000) },
};
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
    // Phase 35A: the shared shell — sidebar expanded / collapsed on representative pages, and keyboard focus on a
    // destination. The sidebar state is in memory only (no navigation-sidebar.json is read or written).
    ("Shell-Expanded-Dashboard", () => new Fixture(), f => Go(f, AppSection.Dashboard)),
    ("Shell-Collapsed-Dashboard", () => new Fixture(), f => { Go(f, AppSection.Dashboard); Collapse(f); }),
    ("Shell-Expanded-Passdown", () => new Fixture(), f => Go(f, AppSection.AssociatePassdown)),
    ("Shell-Collapsed-Passdown", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); Collapse(f); }),
    ("Shell-Expanded-Calendar", () => new Fixture(), f => { Go(f, AppSection.OperationsCalendar); Select(f, Fixtures.Busy); }),
    ("Shell-Expanded-Settings", () => new Fixture(), f => Go(f, AppSection.Settings)),
    ("Shell-FocusNavigation", () => new Fixture(), f => { Go(f, AppSection.Dashboard); FocusNavigation(f.Window, "Operations Calendar section"); }),
    ("Shell-FocusNavigationCollapsed", () => new Fixture(), f => { Go(f, AppSection.Dashboard); Collapse(f); FocusNavigation(f.Window, "Operations Calendar section"); }),
    // Phase 35B: the Passdown workspace (toolbar, section tabs, Overview sheet, divider, preview companion).
    ("Workspace-Overview", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); }),
    ("Workspace-Overview-Collapsed", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); Collapse(f); }),
    ("Workspace-PreviewHidden", () => new Fixture(preview: PdfPreviewLayoutPreferences.BuiltIn with { IsPreviewVisible = false }), f => { Go(f, AppSection.AssociatePassdown); }),
    ("Workspace-FocusedInput", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); FocusNamed(f.Window, "Projected attendance"); }),
    ("Workspace-FocusedTab", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); FocusNamed(f.Window, "Products section"); }),
    ("Workspace-ProductsTab", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowProductsSectionCommand.Execute(null); Pump(8); }),
    ("Workspace-Overrides", () => new Fixture(), f =>
    {
        Go(f, AppSection.AssociatePassdown);
        var a = f.Vm.Associate;
        a.ManualHoursOverrideEnabled = true; a.ManualOpeningTimeText = "9:00 AM"; a.ManualClosingTimeText = "10:00 PM";
        a.PrivateBuyoutEnabled = true; a.PrivateBuyoutOpeningTimeText = "6:00 PM"; a.PrivateBuyoutClosingTimeText = "11:00 PM";
        a.PrivateBuyoutDetails = "Synthetic group buyout"; a.NorthGateOverrideText = "9:00 AM";
        Pump(10);
    }),
    ("Workspace-ErtEditor", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.EarlyRideTimeEditor.OpenErtEditorCommand.Execute(null); Pump(8); ScrollToName(f.Window, "Early Ride Time editor"); }),
    ("Workspace-LongContentScrolled", () => new Fixture(), f =>
    {
        Go(f, AppSection.AssociatePassdown);
        f.Vm.Associate.DailyFeatureText = string.Join(" ", Enumerable.Repeat("Synthetic long Daily Feature text that wraps across several lines in the editor.", 8));
        f.Vm.Associate.ManualHoursOverrideEnabled = true; f.Vm.Associate.PrivateBuyoutEnabled = true;
        Pump(10); ScrollToEnd(f.Window);
    }),
    // Phase 35A: Settings → Appearance → Interface animations in its three effective states (in-memory choice,
    // simulated Windows "Animation effects"; never interface-animations.json).
    ("Settings-AnimationsOn", () => new Fixture(motion: ShellMotion.Create(InterfaceAnimationPreference.On, SystemReducedMotion.NotRequested)), f => { Go(f, AppSection.Settings); ScrollToName(f.Window, "Interface animations"); }),
    ("Settings-AnimationsOff", () => new Fixture(motion: ShellMotion.Create(InterfaceAnimationPreference.Off, SystemReducedMotion.NotRequested)), f => { Go(f, AppSection.Settings); ScrollToName(f.Window, "Interface animations"); }),
    ("Settings-AnimationsWindowsReduced", () => new Fixture(motion: ShellMotion.Create(InterfaceAnimationPreference.On, SystemReducedMotion.Requested)), f => { Go(f, AppSection.Settings); ScrollToName(f.Window, "Interface animations"); }),
    ("Dashboard", () => new Fixture(), f => Go(f, AppSection.Dashboard)),
    // Phase 35E Dashboard follow-up: the approved Admissions Overview in its real states (synthetic data).
    ("Dashboard35E-Typical", () => new Fixture(), f => Go(f, AppSection.Dashboard)),
    ("Dashboard35E-TypicalEnd", () => new Fixture(), f => { Go(f, AppSection.Dashboard); ScrollToEnd(f.Window); }),
    ("Dashboard35E-Collapsed", () => new Fixture(), f => { Go(f, AppSection.Dashboard); Collapse(f); }),
    ("Dashboard35E-NothingYet", () => new Fixture(dashboardVariant: DashboardVariant.NothingYet), f => Go(f, AppSection.Dashboard)),
    ("Dashboard35E-Stale", () => new Fixture(dashboardVariant: DashboardVariant.Stale), f => Go(f, AppSection.Dashboard)),
    ("Dashboard35E-LongText", () => new Fixture(dashboardVariant: DashboardVariant.LongText), f => Go(f, AppSection.Dashboard)),
    ("Dashboard35E-Focus", () => new Fixture(), f => { Go(f, AppSection.Dashboard); FocusNamed(f.Window, "Open Passdown"); }),
    // Phase 37C: the Weather card (approved D4 mock-up) in each state, synthetic weather (no NWS request).
    ("Dashboard37C-Normal", () => new Fixture(weatherState: "Normal"), f => Go(f, AppSection.Dashboard)),
    ("Dashboard37C-Expanded-Normal", () => new Fixture(weatherState: "Normal", weatherExpanded: true), f => Go(f, AppSection.Dashboard)),
    ("Dashboard37C-Alerts", () => new Fixture(weatherState: "Alerts"), f => Go(f, AppSection.Dashboard)),
    ("Dashboard37C-Expanded-Alerts", () => new Fixture(weatherState: "Alerts", weatherExpanded: true), f => Go(f, AppSection.Dashboard)),
    ("Dashboard37C-Stale", () => new Fixture(weatherState: "Stale"), f => Go(f, AppSection.Dashboard)),
    ("Dashboard37C-Expanded-Stale", () => new Fixture(weatherState: "Stale", weatherExpanded: true), f => Go(f, AppSection.Dashboard)),
    ("Dashboard37C-Unavailable", () => new Fixture(weatherState: "Unavailable"), f => Go(f, AppSection.Dashboard)),
    ("Dashboard37C-Loading", () => new Fixture(weatherState: "Loading"), f => Go(f, AppSection.Dashboard)),
    ("Dashboard37C-Evening", () => new Fixture(weatherState: "Evening"), f => Go(f, AppSection.Dashboard)),
    ("Dashboard35E-SourceDetails", () => new Fixture(), f => { Go(f, AppSection.Dashboard); f.Vm.Dashboard.IsSourceDetailsExpanded = true; Pump(8); ScrollToEnd(f.Window); }),
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
    // Phase 31D/31E: Include in Passdown on Overview; show selection on its own Shows rail page (on and off).
    ("Associate-PassdownChoices", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowOverviewSectionCommand.Execute(null); Pump(8); ScrollToName(f.Window, "Include in Passdown"); }),
    ("Associate-ShowsPage", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowShowsSectionCommand.Execute(null); Pump(8); }),
    ("Associate-ShowsPage-Off", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.IncludeShows = false; f.Vm.Associate.ShowShowsSectionCommand.Execute(null); Pump(8); }),
    // Phase 35C: Products & Pricing redesign and the anchored price popover (in-memory fixtures only).
    ("P35C-Products", () => new Fixture(), f => Products(f)),
    ("P35C-Products-Collapsed", () => new Fixture(), f => { Products(f); Collapse(f); }),
    ("P35C-Products-PreviewHidden", () => new Fixture(preview: PdfPreviewLayoutPreferences.BuiltIn with { IsPreviewVisible = false }), f => Products(f)),
    ("P35C-Override", () => new Fixture(), f => { Products(f); FirstRow(f).DisplayPriceOverrideText = "$44.00"; Pump(8); }),
    ("P35C-Popover", () => new Fixture(), f => { Products(f); OpenPriceEditor(f.Window, FirstRow(f).DisplayName); }),
    ("P35C-PopoverOverride", () => new Fixture(), f => { Products(f); FirstRow(f).DisplayPriceOverrideText = "$44.00"; Pump(6); OpenPriceEditor(f.Window, FirstRow(f).DisplayName); }),
    ("P35C-PopoverInvalid", () => new Fixture(), f =>
    {
        Products(f); OpenPriceEditor(f.Window, FirstRow(f).DisplayName);
        f.Vm.Associate.PriceEditor.EditText = "Special"; f.Vm.Associate.PriceEditor.SaveCommand.Execute(null); Pump(8);
    }),
    ("P35C-PopoverNearBottom", () => new Fixture(), f =>
    {
        Products(f);
        var name = LowestVisibleEdit(f.Window);
        if (name is not null) { OpenPriceEditorByName(f.Window, name); }
    }),
    ("P35C-FocusEdit", () => new Fixture(), f => { Products(f); FocusStartsWith(f.Window, "Edit display price for " + FirstRow(f).DisplayName); }),
    ("P35C-Search", () => new Fixture(), f => { Products(f); f.Vm.Associate.ProductSearchText = "pass"; Pump(8); }),
    ("P35C-Scrolled", () => new Fixture(), f => { Products(f); ScrollBy(f.Window, 900); }),
    ("Associate-Products", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowProductsSectionCommand.Execute(null); Pump(8); }),
    // Phase 25: the Season Passes and Memberships groups (scrolled into view).
    ("Associate-Memberships", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowProductsSectionCommand.Execute(null); Pump(8); ScrollToEnd(f.Window); }),
    // Phase 31C: the compact Current Deals list and product rows further down the Products page.
    ("Associate-Products-Scroll700", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowProductsSectionCommand.Execute(null); Pump(8); ScrollBy(f.Window, 700); }),
    ("Associate-Products-Scroll1400", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowProductsSectionCommand.Execute(null); Pump(8); ScrollBy(f.Window, 1400); }),
    ("Associate-Details-Deal", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowProductsSectionCommand.Execute(null); Pump(8);
        f.Vm.Associate.PendingDealsLoad.Wait(2000); Pump(4); f.Vm.Associate.CurrentDeals.First(d => d.Title.Contains("Bundle")).ViewDetailsCommand.Execute(null); Pump(10); }),
    ("Associate-Details-Resort", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowProductsSectionCommand.Execute(null); Pump(8);
        f.Vm.Associate.PendingDealsLoad.Wait(2000); Pump(4); f.Vm.Associate.CurrentDeals.First(d => d.Title.Contains("Resort")).ViewDetailsCommand.Execute(null); Pump(10); }),
    ("Associate-Details-GoldPass", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowProductsSectionCommand.Execute(null); Pump(8);
        f.Vm.Associate.ProductSections.SelectMany(s => s.Products).First(r => r.DisplayName.StartsWith("Gold Season", StringComparison.Ordinal)).ViewDetailsCommand.Execute(null); Pump(10); }),
    ("Associate-Details-NoTerms", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowProductsSectionCommand.Execute(null); Pump(8);
        f.Vm.Associate.ProductSections.SelectMany(s => s.Products).First(r => r.DisplayName.StartsWith("All Season Dining", StringComparison.Ordinal)).ViewDetailsCommand.Execute(null); Pump(10); }),
    // Phase 32: the Pass Perks panel on Products, a perk's View details, the Calendar markers and the setting.
    ("Associate-PassPerks", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowProductsSectionCommand.Execute(null); Pump(8);
        f.Vm.Associate.PassPerks!.PendingLoad.Wait(2000); Pump(6); ScrollToName(f.Window, "Pass Perks month"); }),
    ("Associate-Details-VisitPerk", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowProductsSectionCommand.Execute(null); Pump(8);
        f.Vm.Associate.PassPerks!.PendingLoad.Wait(2000); Pump(4); f.Vm.Associate.PassPerks.Items.First(i => i.Title.StartsWith("Earn", StringComparison.Ordinal)).ViewDetailsCommand.Execute(null); Pump(10); }),
    ("Associate-Details-PerkTerms", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowProductsSectionCommand.Execute(null); Pump(8);
        f.Vm.Associate.PassPerks!.PendingLoad.Wait(2000); Pump(4); f.Vm.Associate.PassPerks.ViewProgramTermsCommand.Execute(null); Pump(10); }),
    ("Calendar-PassPerks", () => new Fixture(), f => { Go(f, AppSection.OperationsCalendar); Select(f, Fixtures.Other); }),
    ("Calendar-PassPerksMonthLong", () => new Fixture(), f => { Go(f, AppSection.OperationsCalendar); Select(f, Fixtures.Today);
        foreach (var e in f.Window.GetVisualDescendants().OfType<Expander>().Where(e => (e.Header as string)?.StartsWith("Valid all", StringComparison.Ordinal) == true)) e.IsExpanded = true; Pump(6); }),
    ("Calendar-PassPerksOff", () => new Fixture(showPassPerks: false), f => { Go(f, AppSection.OperationsCalendar); Select(f, Fixtures.Other); }),
    // Phase 32A: the grid's last rows (Oct 24: two redemption perks; Oct 25: all three marker types) and the legend.
    ("Calendar-PassPerksGrid", () => new Fixture(), f => { Go(f, AppSection.OperationsCalendar); Select(f, Fixtures.Other); ScrollBy(f.Window, 420); }),
    ("Settings-CalendarDisplay", () => new Fixture(), f => { Go(f, AppSection.Settings); Pump(6); ScrollToName(f.Window, "Calendar"); }),
    // Phase 35D: Shows & Entertainment and Passdown Notes redesign, the Actions dropdown and its chevron.
    ("P35D-Shows", () => new Fixture(), f => ShowsTab(f)),
    ("P35D-Shows-Expanded", () => new Fixture(), f => { ShowsTab(f); f.Vm.Associate.ShowRows.First(r => r.CanExpand).IsExpanded = true; Pump(8); }),
    ("P35D-Shows-Collapsed", () => new Fixture(), f => { ShowsTab(f); Collapse(f); }),
    ("P35D-Shows-PreviewHidden", () => new Fixture(preview: PdfPreviewLayoutPreferences.BuiltIn with { IsPreviewVisible = false }), f => { ShowsTab(f); f.Vm.Associate.ShowRows.First(r => r.CanExpand).IsExpanded = true; Pump(8); }),
    ("P35D-Notes", () => new Fixture(), f => NotesTab(f)),
    ("P35D-Notes-Included", () => new Fixture(includedNote: true), f => { NotesTab(f); ScrollToName(f.Window, "Persistent operational notes"); }),
    ("P35D-Notes-Collapsed", () => new Fixture(), f => { NotesTab(f); Collapse(f); }),
    ("P35D-Notes-PreviewHidden", () => new Fixture(preview: PdfPreviewLayoutPreferences.BuiltIn with { IsPreviewVisible = false }), f => NotesTab(f)),
    ("P35D-Notes-Long", () => new Fixture(), f =>
    {
        NotesTab(f);
        // Blank lines included: they are typed in practice and never print (each non-empty line is one note).
        f.Vm.Associate.OperationalNotesText = string.Join(Environment.NewLine, Enumerable.Range(1, 24).Select(i => i % 4 == 0 ? string.Empty : $"Synthetic daily note {i}: a long line that wraps inside the editor so scrolling stays predictable."));
        Pump(12);
    }),
    ("P35D-Actions", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); OpenFlyout(f.Window, "Passdown actions"); }),
    // Phase 35D follow-up: the compact date selector on today (no Today button) and on another date (Today shown).
    ("P35D-Date-Today", () => new Fixture(), f => Go(f, AppSection.AssociatePassdown)),
    ("P35D-Date-OtherDay", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.PreviousDayCommand.Execute(null); Pump(12); }),
    ("P35D-Date-OtherDayCollapsed", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); Collapse(f); f.Vm.Associate.NextDayCommand.Execute(null); Pump(12); }),
    ("Associate-Notes", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowNotesSectionCommand.Execute(null); Pump(8); }),
    // Phase 33: the live PDF preview (real QuestPDF bytes rendered by PDFium) beside the editor.
    ("Preview-Hidden", () => new Fixture(preview: PdfPreviewLayoutPreferences.BuiltIn with { IsPreviewVisible = false }), f => { Go(f, AppSection.AssociatePassdown); Pump(8); }),
    ("Preview-Modern", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.SelectTemplateCommand.Execute(AssociatePassdownTemplate.Modern); Pump(12); }),
    ("Preview-Compact", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.SelectTemplateCommand.Execute(AssociatePassdownTemplate.Compact); Pump(12); }),
    ("Preview-FitPage", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.Preview.FitPageCommand.Execute(null); Pump(12); }),
    ("Preview-Zoomed", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.Preview.ZoomInCommand.Execute(null); f.Vm.Associate.Preview.ZoomInCommand.Execute(null); Pump(12); }),
    ("Preview-LongNotesMultiPage", () => new Fixture(), f =>
    {
        Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowNotesSectionCommand.Execute(null); Pump(6);
        f.Vm.Associate.OperationalNotesText = string.Join(Environment.NewLine, Enumerable.Range(1, 60).Select(i => $"Synthetic operational note {i}: a deliberately long line so the passdown continues onto another page rather than truncating anything."));
        Pump(12); f.Vm.Associate.Preview.NextPageCommand.Execute(null); Pump(12);
    }),
    ("Preview-WideLayout", () => new Fixture(preview: PdfPreviewLayoutPreferences.BuiltIn with { PreviewPaneWidth = 760 }), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowProductsSectionCommand.Execute(null); Pump(12); }),
    ("Preview-NarrowPreviewFirst", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.Preview.ShowPreviewCommand.Execute(null); Pump(12); }),
    // A date change held mid-load: the previous date's page is gone at once; the pane says it is loading.
    ("Preview-SwitchingDates", () => new Fixture(), f =>
    {
        Go(f, AppSection.AssociatePassdown); Pump(6);
        f.AssociateService.LoadGate = new TaskCompletionSource<bool>();
        f.Vm.Associate.NextDayCommand.Execute(null); Pump(8);
    }),
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
    // Phase 35B: Copy for Teams, Print, Save PDF, PDF style, preview, Check Online and Price History share one Actions menu.
    ("Popup-Actions", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); OpenFlyout(f.Window, "Passdown actions"); }),
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
    // Phase 35F: Operational Notes list-detail, editor dialog, resolved, search and empty search.
    ("P35F-Notes", () => new Fixture(), f => { Go(f, AppSection.OperationalNotes); if (f.Vm.Notes.Notes.Count > 0) { f.Vm.Notes.SelectedNote = f.Vm.Notes.Notes[0]; Pump(6); } }),
    ("P35F-Notes-Collapsed", () => new Fixture(), f => { Go(f, AppSection.OperationalNotes); Collapse(f); if (f.Vm.Notes.Notes.Count > 0) { f.Vm.Notes.SelectedNote = f.Vm.Notes.Notes[0]; Pump(6); } }),
    ("P35F-Notes-Editor", () => new Fixture(), f => { Go(f, AppSection.OperationalNotes); f.Vm.Notes.NewNoteCommand.Execute(null); Pump(8); }),
    ("P35F-Notes-Edit", () => new Fixture(), f => { Go(f, AppSection.OperationalNotes); f.Vm.Notes.SelectedNote = f.Vm.Notes.Notes[0]; Pump(6); f.Vm.Notes.EditCommand.Execute(null); Pump(8); }),
    ("P35F-Notes-Resolve", () => new Fixture(), f => { Go(f, AppSection.OperationalNotes); f.Vm.Notes.SelectedNote = f.Vm.Notes.Notes[0]; Pump(6); f.Vm.Notes.RequestResolveCommand.Execute(null); Pump(6); }),
    ("P35F-Notes-Resolved", () => new Fixture(), f => { Go(f, AppSection.OperationalNotes); f.Vm.Notes.SetFilterCommand.Execute(OperationalNoteLifecycleFilter.Resolved); Pump(6); if (f.Vm.Notes.Notes.Count > 0) { f.Vm.Notes.SelectedNote = f.Vm.Notes.Notes[0]; Pump(6); } }),
    ("P35F-Notes-Upcoming", () => new Fixture(), f => { Go(f, AppSection.OperationalNotes); f.Vm.Notes.SetFilterCommand.Execute(OperationalNoteLifecycleFilter.Upcoming); Pump(6); f.Vm.Notes.SelectedNote = f.Vm.Notes.Notes.LastOrDefault(); Pump(6); }),
    ("P35F-Notes-Search", () => new Fixture(), f => { Go(f, AppSection.OperationalNotes); f.Vm.Notes.SearchText = "scanner"; Pump(6); }),
    // Phase 35F: Passdown History list-detail, a record without its PDF, search, record type and empty search.
    ("P35F-History", () => new Fixture(), f => { Go(f, AppSection.History); f.Vm.History.SelectedItem = f.Vm.History.HistoryItems[0]; Pump(10); }),
    ("P35F-History-Collapsed", () => new Fixture(), f => { Go(f, AppSection.History); Collapse(f); f.Vm.History.SelectedItem = f.Vm.History.HistoryItems[0]; Pump(10); }),
    ("P35F-History-Scrolled", () => new Fixture(), f => { Go(f, AppSection.History); f.Vm.History.SelectedItem = f.Vm.History.HistoryItems[0]; Pump(10); ScrollBy(f.Window, 600); }),
    ("P35F-History-NoPdf", () => new Fixture(), f => { Go(f, AppSection.History); f.Vm.History.SelectedItem = f.Vm.History.HistoryItems.First(i => !i.HasFrozenPdf); Pump(10); }),
    ("P35F-History-Manual", () => new Fixture(), f => { Go(f, AppSection.History); f.Vm.History.SetOriginFilterCommand.Execute(Carowinds.Passdown.Application.Finalization.FinalizationOrigin.Manual); Pump(6); f.Vm.History.SelectedItem = f.Vm.History.HistoryItems.FirstOrDefault(); Pump(10); }),
    ("P35F-History-Search", () => new Fixture(), f => { Go(f, AppSection.History); f.Vm.History.SearchText = "pdf unavailable"; Pump(6); }),
    ("P35F-History-SearchEmpty", () => new Fixture(), f => { Go(f, AppSection.History); f.Vm.History.SearchText = "no such date"; Pump(6); }),
    ("P35F-History-Empty", () => new Fixture(emptyHistory: true), f => Go(f, AppSection.History)),
    // Phase 35F: Settings section list — each entry scrolls the page to its group.
    ("P35F-Settings", () => new Fixture(sync: SyncFixture.UpToDate), f => Go(f, AppSection.Settings)),
    ("P35F-Settings-Collapsed", () => new Fixture(sync: SyncFixture.UpToDate), f => { Go(f, AppSection.Settings); Collapse(f); }),
    ("P35F-Settings-Defaults", () => new Fixture(sync: SyncFixture.UpToDate), f => { Go(f, AppSection.Settings); ClickNamed(f.Window, "Go to Passdown Defaults settings"); }),
    ("P35F-Settings-Refresh", () => new Fixture(sync: SyncFixture.UpToDate), f => { Go(f, AppSection.Settings); ClickNamed(f.Window, "Go to Data Refresh information"); }),
    ("P35F-Settings-Sync", () => new Fixture(sync: SyncFixture.UpToDate), f => { Go(f, AppSection.Settings); ClickNamed(f.Window, "Go to Personal Sync settings"); }),
    ("P35F-Settings-SyncError", () => new Fixture(sync: SyncFixture.Error), f => { Go(f, AppSection.Settings); ClickNamed(f.Window, "Go to Personal Sync settings"); }),
    ("P35F-Settings-Diagnostics", () => new Fixture(sync: SyncFixture.UpToDate), f => { Go(f, AppSection.Settings); ClickNamed(f.Window, "Go to Diagnostics"); }),
    // Phase 35G: Settings → Personal Sync with a very long folder path.
    ("G35-Settings-LongPath", () => new Fixture(sync: SyncFixture.LongPath), f => { Go(f, AppSection.Settings); ClickNamed(f.Window, "Go to Personal Sync settings"); }),
    ("P35F-Settings-About", () => new Fixture(sync: SyncFixture.UpToDate), f => { Go(f, AppSection.Settings); ClickNamed(f.Window, "Go to About this build"); }),
    ("P35F-Notes-SearchEmpty", () => new Fixture(), f => { Go(f, AppSection.OperationalNotes); f.Vm.Notes.SearchText = "no such note"; Pump(6); }),
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
    ("LiveSwitch-Actions", () => new Fixture(), f => { Go(f, AppSection.AssociatePassdown); OpenFlyout(f.Window, "Passdown actions"); Flip(); }),
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

// Phase 36B: refuse a run that would keep too many windows' worth of memory in one process (CaptureBudget.cs).
if (!CaptureBudget.TryLoad(out var budget, out var budgetError))
{
    Console.Error.WriteLine(budgetError);
    return 2;
}

var plannedCaptures = sizes.Length * appearanceModes.Length
    * scenarios.Count(s => filter is null || s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
if (plannedCaptures > budget.MaxCaptures)
{
    Console.Error.WriteLine(budget.TooManyCapturesMessage(plannedCaptures));
    return 2;
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

            // Phase 36B: stop before the next window if the process is already past the working-set ceiling.
            if (budget.IsWorkingSetExceeded(out var workingSetMegabytes))
            {
                WriteReport();
                Console.Error.WriteLine(budget.WorkingSetExceededMessage(workingSetMegabytes, report.Count, plannedCaptures));
                return 3;
            }

            // Reset before every scenario: LiveSwitch-* scenarios change the appearance mid-arrange.
            appearanceStore.SaveException = null;
            simulatedOs.Switch(os);
            // Phase 35G: a fresh controller per scenario. Each Settings view model subscribes to the controller for
            // the app's lifetime (one window in the real app), so one shared controller kept every closed scenario
            // window reachable — ~12 MB each, ~18 GB over a full four-size run.
            appearance = new AppearanceThemeController(appearanceStore, simulatedOs, new ApplicationThemeVariantTarget());
            appearance.Initialize();
            Fixture.Appearance = appearance;
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
            fixture.AssociateService.LoadGate?.TrySetResult(true);
            fixture.Window.Close();
            Pump(2);
        }
    }
}

WriteReport();
Console.WriteLine(string.Join(Environment.NewLine, report));
return 0;

void WriteReport() => File.WriteAllLines(Path.Combine(outDir, $"{label}-geometry.txt"), report);

void Shot(Window window, string name)
{
    using var frame = window.CaptureRenderedFrame();
    // Phase 36B: the non-obsolete overload (CS0618); PNG with the default options, the same encoder as before.
    frame?.Save(Path.Combine(outDir, $"{label}-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
}

static void Collapse(Fixture f)
{
    if (f.Vm.ToggleNavigationCommand.CanExecute(null) && !f.Vm.IsNavigationCollapsed)
    {
        f.Vm.ToggleNavigationCommand.Execute(null);
    }

    Pump(8);
}

static void FocusNavigation(Window window, string automationName)
{
    var item = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => AutomationProperties.GetName(b) == automationName);
    item?.Focus(Avalonia.Input.NavigationMethod.Tab);
    Pump(8);
}

static void ClickNamed(Window window, string automationName)
{
    var button = window.GetVisualDescendants().OfType<Button>().First(b => AutomationProperties.GetName(b) == automationName);
    button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Pump(8);
}

static void FocusNamed(Window window, string automationName)
{
    var control = window.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.IsEffectivelyVisible && c.Focusable && AutomationProperties.GetName(c) == automationName);
    control?.Focus(Avalonia.Input.NavigationMethod.Tab);
    Pump(8);
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

static void ScrollBy(Window window, double y)
{
    var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.IsEffectivelyVisible).OrderByDescending(s => s.Extent.Height).FirstOrDefault();
    if (scroll is not null)
    {
        scroll.Offset = new Vector(0, Math.Min(y, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)));
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
    var horizontal = scrolls.Where(s => s.Extent.Width > s.Viewport.Width + 1).Select(s => string.IsNullOrEmpty(s.Name) ? $"{s.Extent.Width - s.Viewport.Width:F0}" : $"{s.Name}:{s.Extent.Width - s.Viewport.Width:F0}").ToList();
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

static void Products(Fixture f)
{
    Go(f, AppSection.AssociatePassdown);
    f.Vm.Associate.ShowProductsSectionCommand.Execute(null);
    Pump(10);
}

static ProductRowViewModel FirstRow(Fixture f) =>
    f.Vm.Associate.ProductSections.SelectMany(s => s.Products).First(r => r.CanEditPrice);

// Phase 35C: activates an Edit control the way a click/Enter does (Click first — it anchors the popover — then the command).
static void OpenPriceEditor(Window window, string productName) => OpenPriceEditorByName(window, FindEdit(window, "Edit display price for " + productName)?.Name2());

static void OpenPriceEditorByName(Window window, string? automationName)
{
    var button = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == automationName);
    if (button is null)
    {
        return;
    }

    button.Focus(Avalonia.Input.NavigationMethod.Tab);
    button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    button.Command?.Execute(button.CommandParameter);
    Pump(12);
}

static Button? FindEdit(Window window, string prefix) =>
    window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && (AutomationProperties.GetName(b) ?? string.Empty).StartsWith(prefix, StringComparison.Ordinal));

static string? LowestVisibleEdit(Window window) =>
    window.GetVisualDescendants().OfType<Button>()
        .Where(b => b.IsEffectivelyVisible && (AutomationProperties.GetName(b) ?? string.Empty).StartsWith("Edit display price for ", StringComparison.Ordinal))
        .Select(b => (Button: b, Y: b.TranslatePoint(new Point(0, b.Bounds.Height), window)?.Y ?? double.MaxValue))
        .Where(x => x.Y < window.Bounds.Height - 30)
        .OrderByDescending(x => x.Y).Select(x => AutomationProperties.GetName(x.Button)).FirstOrDefault();

static void FocusStartsWith(Window window, string prefix)
{
    FindEdit(window, prefix)?.Focus(Avalonia.Input.NavigationMethod.Tab);
    Pump(8);
}

static void ShowsTab(Fixture f) { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowShowsSectionCommand.Execute(null); Pump(10); }

static void NotesTab(Fixture f) { Go(f, AppSection.AssociatePassdown); f.Vm.Associate.ShowNotesSectionCommand.Execute(null); Pump(10); }

static void Pump(int n)
{
    for (var i = 0; i < n; i++)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(10);
    }
}

internal enum SyncFixture { Off, UpToDate, Conflict, Error, LongPath }

/// <summary>One isolated app shell over fakes — no database, no network, no real profile.</summary>
/// <summary>Phase 35A: an Interface animations controller with an in-memory choice and a simulated OS setting.</summary>
internal static class ShellMotion
{
    public static InterfaceMotionController Create(InterfaceAnimationPreference choice, SystemReducedMotion os)
    {
        var controller = new InterfaceMotionController(new MemoryStore(choice), new FixedReader(os));
        controller.Initialize();
        return controller;
    }

    private sealed class MemoryStore(InterfaceAnimationPreference value) : IInterfaceAnimationPreferenceStore
    {
        private InterfaceAnimationPreference _value = value;

        public InterfaceAnimationPreference Load() => _value;

        public Task SaveAsync(InterfaceAnimationPreference preference, CancellationToken cancellationToken = default)
        {
            _value = preference;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedReader(SystemReducedMotion value) : ISystemReducedMotionReader
    {
        public SystemReducedMotion Read() => value;
    }
}

internal sealed class Fixture
{
    /// <summary>Phase 23C: the harness-wide appearance controller (in-memory store, simulated OS).</summary>
    public static AppearanceThemeController? Appearance { get; set; }

    public FakeOperationsCalendarService CalendarService { get; } = new();
    public FakeAssociatePassdownService AssociateService { get; } = new();
    public MainViewModel Vm { get; }
    public MainWindow Window { get; }

    public Fixture(bool includedNote = false, bool emptyNotes = false, bool emptyHistory = false, SyncFixture sync = SyncFixture.Off, Carowinds.Passdown.Application.Shows.ShowScheduleView? dashboardShows = null, bool showPassPerks = true,
        PdfPreviewLayoutPreferences? preview = null, InterfaceMotionController? motion = null, DashboardVariant dashboardVariant = DashboardVariant.Typical,
        string? weatherState = null, bool weatherExpanded = false)
    {
        var clock = new FakeClock(Fixtures.Now);
        var passPerks = Phase32Fixtures.Service();
        var calendarDisplay = new CalendarDisplaySettingsViewModel(new Phase32Fixtures.MemoryCalendarStore(showPassPerks));
        calendarDisplay.PendingLoad.Wait(2000);
        var dashboard = new FakeOperationsDashboardService();
        BusyDayFixtures.ApplyBusyDay(dashboard);
        if (dashboardShows is not null) Phase23BDashboardShowsFixtures.Apply(dashboard, dashboardShows);
        Phase35EDashboardFixtures.Apply(dashboard, dashboardVariant);
        BusyDayFixtures.ApplyBusyAssociate(AssociateService, partialFailure: true);
        Phase35DFixtures.SeedPersistentNotes(AssociateService);
        if (includedNote) Phase35DFixtures.IncludeFirstPersistentNote(AssociateService);
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

        // Phase 37C: the Weather card over a scripted monitor (synthetic snapshots, no NWS request). Null keeps the
        // RC.14 Dashboard without the card, so the Dashboard35E scenarios stay comparable with earlier captures.
        DashboardWeatherViewModel? weather = null;
        if (weatherState is not null)
        {
            var snapshot = weatherState switch
            {
                "Normal" => WeatherUiFixtures.Normal(),
                "Alerts" => WeatherUiFixtures.TwoAlerts(),
                "Stale" => WeatherUiFixtures.Stale(),
                "Unavailable" => WeatherUiFixtures.Unavailable(),
                "Evening" => WeatherUiFixtures.EveningForecast(),
                _ => null, // Loading
            };
            var weatherClock = new FakeClock(weatherState == "Evening" ? WeatherUiFixtures.Evening : WeatherUiFixtures.Morning);
            weather = new DashboardWeatherViewModel(new FakeWeatherMonitor { Current = snapshot }, weatherClock, new ImmediateUiDispatcher()) { IsExpanded = weatherExpanded };
        }

        Vm = new MainViewModel(
            new OperationsDashboardViewModel(dashboard, uiDispatcher: weather is null ? null : new ImmediateUiDispatcher(), weather: weather),
            new OperationsCalendarViewModel(CalendarService, clock, passPerksService: passPerks, calendarDisplay: calendarDisplay),
            new AssociateViewModel(AssociateService, clock, autosaveDelay: (_, _) => Task.CompletedTask,
                externalLinkLauncher: new Phase31CFixtures.NoOpLinkLauncher(), dealsService: Phase31CFixtures.Deals(), importantDetailsService: Phase31CFixtures.Details(), passPerksService: passPerks,
                // Phase 33: the real QuestPDF export (the default) rendered by the real PDFium rasterizer, inline and
                // undebounced so a capture shows the finished preview. In-memory layout preferences only.
                // Never the real profile's associate-passdown-template.json: the Preview-Modern/Compact
                // scenarios change the PDF style.
                templatePreferenceStore: new FakeAssociatePassdownTemplatePreferenceStore(),
                pdfRasterizer: new PdfiumPdfRasterizer(),
                pdfPrintService: new FakePdfPrintService(),
                pdfPreviewLayoutStore: new InMemoryPdfPreviewLayoutPreferenceStore(preview),
                previewWorkScheduler: new InlinePreviewWorkScheduler(),
                previewDelay: (_, _) => Task.CompletedTask),
            new OperationalNotesViewModel(notes, clock),
            new PassdownHistoryViewModel(history, new FakeFinalizedPassdownPdfRetrievalService()),
            new PersonalSyncSettingsViewModel(settingsService, new FakeSyncFolderPicker(), conflicts, coordinator, buildIdentity: build, diagnostics: diagnostics,
                appearance: new AppearanceSettingsViewModel(Appearance, motion), calendarDisplay: calendarDisplay),
            new PriceHistoryViewModel(priceHistory));
        Window = new MainWindow { DataContext = Vm };
    }
}

internal static class VisualFixtureButtonExtensions
{
    public static string? Name2(this Button button) => AutomationProperties.GetName(button);
}
