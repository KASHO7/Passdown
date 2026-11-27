using System.Text.RegularExpressions;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Shows;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Infrastructure.Time;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 35E Dashboard follow-up: the approved Admissions Overview — presentation strings from the existing
/// snapshot, the new read-only navigation actions, the width-driven layout and the markup contracts. No real
/// Window and no bound-value reads from views (mixed Ui partition rule); all values are synthetic.
/// </summary>
public class Phase35EDashboardTests
{
    private static readonly DateOnly Date = FakeOperationsDashboardService.DefaultDate; // Aug 26, 2026
    private static readonly DateTimeOffset Generated = new(2026, 8, 26, 15, 0, 0, TimeSpan.Zero); // 11:00 AM EDT
    private static readonly AssociateVerificationInfo Verified = new(AssociateVerificationState.Verified, Generated.AddHours(-4), null, null);

    private static string Markup => File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/DashboardView.axaml")).Replace("\r\n", "\n");

    private static AssociateParkHoursView Hours(TimeOnly? northGate = null, TimeOnly? avenue = null) => new(
        ParkHoursCalculator.Calculate(false, new TimeOnly(11, 0), new TimeOnly(18, 0), false, null, null, false, null, null, northGate, avenue),
        Verified, false, new TimeOnly(11, 0), new TimeOnly(18, 0));

    private static OperationsDashboardSnapshot Snapshot(
        DashboardParkStatus parkStatus = DashboardParkStatus.Open,
        AssociateParkHoursView? hours = null,
        DashboardPassdownStatusSummary? passdown = null,
        ShowScheduleView? shows = null,
        DashboardPricingSummary? pricing = null) =>
        FakeOperationsDashboardService.DefaultSnapshot(Date) with
        {
            ParkStatus = new DashboardParkStatusSummary(parkStatus, hours ?? Hours()),
            PassdownStatus = passdown ?? new DashboardPassdownStatusSummary(DashboardPassdownStatus.DraftInProgress, Generated.AddHours(-1), null),
            GeneratedAt = Generated,
            TodayShows = shows ?? ShowScheduleView.NotCheckedFor(Date),
            Pricing = pricing ?? DashboardPricingSummary.Empty,
        };

    // ------------------------------------------------------------------ gate operations

    [Fact]
    public void Gates_ShowTheExistingEffectiveTimes_AndSayHowEachWasSet()
    {
        var p = OperationsDashboardPresentation.Build(Snapshot());

        Assert.Equal("11:00 AM – 6:00 PM", p.ParkHoursHeadlineText);
        Assert.Equal("Published park schedule", p.ParkHoursSupportText);
        Assert.Equal("10:15 AM", p.AvenueText);
        Assert.Equal("45 min before opening", p.AvenueSupportText);
        Assert.Equal("10:30 AM", p.NorthGateText);
        Assert.Equal("30 min before opening", p.NorthGateSupportText);
        Assert.Equal("Aug 26, 2026", p.OperatingDateShortText);
        Assert.Equal("Wednesday, August 26, 2026", p.OperatingDayLabel);
    }

    [Fact]
    public void Gates_AManualEntranceOverride_IsLabelledManual_NeverAsCalculated()
    {
        var p = OperationsDashboardPresentation.Build(Snapshot(hours: Hours(northGate: new TimeOnly(10, 0), avenue: new TimeOnly(9, 50))));

        Assert.Equal("9:50 AM", p.AvenueText);
        Assert.Equal("Manual time for today", p.AvenueSupportText);
        Assert.Equal("10:00 AM", p.NorthGateText);
        Assert.Equal("Manual time for today", p.NorthGateSupportText);
    }

