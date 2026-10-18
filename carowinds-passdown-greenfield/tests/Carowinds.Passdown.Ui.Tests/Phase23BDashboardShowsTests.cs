using System.Text.RegularExpressions;
using Carowinds.Passdown.Application.Changes;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Shows;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Infrastructure.Time;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 23B: the Dashboard's "Today's Shows" redesign (stacked name → times → venue entries, "+N more"
/// disclosure, in-memory expansion keyed by official show id) and the title-only Fury banner. Presentation,
/// view-model and markup assertions only — no real Window (those live in the run-alone
/// <see cref="Phase15ERealWindowLayoutTests"/>), so this class runs in the normal mixed Ui partition.
/// </summary>
public sealed class Phase23BDashboardShowsTests
{
    private static readonly DateTimeOffset At = Phase23BDashboardShowsFixtures.GeneratedAt; // 10:45 AM ET

    private static ShowSchedulePresentation Build(ShowScheduleView view) => ShowSchedulePresentation.Build(view, At, isDashboard: true);

    private static ShowRowPresentation Row(ShowSchedulePresentation presentation, string key) => presentation.Rows.Single(r => r.Key == key);

    private static DashboardShowRowViewModel RowViewModel(string key, bool expanded = false) =>
        new(Row(Build(Phase23BDashboardShowsFixtures.Scheduled(Phase23BDashboardShowsFixtures.Busy)), key), expanded, (_, _) => { });

    private static string Plain(string text) => text.Replace('\u00A0', ' ');

    // ---- Disclosure rule ---------------------------------------------------------------------------

    [Theory]
    [InlineData(1, false, 1, 0)]
    [InlineData(4, false, 4, 0)]
    [InlineData(5, false, 5, 0)]
    [InlineData(6, false, 4, 2)]
    [InlineData(14, false, 4, 10)]
    [InlineData(14, true, 14, 0)]
    [InlineData(6, true, 6, 0)]
    [InlineData(5, true, 5, 0)]
    public void Disclosure_ShowsShortListsWhole_AndCountsHiddenTimesExactly(int count, bool expanded, int visible, int hidden)
    {
        Assert.Equal(visible, ShowTimesDisclosure.VisibleCount(count, expanded));
        Assert.Equal(hidden, ShowTimesDisclosure.HiddenCount(count, expanded));
        Assert.Equal(count > ShowTimesDisclosure.CollapsedLimit, ShowTimesDisclosure.CanCollapse(count));
        Assert.Equal(count, ShowTimesDisclosure.VisibleCount(count, expanded) + ShowTimesDisclosure.HiddenCount(count, expanded));
    }

    [Fact]
    public void Disclosure_NeverOffersPlusOneMore()
    {
        for (var count = 1; count <= 40; count++)
        {
            var hidden = ShowTimesDisclosure.HiddenCount(count, isExpanded: false);
            Assert.True(hidden == 0 || hidden >= 2, $"{count} times would show '+{hidden} more'");
        }
    }

    [Fact]
    public void Disclosure_KeepsEachTimeTogetherOnAWrappedLine()
    {
        var joined = ShowTimesDisclosure.Join(["11:15 AM", "11:30 AM"]);

        Assert.Equal("11:15\u00A0AM · 11:30\u00A0AM", joined);
    }

    // ---- Presentation ------------------------------------------------------------------------------

    [Fact]
    public void Presentation_KeysRowsByOfficialShowId_AndListsEveryUpcomingTime()
    {
        var presentation = Build(Phase23BDashboardShowsFixtures.Scheduled(Phase23BDashboardShowsFixtures.Busy));

        var camp = Row(presentation, "syn-10");
        Assert.Equal("Synthetic Camp Meet and Greet", camp.Name);
        Assert.Equal(14, camp.Times.Count);
        Assert.Equal("11:15 AM", camp.Times[0]);
        Assert.Equal("2:30 PM", camp.Times[^1]);
        Assert.Equal(string.Join(" · ", camp.Times), camp.TimesText); // the Phase 21C text is unchanged
        Assert.Equal("Synthetic Camp Gathering Spot", camp.Venue);
        Assert.Equal(presentation.Rows.Count, presentation.Rows.Select(r => r.Key).Distinct().Count());
    }

