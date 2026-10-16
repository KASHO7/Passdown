using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Infrastructure.Time;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 21A: Dashboard → Open Passdown always opens today's operational Eastern date, whatever date
/// Associate last showed; Associate's own date selection, Calendar routes and History keep their
/// explicit dates; and nothing reloads or retrieves when today is already selected.
/// </summary>
public class Phase21ADashboardOpenPassdownTests
{
    /// <summary>2026-10-16 10:00 Eastern.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 16);
    private static readonly DateOnly HistoryDate = new(2026, 10, 3);

    private sealed record Shell(
        MainViewModel Main,
        OperationsDashboardViewModel Dashboard,
        OperationsCalendarViewModel Calendar,
        AssociateViewModel Associate,
        FakeAssociatePassdownService AssociateService,
        PassdownHistoryViewModel History,
        FakeClock Clock);

    private static async Task<Shell> CreateAsync(DateTimeOffset? now = null)
    {
        var clock = new FakeClock(now ?? Now);
        var dashboard = new OperationsDashboardViewModel(new FakeOperationsDashboardService());
        var calendar = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), clock);
        var associateService = new FakeAssociatePassdownService();
        var associate = new AssociateViewModel(associateService, clock);
        var notes = new OperationalNotesViewModel(new FakeOperationalNotesService(), new SystemClock());
        var history = new PassdownHistoryViewModel(
            new FakePassdownHistoryService { Summaries = [new FinalizedPassdownSummary(HistoryDate, Now, FinalizationOrigin.Scheduled, true, 100)] },
            new FakeFinalizedPassdownPdfRetrievalService());
        var settings = new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator());
        var main = new MainViewModel(dashboard, calendar, associate, notes, history, settings);
        await dashboard.PendingLoad;
        await associate.PendingLoad;
        return new Shell(main, dashboard, calendar, associate, associateService, history, clock);
    }

    [Theory]
    [InlineData(-1)]  // yesterday
    [InlineData(7)]   // next week
    [InlineData(-31)] // last month
    public async Task OpenPassdown_FromAnyPreviouslySelectedDate_OpensTodayInEasternTime(int offsetDays)
    {
        var shell = await CreateAsync();
        shell.Associate.SelectedDate = Today.AddDays(offsetDays);
        await shell.Associate.PendingLoad;
        shell.Main.ShowDashboardSectionCommand.Execute(null);

        shell.Dashboard.OpenPassdownCommand.Execute(null);
        await shell.Associate.PendingLoad;

        Assert.Equal(Today, shell.Associate.SelectedDate);
        Assert.Equal(AppSection.AssociatePassdown, shell.Main.SelectedSection);
        Assert.Equal(AssociateSection.Overview, shell.Associate.SelectedSection);
        Assert.Equal(Today, shell.AssociateService.LoadCalls[^1]);
    }

    [Fact]
    public async Task OpenPassdown_UsesTheEasternDate_NotTheUtcOrHostDate()
    {
        // 2026-10-17 02:30 UTC is still 2026-10-16 22:30 in Eastern time.
        var shell = await CreateAsync(new DateTimeOffset(2026, 10, 17, 2, 30, 0, TimeSpan.Zero));
        shell.Associate.SelectedDate = new DateOnly(2026, 10, 17);
        await shell.Associate.PendingLoad;

        shell.Dashboard.OpenPassdownCommand.Execute(null);
        await shell.Associate.PendingLoad;

        Assert.Equal(new DateOnly(2026, 10, 16), shell.Associate.SelectedDate);
    }

    [Fact]
    public async Task OpenPassdown_AfterMidnightRollover_OpensTheNewEasternDate()
    {
        var shell = await CreateAsync(new DateTimeOffset(2026, 10, 17, 3, 50, 0, TimeSpan.Zero)); // 23:50 Eastern, Oct 16
        Assert.Equal(Today, shell.Associate.SelectedDate);

        shell.Clock.UtcNow = new DateTimeOffset(2026, 10, 17, 4, 10, 0, TimeSpan.Zero); // 00:10 Eastern, Oct 17
        shell.Dashboard.OpenPassdownCommand.Execute(null);
        await shell.Associate.PendingLoad;

        Assert.Equal(new DateOnly(2026, 10, 17), shell.Associate.SelectedDate);
    }

    [Fact]
    public async Task OpenPassdown_WhenTodayIsAlreadySelected_CausesNoRedundantLoadOrRetrieval()
    {
        var shell = await CreateAsync();
        var loadsBefore = shell.AssociateService.LoadCalls.Count;

        shell.Dashboard.OpenPassdownCommand.Execute(null);
        shell.Dashboard.OpenPassdownCommand.Execute(null);
        await shell.Associate.PendingLoad;

        Assert.Equal(loadsBefore, shell.AssociateService.LoadCalls.Count);
        Assert.Empty(shell.AssociateService.CheckOnlineCalls);
    }

    [Fact]
    public async Task AssociateOwnDateSelection_StaysStableAcrossOrdinaryNavigation()
    {
        var shell = await CreateAsync();
        var nextSaturday = new DateOnly(2026, 10, 24);
        shell.Associate.SelectedDate = nextSaturday;
        await shell.Associate.PendingLoad;

        shell.Main.ShowDashboardSectionCommand.Execute(null);
        shell.Main.ShowHistorySectionCommand.Execute(null);
        shell.Main.ShowAssociateSectionCommand.Execute(null);
        shell.Associate.SelectedSection = AssociateSection.Products;
        shell.Dashboard.OpenNotesCommand.Execute(null);

        Assert.Equal(nextSaturday, shell.Associate.SelectedDate);
    }

    [Fact]
    public async Task CalendarRoutes_KeepTheirExplicitDate()
    {
        var shell = await CreateAsync();
        await shell.Calendar.PendingLoad;
        var target = new DateOnly(2026, 10, 30);
        shell.Calendar.SelectDateCommand.Execute(target);
        await shell.Calendar.PendingLoad;

        shell.Calendar.ViewDatePricingCommand.Execute(null);
        await shell.Associate.PendingLoad;

        Assert.Equal(target, shell.Associate.SelectedDate);
        Assert.Equal(AssociateSection.Products, shell.Associate.SelectedSection);
    }

    [Fact]
    public async Task History_OpensTheRequestedRecord_NotToday()
    {
        var shell = await CreateAsync();
        await shell.Calendar.PendingLoad;
        shell.Calendar.SelectDateCommand.Execute(HistoryDate);
        await shell.Calendar.PendingLoad;

        shell.Calendar.OpenHistoryCommand.Execute(null);
        await shell.History.PendingSelectByDate;

        Assert.Equal(AppSection.History, shell.Main.SelectedSection);
        Assert.Equal(HistoryDate, shell.History.SelectedItem!.PassdownDate);
        Assert.Equal(Today, shell.Associate.SelectedDate); // History never moves Associate's date either
    }

    [Fact]
    public void HistoricalPricingNotice_SaysUnavailable_OrNamesTheDate_AndIsNullForCurrentDates()
    {
        var emptyPast = Workspace(new DateOnly(2026, 4, 18), AssociatePricingTimeframe.Historical, []);
        var recordedPast = Workspace(new DateOnly(2026, 8, 29), AssociatePricingTimeframe.Historical,
            [new AssociateProduct("dated|DatedAdmissionTickets|p", "Park Ticket", Domain.SourceCategory.DatedAdmissionTickets, false, 49m, null, null, null,
                true, new DateOnly(2026, 8, 29), AssociateVerificationInfo.NotCheckedYet, true, null)]);
        var current = Workspace(Today, AssociatePricingTimeframe.Current, []);

        Assert.Equal("Historical pricing not available for this date.", AssociateViewModel.BuildHistoricalPricingNotice(emptyPast));
        Assert.Equal(
            "Showing prices recorded for August 29, 2026. Current catalog products are not shown for past dates.",
            AssociateViewModel.BuildHistoricalPricingNotice(recordedPast));
        Assert.Null(AssociateViewModel.BuildHistoricalPricingNotice(current));
    }

    [Fact]
    public void ProductsMarkup_BindsTheHistoricalPricingNotice()
    {
        var markup = File.ReadAllText(FindRepoFile("src/Carowinds.Passdown.Desktop/Views/AssociateView.axaml"));

        Assert.Contains("IsVisible=\"{Binding HasHistoricalPricingNotice}\"", markup);
        Assert.Contains("Text=\"{Binding HistoricalPricingNotice}\"", markup);
    }

    private static string FindRepoFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate '{relativePath}' above {AppContext.BaseDirectory}.");
    }

    private static AssociateWorkspace Workspace(DateOnly date, AssociatePricingTimeframe timeframe, IReadOnlyList<AssociateProduct> products) =>
        new(date, AssociateDraftPayload.CreateDefault(), AssociateParkHoursComposer.Compose(null, AssociateDraftPayload.CreateDefault(), Now), products, [], AssociateSaveStatus.NeverSaved)
        {
            PricingTimeframe = timeframe,
        };
}