    [Theory]
    [InlineData(DashboardParkStatus.NotChecked, "Hours not checked yet")]
    [InlineData(DashboardParkStatus.NotPublished, "Hours not published")]
    [InlineData(DashboardParkStatus.Unavailable, "Hours unavailable")]
    public void Gates_WithoutPublishedHours_NeverInventATime(DashboardParkStatus status, string headline)
    {
        var p = OperationsDashboardPresentation.Build(FakeOperationsDashboardService.DefaultSnapshot(Date) with
        {
            ParkStatus = new DashboardParkStatusSummary(status, FakeOperationsDashboardService.DefaultSnapshot(Date).ParkStatus.Hours),
        });

        Assert.Equal(headline, p.ParkHoursHeadlineText);
        Assert.Null(p.ParkHoursSupportText);
        Assert.Equal(OperationsDashboardPresentation.NoGateTimeText, p.AvenueText);
        Assert.Equal(OperationsDashboardPresentation.NoGateTimeText, p.NorthGateText);
        Assert.Equal("Needs published park hours", p.NorthGateSupportText);
    }

    [Fact]
    public void Gates_EarlyRideTime_IsTheResolverValue_RidesListed()
    {
        var p = OperationsDashboardPresentation.Build(Snapshot() with
        {
            EarlyRideTime = new EffectiveErt(EffectiveErtKind.Configured, EffectiveErtSource.Monthly, null, ["Copperhead Strike", "Mountain Gliders"], null),
        });

        Assert.Equal("Copperhead Strike, Mountain Gliders", p.EarlyRideTimeText);
    }

    // ------------------------------------------------------------------ passdown card