    [Fact]
    public void Presentation_ShowWithoutAnId_IsKeyedByItsName()
    {
        var unnamed = new ScheduledShow("  ", " Synthetic Pop-Up Choir ", null, [new TimeOnly(15, 0)]);

        var row = Assert.Single(Build(Phase23BDashboardShowsFixtures.Scheduled(unnamed)).Rows);

        Assert.Equal("Synthetic Pop-Up Choir", row.Key);
    }

    [Fact]
    public void Presentation_LateNightTimes_StayAfterTheEveningOnes()
    {
        var presentation = Build(Phase23BDashboardShowsFixtures.Scheduled(Phase23BDashboardShowsFixtures.Busy));

        var lateNight = Row(presentation, "syn-18");
        Assert.Equal(["10:30 PM", "11:15 PM", "12:15 AM", "1:00 AM"], lateNight.Times);
        Assert.Equal("syn-18", presentation.Rows[^1].Key); // ordered by its next performance, the latest of the day
    }

    [Fact]
    public void Presentation_StartedPerformances_StayAnEarlierCount_NeverListedAsUpcoming()
    {
        var band = Row(Build(Phase23BDashboardShowsFixtures.Scheduled(Phase23BDashboardShowsFixtures.Busy)), "syn-14");

        Assert.Equal(["12:00 PM", "2:00 PM", "5:00 PM"], band.Times);
        Assert.Equal("+2 earlier", band.EarlierText);
        Assert.False(band.HasVenue);
    }

    [Fact]
    public void Presentation_RowsFollowTheExistingNextPerformanceOrder()
    {
        var presentation = Build(Phase23BDashboardShowsFixtures.Scheduled(Phase23BDashboardShowsFixtures.Busy));

        Assert.Equal(
            ["syn-16", "syn-19", "syn-10", "syn-11", "syn-12", "syn-14", "syn-13", "syn-15", "syn-17", "syn-21", "syn-20", "syn-18"],
            presentation.Rows.Select(r => r.Key));
    }

    [Theory]
    [InlineData(ShowScheduleState.NoShows, "No shows scheduled today.")]
    [InlineData(ShowScheduleState.NotPublished, "Today's show schedule is not published.")]
    [InlineData(ShowScheduleState.ParkClosed, "No shows — the park is closed today.")]
    [InlineData(ShowScheduleState.CouldNotCheck, "Couldn't check today's shows.")]
    [InlineData(ShowScheduleState.NotChecked, "Today's shows haven't been checked yet.")]
    public async Task Dashboard_EmptyStates_HaveNoShowRows_AndKeepTheirOwnWording(ShowScheduleState state, string expected)
    {
        var viewModel = await DashboardAsync(Phase23BDashboardShowsFixtures.Empty(state));

        Assert.Empty(viewModel.ShowRows);
        Assert.Equal(expected, viewModel.ShowsPresentation!.StatusText);
        Assert.Equal("Official schedule checked 6:05 AM ET", viewModel.ShowsPresentation.FreshnessText);
    }

    [Fact]
    public async Task Dashboard_FailedLatestCheck_KeepsTheLastKnownGoodRows_AndSaysTheCheckFailed()
    {
        var viewModel = await DashboardAsync(Phase23BDashboardShowsFixtures.FailedWithLastKnownGood());

        Assert.Equal(12, viewModel.ShowRows.Count);
        Assert.EndsWith("· latest check failed", viewModel.ShowsPresentation!.FreshnessText);
        Assert.True(viewModel.ShowsPresentation.IsProblem);
        Assert.Null(viewModel.ShowsPresentation.StatusText);
    }

    [Fact]
    public async Task Dashboard_StaleSchedule_ShowsItsOlderCheckedTime_NotAFreshOne()
    {
        var viewModel = await DashboardAsync(Phase23BDashboardShowsFixtures.Stale());

        Assert.Equal(4, viewModel.ShowRows.Count);
        Assert.StartsWith("Official schedule checked Aug 24", viewModel.ShowsPresentation!.FreshnessText);
    }

    // ---- Row view model ----------------------------------------------------------------------------

    [Fact]
    public void Row_LongList_StartsCollapsedWithFourTimesAndTheExactHiddenCount()
    {
        var row = RowViewModel("syn-10");

        Assert.True(row.CanExpand);
        Assert.False(row.IsExpanded);
        Assert.Equal("11:15 AM · 11:30 AM · 11:45 AM · 12:00 PM ·", Plain(row.VisibleTimesText));
        Assert.Equal(10, row.HiddenCount);
        Assert.Equal("+10 more", row.ToggleText);
        Assert.Equal("Show all 14 performance times for Synthetic Camp Meet and Greet", row.ToggleAccessibleName);
    }

    [Fact]
    public void Row_Expanded_ListsEveryTimeInOrder_AndOffersToCollapse()
    {
        var row = RowViewModel("syn-10");
        var changed = new List<string>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        row.IsExpanded = true;

        var times = Plain(row.VisibleTimesText).Split(" · ");
        Assert.Equal(row.Row.Times, times);
        Assert.Equal(0, row.HiddenCount);
        Assert.Equal("Show fewer", row.ToggleText);
        Assert.Equal("Show fewer performance times for Synthetic Camp Meet and Greet", row.ToggleAccessibleName);
        Assert.Contains(nameof(row.VisibleTimesText), changed);
        Assert.Contains(nameof(row.ToggleAccessibleName), changed);

        row.IsExpanded = false;
        Assert.Equal("+10 more", row.ToggleText);
    }

    [Fact]
    public void Row_ShortList_ShowsEveryTime_AndCannotExpand()
    {
        var row = RowViewModel("syn-11");

        Assert.False(row.CanExpand);
        Assert.Equal("11:30 AM · 1:00 PM · 2:30 PM · 4:00 PM", Plain(row.VisibleTimesText));
        Assert.Equal(0, row.HiddenCount);

        row.IsExpanded = true;
        Assert.False(row.IsExpanded);
    }

    [Fact]
    public void Row_TimesAccessibleName_AlwaysCarriesTheWholeSchedule()
    {
        var row = RowViewModel("syn-10");

        Assert.Equal($"Performance times for Synthetic Camp Meet and Greet: {string.Join(", ", row.Row.Times)}", row.TimesAccessibleName);
    }

    [Fact]
    public void Row_LongNamesAndVenues_AreKeptWhole_AndAMissingVenueIsOmitted()
    {
        var stunt = RowViewModel("syn-13");
        var acrobats = RowViewModel("syn-15");
        var band = RowViewModel("syn-14");

        Assert.Equal("Synthetic Pirate Stunt Spectacular with Extended Finale and Fireworks Preview", stunt.Name);
        Assert.Equal("The Exceptionally Long Synthetic Covered Pavilion Stage near the Carolina Harbor Entrance", acrobats.Venue);
        Assert.False(band.HasVenue);
        Assert.Null(band.Venue);
        Assert.Equal("+2 earlier", band.EarlierText);
    }

    // ---- Dashboard expansion state -----------------------------------------------------------------

    private static async Task<OperationsDashboardViewModel> DashboardAsync(ShowScheduleView view, FakeOperationsDashboardService? service = null, DataChangeNotifier? notifier = null)
    {
        service ??= new FakeOperationsDashboardService();
        Phase23BDashboardShowsFixtures.ApplyTo(service, () => view);
        var viewModel = new OperationsDashboardViewModel(service, dataChangeNotifier: notifier, uiDispatcher: new ImmediateUiDispatcher());
        await viewModel.PendingLoad;
        return viewModel;
    }

    private static DashboardShowRowViewModel ShowRow(OperationsDashboardViewModel viewModel, string key) => viewModel.ShowRows.Single(r => r.Key == key);

    [Fact]
    public async Task Dashboard_LongListsStartCollapsed_AndMultipleRowsExpandIndependently()
    {
        var viewModel = await DashboardAsync(Phase23BDashboardShowsFixtures.Scheduled(Phase23BDashboardShowsFixtures.Busy));

        Assert.Equal(["syn-16", "syn-19", "syn-10", "syn-12"], viewModel.ShowRows.Where(r => r.CanExpand).Select(r => r.Key));
        Assert.All(viewModel.ShowRows, r => Assert.False(r.IsExpanded));

        ShowRow(viewModel, "syn-10").IsExpanded = true;
        ShowRow(viewModel, "syn-12").IsExpanded = true;

        Assert.Equal(["syn-10", "syn-12"], viewModel.ShowRows.Where(r => r.IsExpanded).Select(r => r.Key));
    }

    [Fact]
    public async Task Dashboard_Expansion_SurvivesTheMinuteTick_AndALocalReload()
    {
        var notifier = new DataChangeNotifier();
        var viewModel = await DashboardAsync(Phase23BDashboardShowsFixtures.Scheduled(Phase23BDashboardShowsFixtures.Busy), notifier: notifier);
        ShowRow(viewModel, "syn-10").IsExpanded = true;

        viewModel.RefreshShowsForTime(At.AddMinutes(1));
        Assert.True(ShowRow(viewModel, "syn-10").IsExpanded);

        notifier.Publish(new DataChange(DataChangeKind.OfficialData));
        await viewModel.PendingLoad;
        Assert.True(ShowRow(viewModel, "syn-10").IsExpanded);
        Assert.Single(viewModel.ShowRows, r => r.IsExpanded);
    }