    [Fact]
    public void Passdown_Draft_IsNeutral_SavedLocally_NeverSynced()
    {
        var p = OperationsDashboardPresentation.Build(Snapshot());

        Assert.Equal("Draft In Progress", p.PassdownStatusText);
        Assert.Equal("Last saved locally 10:00 AM ET", p.LastSavedLocallyText);
        Assert.Equal("Autosaves on this computer as you edit", p.PassdownStatusNoteText);
        Assert.Null(p.PassdownCardDetailText); // the save time is not repeated
        Assert.DoesNotContain("sync", p.LastSavedLocallyText!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("final", p.PassdownStatusNoteText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Passdown_NoDraft_AndFinalized_SayExactlyThat()
    {
        var none = OperationsDashboardPresentation.Build(Snapshot(passdown: new DashboardPassdownStatusSummary(DashboardPassdownStatus.NoDraft, null, null)));
        Assert.Equal("No Draft", none.PassdownStatusText);
        Assert.Null(none.LastSavedLocallyText);
        Assert.Equal("Nothing has been entered for today yet.", none.PassdownCardDetailText);

        var finalized = OperationsDashboardPresentation.Build(Snapshot(passdown: new DashboardPassdownStatusSummary(DashboardPassdownStatus.Finalized, Generated, Generated)));
        Assert.Equal("Finalized", finalized.PassdownStatusText);
        Assert.Null(finalized.LastSavedLocallyText);
        Assert.Equal("Finalized record is read-only in History", finalized.PassdownStatusNoteText);
        Assert.False(finalized.ShowOpenPassdownAction);
    }

    [Fact]
    public void Passdown_FiguresNotEntered_StayNotEntered_NeverZero()
    {
        var p = OperationsDashboardPresentation.Build(Snapshot());

        Assert.Equal("Not entered", p.AttendanceText);
        Assert.Equal("Not entered", p.PreSoldsText);
    }

    // ------------------------------------------------------------------ shows summary

    [Fact]
    public void Shows_SummaryCountsTheRecordedOfficialSchedule_OnlyWhenScheduled()
    {
        var scheduled = new ShowScheduleView(Date, ShowScheduleState.Scheduled,
        [
            new ScheduledShow("a", "Synthetic Revue", "Stage", [new TimeOnly(12, 0), new TimeOnly(14, 0)]),
            new ScheduledShow("b", "Synthetic Parade", null, [new TimeOnly(16, 0)]),
        ], false, Generated, Generated);

        Assert.Equal("2 scheduled shows · 3 performances", OperationsDashboardPresentation.Build(Snapshot(shows: scheduled)).ShowsSummaryText);
        Assert.Null(OperationsDashboardPresentation.Build(Snapshot(shows: scheduled with { State = ShowScheduleState.NotPublished, Shows = [] })).ShowsSummaryText);
        Assert.Null(OperationsDashboardPresentation.Build(Snapshot()).ShowsSummaryText);
    }

    // ------------------------------------------------------------------ pricing card

    [Fact]
    public void Pricing_RowsAreOfficialAmounts_WithTheOldestVerificationTime()
    {
        var summary = new DashboardPricingSummary(
        [
            new("Park Ticket", 50m, ProductDisplaySection.DailyAdmission, AssociateVerificationState.Verified, Generated.AddHours(-3)),
            new("Fast Lane Reserve", 45m, ProductDisplaySection.FastLane, AssociateVerificationState.Verified, Generated.AddHours(-2)),
        ], TotalCount: 4);

        var p = OperationsDashboardPresentation.Build(Snapshot(pricing: summary)).Pricing;

        Assert.Equal([("Park Ticket", "$50.00"), ("Fast Lane Reserve", "$45.00")], p.Rows.Select(r => (r.ProductName, r.PriceText)));
        Assert.Equal("Verified 8:00 AM ET", p.FreshnessText);
        Assert.Equal("2 more in Products", p.MoreText);
        Assert.False(p.IsStale);
    }

    [Fact]
    public void Pricing_AStalePrice_SaysSoInVisibleText()
    {
        var summary = new DashboardPricingSummary(
            [new("Park Ticket", 50m, ProductDisplaySection.DailyAdmission, AssociateVerificationState.Stale, Generated.AddDays(-2))], 1);

        var p = OperationsDashboardPresentation.Build(Snapshot(pricing: summary)).Pricing;

        Assert.True(p.IsStale);
        Assert.True(p.Rows[0].IsStale);
        Assert.Contains("may be out of date", p.FreshnessText);
        Assert.Contains("may be out of date", p.Rows[0].AccessibleName);
    }

    [Fact]
    public void Pricing_WithNoDatedRows_FallsBackToTheHonestAdmissionReference()
    {
        var p = OperationsDashboardPresentation.Build(Snapshot());

        Assert.True(p.Pricing.HasNoRows);
        Assert.Equal(DashboardAdmissionPresentation.NotAvailableText, p.Admission.PriceText);
        Assert.False(p.Admission.HasPrice);
    }

    // ------------------------------------------------------------------ navigation (read-only, no retrieval)

    private static (MainViewModel Main, OperationsDashboardViewModel Dashboard, OperationsCalendarViewModel Calendar, AssociateViewModel Associate,
        FakeOperationsDashboardService DashboardService, FakeOperationsCalendarService CalendarService, FakeAssociatePassdownService AssociateService) CreateMain()
    {
        // The computer's clock/zone never matters: the clock is UTC 02:30 on Aug 27, which is still Aug 26 in Carowinds.
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 27, 2, 30, 0, TimeSpan.Zero));
        var dashboardService = new FakeOperationsDashboardService();
        var dashboard = new OperationsDashboardViewModel(dashboardService);
        var calendarService = new FakeOperationsCalendarService();
        var calendar = new OperationsCalendarViewModel(calendarService, clock);
        var associateService = new FakeAssociatePassdownService();
        var associate = new AssociateViewModel(associateService, clock);
        var main = new MainViewModel(
            dashboard,
            calendar,
            associate,
            new OperationalNotesViewModel(new FakeOperationalNotesService(), new SystemClock()),
            new PassdownHistoryViewModel(new FakePassdownHistoryService(), new FakeFinalizedPassdownPdfRetrievalService()),
            new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator()));
        return (main, dashboard, calendar, associate, dashboardService, calendarService, associateService);
    }