    [Fact]
    public async Task Dashboard_Expansion_FollowsTheShowId_NotItsPositionInTheList()
    {
        var notifier = new DataChangeNotifier();
        var shows = Phase23BDashboardShowsFixtures.Busy.ToArray();
        var view = Phase23BDashboardShowsFixtures.Scheduled(shows);
        var service = new FakeOperationsDashboardService();
        Phase23BDashboardShowsFixtures.ApplyTo(service, () => view);
        var viewModel = new OperationsDashboardViewModel(service, dataChangeNotifier: notifier, uiDispatcher: new ImmediateUiDispatcher());
        await viewModel.PendingLoad;
        var index = viewModel.ShowRows.ToList().FindIndex(r => r.Key == "syn-10");
        viewModel.ShowRows[index].IsExpanded = true;

        // A refresh moves the Camp show later in the day: another long show now sits at its old position.
        var moved = shows.Select(s => s.ShowId == "syn-10" ? s with { Times = s.Times.Select(t => t.AddHours(3)).ToList() } : s).ToArray();
        view = Phase23BDashboardShowsFixtures.Scheduled(moved);
        notifier.Publish(new DataChange(DataChangeKind.OfficialData));
        await viewModel.PendingLoad;

        Assert.NotEqual("syn-10", viewModel.ShowRows[index].Key);
        Assert.False(viewModel.ShowRows[index].IsExpanded);
        Assert.True(ShowRow(viewModel, "syn-10").IsExpanded);
        Assert.Equal("2:15 PM", ShowRow(viewModel, "syn-10").Row.Times[0]); // recalculated from the refreshed data
    }

    [Fact]
    public async Task Dashboard_RemovedShow_ForgetsItsExpansion_AndANewShowStartsCollapsed()
    {
        var notifier = new DataChangeNotifier();
        var view = Phase23BDashboardShowsFixtures.Scheduled(Phase23BDashboardShowsFixtures.Busy);
        var service = new FakeOperationsDashboardService();
        Phase23BDashboardShowsFixtures.ApplyTo(service, () => view);
        var viewModel = new OperationsDashboardViewModel(service, dataChangeNotifier: notifier, uiDispatcher: new ImmediateUiDispatcher());
        await viewModel.PendingLoad;
        ShowRow(viewModel, "syn-10").IsExpanded = true;

        var replacement = Phase23BDashboardShowsFixtures.Busy[0] with { ShowId = "syn-99", Name = "Synthetic Replacement Meet and Greet" };
        view = Phase23BDashboardShowsFixtures.Scheduled([replacement, .. Phase23BDashboardShowsFixtures.Busy.Skip(1)]);
        notifier.Publish(new DataChange(DataChangeKind.OfficialData));
        await viewModel.PendingLoad;
        Assert.False(ShowRow(viewModel, "syn-99").IsExpanded);

        view = Phase23BDashboardShowsFixtures.Scheduled(Phase23BDashboardShowsFixtures.Busy);
        notifier.Publish(new DataChange(DataChangeKind.OfficialData));
        await viewModel.PendingLoad;
        Assert.False(ShowRow(viewModel, "syn-10").IsExpanded);
    }

    [Fact]
    public async Task Dashboard_NewOperatingDate_StartsEveryShowCollapsed()
    {
        var notifier = new DataChangeNotifier();
        var view = Phase23BDashboardShowsFixtures.Scheduled(Phase23BDashboardShowsFixtures.Busy);
        var service = new FakeOperationsDashboardService();
        Phase23BDashboardShowsFixtures.ApplyTo(service, () => view);
        var viewModel = new OperationsDashboardViewModel(service, dataChangeNotifier: notifier, uiDispatcher: new ImmediateUiDispatcher());
        await viewModel.PendingLoad;
        ShowRow(viewModel, "syn-10").IsExpanded = true;

        // The same official show id runs again on the next operating date.
        view = view with { Date = view.Date.AddDays(1) };
        notifier.Publish(new DataChange(DataChangeKind.OfficialData));
        await viewModel.PendingLoad;

        Assert.All(viewModel.ShowRows, r => Assert.False(r.IsExpanded));
    }

    [Fact]
    public async Task Dashboard_Expanding_NeverReadsData_ChecksOnline_OrSavesADraft()
    {
        var dashboardService = new FakeOperationsDashboardService();
        Phase23BDashboardShowsFixtures.ApplyTo(dashboardService, () => Phase23BDashboardShowsFixtures.Scheduled(Phase23BDashboardShowsFixtures.Busy));
        var dashboard = new OperationsDashboardViewModel(dashboardService, uiDispatcher: new ImmediateUiDispatcher());
        var clock = new FakeClock(At);
        var associateService = new FakeAssociatePassdownService();
        var main = new MainViewModel(
            dashboard,
            new OperationsCalendarViewModel(new FakeOperationsCalendarService(), clock),
            new AssociateViewModel(associateService, clock, autosaveDelay: (_, _) => Task.CompletedTask),
            new OperationalNotesViewModel(new FakeOperationalNotesService(), new SystemClock()),
            new PassdownHistoryViewModel(new FakePassdownHistoryService(), new FakeFinalizedPassdownPdfRetrievalService()),
            new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator()));
        await dashboard.PendingLoad;
        await main.Associate.PendingLoad;
        var loads = dashboardService.LoadCalls.Count;
        var todays = dashboardService.GetTodayCallCount;
        var saves = associateService.SaveCalls.Count;

        foreach (var row in dashboard.ShowRows.Where(r => r.CanExpand))
        {
            row.IsExpanded = true;
            row.IsExpanded = false;
            row.IsExpanded = true;
        }