    [Fact]
    public async Task ViewInCalendar_SelectsTheDashboardsOperatingDate_AndLeavesThePassdownDateAlone()
    {
        var (main, dashboard, calendar, associate, dashboardService, calendarService, _) = CreateMain();
        await dashboard.PendingLoad;
        await associate.PendingLoad;
        associate.SelectedDate = Date.AddDays(-5);
        await associate.PendingLoad;
        await calendar.PendingLoad;
        calendar.SelectDateCommand.Execute(Date.AddMonths(2));
        await calendar.PendingLoad;

        Assert.True(dashboard.OpenCalendarCommand.CanExecute(null));
        dashboard.OpenCalendarCommand.Execute(null);
        await calendar.PendingLoad;

        Assert.Equal(AppSection.OperationsCalendar, main.SelectedSection);
        Assert.Equal(Date, calendar.SelectedDate);
        Assert.Equal((new DateOnly(2026, 8, 1), Date), calendarService.LoadCalls[^1]);
        Assert.Equal(Date.AddDays(-5), associate.SelectedDate);
        Assert.Equal(0, dashboardService.CheckOnlineCallCount);
    }

    [Theory]
    [InlineData("Shows", AssociateSection.Shows)]
    [InlineData("PassPerks", AssociateSection.Products)]
    public async Task ViewShows_AndPassPerksDetails_OpenTodaysEasternPassdown_OnTheirSection(string action, AssociateSection expected)
    {
        var (main, dashboard, _, associate, dashboardService, _, associateService) = CreateMain();
        await dashboard.PendingLoad;
        await associate.PendingLoad;
        associate.SelectedDate = Date.AddDays(7);
        await associate.PendingLoad;

        (action == "Shows" ? dashboard.OpenShowsCommand : dashboard.OpenPassPerksCommand).Execute(null);
        await associate.PendingLoad;

        Assert.Equal(AppSection.AssociatePassdown, main.SelectedSection);
        Assert.Equal(expected, associate.SelectedSection);
        Assert.Equal(Date, associate.SelectedDate); // Eastern Aug 26 although the UTC date is already Aug 27
        Assert.Equal(Date, associateService.LoadCalls[^1]);
        Assert.Equal(0, dashboardService.CheckOnlineCallCount);
        Assert.Empty(associateService.CheckOnlineCalls);
    }

    [Fact]
    public async Task NavigationActions_AreUnavailableUntilTodaysSummaryHasLoaded()
    {
        var service = new FakeOperationsDashboardService { Gate = new TaskCompletionSource<bool>() };
        var dashboard = new OperationsDashboardViewModel(service);

        Assert.False(dashboard.OpenShowsCommand.CanExecute(null));
        Assert.False(dashboard.OpenCalendarCommand.CanExecute(null));
        Assert.False(dashboard.OpenPassPerksCommand.CanExecute(null));

        service.Gate.SetResult(true);
        await dashboard.PendingLoad;

        Assert.True(dashboard.OpenShowsCommand.CanExecute(null));
        Assert.True(dashboard.OpenCalendarCommand.CanExecute(null));
        Assert.True(dashboard.OpenPassPerksCommand.CanExecute(null));
    }

    [Fact]
    public async Task OpeningAndUsingTheDashboard_NeverChecksOnline()
    {
        var service = new FakeOperationsDashboardService();
        var dashboard = new OperationsDashboardViewModel(service);
        await dashboard.PendingLoad;

        dashboard.IsSourceDetailsExpanded = true;
        dashboard.OpenShowsCommand.Execute(null);
        dashboard.OpenPassPerksCommand.Execute(null);
        dashboard.OpenCalendarCommand.Execute(null);
        dashboard.OpenPricingCommand.Execute(null);
        dashboard.OpenOperationalNotesCommand.Execute(null);
        dashboard.OpenHistoryCommand.Execute(null);

        Assert.Equal(0, service.CheckOnlineCallCount);
        Assert.Equal(1, service.GetTodayCallCount);
    }

    // ------------------------------------------------------------------ layout

    [Theory]
    [InlineData(1872, DashboardLayoutMode.Wide)] // 1920 wide, sidebar collapsed
    [InlineData(1000, DashboardLayoutMode.Wide)]
    [InlineData(999, DashboardLayoutMode.Medium)]
    [InlineData(694, DashboardLayoutMode.Medium)] // 960×640 with the sidebar expanded
    [InlineData(679, DashboardLayoutMode.Narrow)]
    public void Layout_FollowsTheReferenceBreakpoints(double contentWidth, DashboardLayoutMode expected)
    {
        Assert.Equal(expected, DashboardView.ModeForContentWidth(contentWidth));
    }