        await main.Associate.PendingAutosave;
        Assert.Equal(loads, dashboardService.LoadCalls.Count);
        Assert.Equal(todays, dashboardService.GetTodayCallCount);
        Assert.Equal(0, dashboardService.CheckOnlineCallCount);
        Assert.Equal(saves, associateService.SaveCalls.Count);
    }

    // ---- Markup ------------------------------------------------------------------------------------

    private static string Markup(string relativePath) =>
        Regex.Replace(File.ReadAllText(MarkupFiles.Find(relativePath)), "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    private static string DashboardShowsTemplate()
    {
        var markup = Markup("src/Carowinds.Passdown.Desktop/Views/DashboardView.axaml");
        var start = markup.IndexOf("x:DataType=\"vm:DashboardShowRowViewModel\"", StringComparison.Ordinal);
        Assert.True(start > 0, "Dashboard show row template missing");
        return markup[start..markup.IndexOf("</DataTemplate>", start, StringComparison.Ordinal)];
    }

    [Fact]
    public void DashboardMarkup_StacksNameThenTimesThenVenue_InReadingOrder()
    {
        var template = DashboardShowsTemplate();

        var name = template.IndexOf("Classes=\"showName\" Text=\"{Binding Name}\"", StringComparison.Ordinal);
        var times = template.IndexOf("Text=\"{Binding VisibleTimesText}\"", StringComparison.Ordinal);
        var toggle = template.IndexOf("<ToggleButton", StringComparison.Ordinal);
        var venue = template.IndexOf("Text=\"{Binding Venue}\"", StringComparison.Ordinal);
        Assert.True(name > 0 && name < times && times < toggle && toggle < venue, "reading order must be name, times, disclosure, venue");
        Assert.DoesNotContain("ColumnDefinitions", template); // no name · times · venue columns any more
        Assert.DoesNotContain("Orientation=\"Horizontal\" Spacing", template); // no unconstrained horizontal StackPanel
        Assert.Contains("<WrapPanel Orientation=\"Horizontal\">", template);
    }

    [Fact]
    public void DashboardMarkup_Disclosure_IsANamedToggleBoundToTheRowsState()
    {
        var template = DashboardShowsTemplate();
        var toggle = Regex.Match(template, "<ToggleButton[^>]*/>", RegexOptions.Singleline).Value;

        Assert.Contains("Classes=\"disclosureHeader showTimesToggle\"", toggle);
        Assert.Contains("IsChecked=\"{Binding IsExpanded}\"", toggle);
        Assert.Contains("IsVisible=\"{Binding CanExpand}\"", toggle);
        Assert.Contains("AutomationProperties.Name=\"{Binding ToggleAccessibleName}\"", toggle);
        Assert.Contains("Content=\"{Binding ToggleText}\"", toggle);
        Assert.DoesNotContain("PointerEntered", template); // nothing is revealed on hover
        Assert.Contains("AutomationProperties.Name=\"{Binding TimesAccessibleName}\"", template);
    }

    [Fact]
    public void DashboardMarkup_UsesTheColumnsPanel_AndKeepsFreshnessUntrimmed()
    {
        var markup = Markup("src/Carowinds.Passdown.Desktop/Views/DashboardView.axaml");

        Assert.Contains("ItemsSource=\"{Binding ShowRows}\"", markup);
        Assert.Contains("<controls:ShowColumnsPanel MinColumnWidth=\"340\" MaxColumns=\"3\" ColumnSpacing=\"24\" />", markup);
        var freshness = Regex.Match(markup, "<TextBlock Grid.Column=\"1\" Text=\"\\{Binding ShowsPresentation.FreshnessText\\}\"[^>]*/>", RegexOptions.Singleline).Value;
        Assert.Contains("TextWrapping=\"Wrap\"", freshness);
        Assert.DoesNotContain("TextTrimming", freshness);
        Assert.Contains("{Binding ShowsPresentation.StartedEarlierText}", markup);
    }

    [Fact]
    public void DashboardMarkup_ShowStyles_UseOnlySharedThemeTokens()
    {
        var markup = Markup("src/Carowinds.Passdown.Desktop/Views/DashboardView.axaml");
        var start = markup.IndexOf("<Style Selector=\"Border.dashboardShowRow\">", StringComparison.Ordinal);
        var end = markup.IndexOf("<Style Selector=\"TextBlock.statValue.unavailable\">", StringComparison.Ordinal);
        var styles = markup[start..end];

        Assert.DoesNotMatch("#[0-9A-Fa-f]{3,8}\\b", styles);
        Assert.Contains("{DynamicResource PassdownDividerBrush}", styles);
        Assert.Contains("{DynamicResource PassdownTextPrimaryBrush}", styles);
        // Venue and "+N earlier" are TextBlock.muted (PassdownTextSecondaryBrush); the toggle is the shared
        // disclosureHeader (PassdownAccentForegroundBrush) — both pairs are contrast-checked in both themes by
        // Phase22ADesignFoundationTests.KeyTokenPairs_MeetContrastTargets.
        Assert.Contains("Classes=\"muted showVenue\"", DashboardShowsTemplate());
    }

    // ---- Fury banner -------------------------------------------------------------------------------

    private static string Banner()
    {
        var markup = Markup("src/Carowinds.Passdown.Desktop/Views/MainWindow.axaml");
        var start = markup.IndexOf("<Border x:Name=\"BrandBanner\"", StringComparison.Ordinal);
        return markup[start..markup.IndexOf("</Border>\n", markup.IndexOf("BrandWordmark", start, StringComparison.Ordinal), StringComparison.Ordinal)];
    }

    [Fact]
    public void Banner_IsTitleOnly_WithNoChartMarkOrReplacementSymbol()
    {
        var banner = Banner();

        Assert.DoesNotContain("<Rectangle", banner);
        Assert.DoesNotContain("<PathIcon", banner);
        Assert.DoesNotContain("<Path ", banner);
        Assert.DoesNotContain("<StackPanel", banner);
        Assert.Single(Regex.Matches(banner, "<Image "));
        Assert.Contains("Source=\"/Assets/Branding/fury325-banner.jpg\"", banner);
        Assert.Matches("<TextBlock x:Name=\"BrandWordmark\" Classes=\"brandWordmark\" Text=\"CAROWINDS OPERATIONS\"\\s+Margin=\"24,0\"", banner);
    }

    [Fact]
    public void Banner_TitleSharesThePageContentLeftMargin()
    {
        var tokens = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Themes/PassdownTokens.axaml"));

        Assert.Contains("<Thickness x:Key=\"PassdownPagePadding\">24,", tokens);
        Assert.Contains("<Thickness x:Key=\"PassdownPageHeaderPadding\">24,", tokens);
    }

    [Fact]
    public void WindowAndTaskbarIcon_AreUnchanged()
    {
        var markup = Markup("src/Carowinds.Passdown.Desktop/Views/MainWindow.axaml");
        var project = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj"));

        Assert.Contains("Icon=\"/Assets/logo.ico\"", markup[..markup.IndexOf('>')]);
        Assert.True(File.Exists(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Assets/logo.ico")));
        Assert.Contains("<ApplicationIcon>Assets\\logo.ico</ApplicationIcon>", project);
    }
}