    // ------------------------------------------------------------------ markup

    [Fact]
    public void Markup_FollowsTheApprovedCardStructure_WithTokenColoursOnly()
    {
        var markup = Markup;

        Assert.Contains("Text=\"Admissions Overview\"", markup);
        Assert.Equal(4, Regex.Matches(markup, "Classes=\"gateColumn").Count);
        Assert.Equal(7, Regex.Matches(markup, "Classes=\"cardRule").Count); // gate, passdown, shows, weather (Phase 37C), pricing, notes, perks
        // Phase 37C (approved D4 mock-up): the Weather card is the first card in the right-hand column, with the teal rule.
        var secondary = markup.IndexOf("x:Name=\"DashboardSecondaryColumn\"", StringComparison.Ordinal);
        Assert.True(secondary > 0);
        Assert.True(markup.IndexOf("x:Name=\"WeatherCard\"", secondary, StringComparison.Ordinal) < markup.IndexOf("AutomationProperties.Name=\"Daily admission\"", secondary, StringComparison.Ordinal));
        Assert.Contains("Classes=\"cardRule teal\"", markup[markup.IndexOf("x:Name=\"WeatherCard\"", StringComparison.Ordinal)..]);
        Assert.Contains("Classes=\"cardRule teal\"", markup);
        Assert.Contains("Classes=\"cardRule slate\"", markup);
        Assert.Contains("Classes=\"cardRule amber\"", markup);
        Assert.DoesNotMatch("#[0-9A-Fa-f]{6}\\b", markup);
        Assert.DoesNotContain("ShowColumnsPanel", markup);
    }

    [Fact]
    public void Markup_HasNoFabricatedTelemetryOrDecorativeStatusDots()
    {
        var markup = Markup;

        foreach (var forbidden in new[] { "Ellipse", "status-dot", "statusDot", "%\"", "throughput", "Staffing", "health score", "7:44", "6,000", "$50" })
        {
            Assert.DoesNotContain(forbidden, markup, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Markup_EveryLinkAndButton_IsNamedByItsVisibleLabel_AndHasACommand()
    {
        var buttons = Regex.Matches(Markup, "<Button Content=\"(?<label>[^\"{]+)\"[^>]*>", RegexOptions.Singleline);

        Assert.NotEmpty(buttons);
        foreach (Match button in buttons)
        {
            var label = button.Groups["label"].Value;
            var name = Regex.Match(button.Value, "AutomationProperties.Name=\"(?<name>[^\"]+)\"").Groups["name"].Value;
            Assert.Contains(label, name, StringComparison.OrdinalIgnoreCase);
            Assert.Matches("Command=\"\\{Binding \\w+Command\\}\"", button.Value);
        }
    }

    [Fact]
    public void Markup_PassPerksCard_StatesItIsAPublicListing_InVisibleText()
    {
        var markup = Markup;
        var start = markup.IndexOf("AutomationProperties.Name=\"Pass Perks\"", StringComparison.Ordinal);
        var card = markup[start..markup.IndexOf("</Border>\n            </StackPanel>\n          </Grid>", start, StringComparison.Ordinal)];

        Assert.Contains("Text=\"{x:Static presentation:DashboardPassPerksPresentation.PublicListingNote}\"", card);
        Assert.Contains("ItemsSource=\"{Binding Dates}\"", card);
        Assert.Contains("Command=\"{Binding OpenPassPerksCommand}\"", card);
    }

    [Fact]
    public void Markup_DraftStatus_IsNeutralText_NotAWarningStyle()
    {
        var line = Regex.Match(Markup, "<TextBlock Grid.Column=\"0\" Text=\"\\{Binding Presentation.PassdownStatusText\\}\"[^>]*/>", RegexOptions.Singleline).Value;

        Assert.NotEmpty(line);
        Assert.DoesNotContain("statusWarning", line);
        Assert.DoesNotContain("statusError", line);
        Assert.DoesNotContain("Accent", line);
    }
}
