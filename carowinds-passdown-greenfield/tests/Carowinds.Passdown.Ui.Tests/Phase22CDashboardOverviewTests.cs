using System.Text.RegularExpressions;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Infrastructure.Time;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 22C: the Dashboard's Operations Overview — presentation strings, view-model state, navigation
/// and markup structure. No real Window and no bound-value reads from views (mixed Ui partition); the
/// realized geometry and keyboard checks are in <see cref="Phase15ERealWindowLayoutTests"/>.
/// </summary>
public class Phase22CDashboardOverviewTests
{
    private static readonly DateOnly Today = new(2026, 10, 16);
    private static readonly DateTimeOffset Afternoon = Phase22CDashboardFixtures.AfternoonOf(Today); // 2:00 PM EDT
    private static readonly DateTimeOffset MorningCheck = Phase22CDashboardFixtures.MorningCheckOf(Today); // 6:05 AM EDT

    private static string DashboardMarkup => File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/DashboardView.axaml")).Replace("\r\n", "\n");

    private static string StylesMarkup => File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Themes/PassdownStyles.axaml"));

    // ------------------------------------------------------------------ daily admission

    [Fact]
    public void Admission_ExactDatedPrice_NamesTheProduct_SaysForToday_AndCarriesItsOwnEasternTimestamp()
    {
        var p = DashboardAdmissionPresentation.Build(Phase22CDashboardFixtures.ExactAdmission(Today, MorningCheck), Afternoon);

        Assert.True(p.HasPrice);
        Assert.Equal("Park Ticket", p.ProductName);
        Assert.Equal("$64.99", p.PriceText);
        Assert.Equal("Official price for today", p.QualifierText);
        Assert.Equal("Verified 6:05 AM ET", p.FreshnessText);
        Assert.False(p.IsStale);
        Assert.Contains("Park Ticket", p.AutomationText);
        Assert.Contains("$64.99", p.AutomationText);
    }

    [Fact]
    public void Admission_StartingFromPrice_IsLabeledFrom_AndAdvertised_NeverAsToday()
    {
        var summary = Phase22CDashboardFixtures.ExactAdmission(Today, MorningCheck) with { Kind = DashboardAdmissionKind.StartingFrom, PriceAmount = 44.99m, ApplicableDate = null };

        var p = DashboardAdmissionPresentation.Build(summary, Afternoon);

        Assert.Equal("From $44.99", p.PriceText);
        Assert.Equal("Advertised starting price", p.QualifierText);
        Assert.DoesNotContain("today", p.QualifierText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Admission_StalePrice_SaysSoInVisibleText_NotOnlyInATooltip()
    {
        var summary = Phase22CDashboardFixtures.ExactAdmission(Today, MorningCheck.AddDays(-1)) with { PriceState = AssociateVerificationState.Stale };

        var p = DashboardAdmissionPresentation.Build(summary, Afternoon);

        Assert.True(p.IsStale);
        Assert.Equal("Last verified Oct 15, 6:05 AM ET — may be out of date", p.FreshnessText);
    }

    [Theory]
    [InlineData(AssociateVerificationState.NotPublished, DashboardAdmissionUnavailableReason.NoProduct, "Today's dated prices are not published yet")]
    [InlineData(AssociateVerificationState.Unavailable, DashboardAdmissionUnavailableReason.NoProduct, "No official dated price for today")]
    [InlineData(AssociateVerificationState.Error, DashboardAdmissionUnavailableReason.NoProduct, "Dated prices could not be checked")]
    [InlineData(AssociateVerificationState.NotCheckedYet, DashboardAdmissionUnavailableReason.NoProduct, "Dated prices have not been checked yet")]
    [InlineData(AssociateVerificationState.Verified, DashboardAdmissionUnavailableReason.Ambiguous, "More than one Park Ticket price is listed for today")]
    [InlineData(AssociateVerificationState.Verified, DashboardAdmissionUnavailableReason.Historical, "Today's prices are not shown for past dates")]
    public void Admission_NoPrice_SaysWhyPlainly_AndOnlyEverClaimsALastCheck(AssociateVerificationState state, DashboardAdmissionUnavailableReason reason, string expected)
    {
        var summary = Phase22CDashboardFixtures.UnavailableAdmission(state, MorningCheck) with { UnavailableReason = reason };

        var p = DashboardAdmissionPresentation.Build(summary, Afternoon);

        Assert.False(p.HasPrice);
        Assert.False(p.HasProductName);
        Assert.Equal(DashboardAdmissionPresentation.NotAvailableText, p.PriceText);
        Assert.Equal(expected, p.QualifierText);
        Assert.Equal("Last checked 6:05 AM ET", p.FreshnessText); // attempt-only: never "Verified"
        Assert.DoesNotContain("$", p.PriceText);
    }

    [Fact]
    public void Admission_Timestamps_AreEastern_NearMidnight()
    {
        // 02:30 UTC Oct 17 is 10:30 PM EDT Oct 16; "now" 03:00 UTC is the same Eastern day.
        var verified = new DateTimeOffset(2026, 10, 17, 2, 30, 0, TimeSpan.Zero);
        var p = DashboardAdmissionPresentation.Build(Phase22CDashboardFixtures.ExactAdmission(Today, verified), new DateTimeOffset(2026, 10, 17, 3, 0, 0, TimeSpan.Zero));

        Assert.Equal("Verified 10:30 PM ET", p.FreshnessText);
    }

    [Fact]
    public void DashboardPresentation_BuildsTheAdmissionFromTheSnapshot_AndACompactDateCaption()
    {
        var p = OperationsDashboardPresentation.Build(Phase22CDashboardFixtures.VerifiedOperatingDay(Today));

        Assert.Equal("$64.99", p.Admission.PriceText);
        Assert.Equal("Friday, October 16, 2026", p.OperatingDayLabel);
        Assert.Equal("FRIDAY, OCTOBER 16, 2026", p.OperatingDayCaption);
    }

    [Fact]
    public void SnapshotWithoutAnAdmissionValue_NeverFabricatesAPrice()
    {
        var p = OperationsDashboardPresentation.Build(FakeOperationsDashboardService.DefaultSnapshot(Today));

        Assert.False(p.Admission.HasPrice);
        Assert.Equal("Price not available", p.Admission.PriceText);
    }

    // ------------------------------------------------------------------ park hours and passdown

    [Fact]
    public void ParkHours_OpenClosedUnpublishedAndStale_UseTheExistingStatusModel()
    {
        var open = OperationsDashboardPresentation.Build(Phase22CDashboardFixtures.VerifiedOperatingDay(Today));
        Assert.Equal("10:00 AM – 10:00 PM", open.ParkHours.HeadlineText);
        Assert.Equal("Open", open.ParkStatusBadgeText);
        Assert.StartsWith("Verified", open.ParkHours.VerificationText);

        var closed = OperationsDashboardPresentation.Build(Phase22CDashboardFixtures.ClosedPark(Today));
        Assert.Equal("Park is closed", closed.ParkHours.HeadlineText);
        Assert.Equal("Closed", closed.ParkStatusBadgeText);

        var unpublished = OperationsDashboardPresentation.Build(Phase22CDashboardFixtures.NoPublishedData(Today));
        Assert.Equal("Not Published", unpublished.ParkStatusBadgeText);
        Assert.Equal("Hours not published", unpublished.ParkHoursHeadlineText); // never "not checked"
        Assert.Equal("10:00 AM – 10:00 PM", open.ParkHoursHeadlineText);
        Assert.Equal("Park is closed", closed.ParkHoursHeadlineText);
        Assert.Equal("Hours not checked yet", OperationsDashboardPresentation.Build(FakeOperationsDashboardService.DefaultSnapshot(Today)).ParkHoursHeadlineText);
        var unavailable = Phase22CDashboardFixtures.NoPublishedData(Today);
        unavailable = unavailable with { ParkStatus = unavailable.ParkStatus with { Status = DashboardParkStatus.Unavailable } };
        Assert.Equal("Hours unavailable", OperationsDashboardPresentation.Build(unavailable).ParkHoursHeadlineText);
        Assert.Equal("Official hours are not published yet for this date.", unpublished.ParkHours.VerificationText);

        var stale = OperationsDashboardPresentation.Build(Phase22CDashboardFixtures.StaleAfterFailure(Today));
        Assert.NotNull(stale.ParkHours.StaleNoteText); // last-known-good is never shown as freshly checked
        Assert.Contains("may be out of date", stale.ParkHours.VerificationText);
    }

    [Theory]
    [InlineData(DashboardPassdownStatus.NoDraft, "No Draft", true)]
    [InlineData(DashboardPassdownStatus.DraftInProgress, "Draft In Progress", true)]
    [InlineData(DashboardPassdownStatus.Finalized, "Finalized", false)]
    public void Passdown_ShowsTheRealLifecycleState(DashboardPassdownStatus status, string expected, bool showOpen)
    {
        var snapshot = Phase22CDashboardFixtures.VerifiedOperatingDay(Today) with
        {
            PassdownStatus = new DashboardPassdownStatusSummary(status, MorningCheck, status == DashboardPassdownStatus.Finalized ? MorningCheck : null),
        };

        var p = OperationsDashboardPresentation.Build(snapshot);

        Assert.Equal(expected, p.PassdownStatusText);
        Assert.Equal(showOpen, p.ShowOpenPassdownAction);
    }

    // ------------------------------------------------------------------ operational notes

    [Theory]
    [InlineData(0, 0, false, "No active notes today")]
    [InlineData(1, 0, false, "1 active note")]
    [InlineData(5, 2, false, "5 active notes · 2 important")]
    [InlineData(0, 0, true, "Unable to load operational notes")]
    public void OperationalNotes_SummaryIsTruthful_ImportantOnlyWhenMarked(int count, int important, bool failed, string expected)
    {
        var summary = new DashboardOperationalNotesSummary(count, important, [], failed);

        Assert.Equal(expected, DashboardOperationalNotesPresentation.Build(summary, Today).SummaryText);
    }

    // ------------------------------------------------------------------ official data

    [Fact]
    public void OfficialData_CompactSummary_UsesTheRealCounts_AndNamesGenuineProblems()
    {
        var p = OperationsDashboardPresentation.Build(Phase22CDashboardFixtures.BusyDayWithPartialFailure(Today)).DataHealth;

        Assert.Equal("4 current · 1 stale · 1 unavailable · 1 not published", p.SummaryText);
        Assert.Equal("Needs attention: Fast Lane unavailable · Promotions stale", p.AttentionText);
        Assert.True(p.HasAttention);
        Assert.Equal("Some information is stale", p.OverallHeadline);
    }

    [Fact]
    public void OfficialData_NotPublished_IsInformational_NotAnError()
    {
        var p = OperationsDashboardPresentation.Build(Phase22CDashboardFixtures.VerifiedOperatingDay(Today)).DataHealth;

        Assert.Equal("6 current · 1 not published", p.SummaryText);
        Assert.Null(p.AttentionText);
        Assert.Equal("Current", p.OverallHeadline);
    }

    [Fact]
    public void OfficialData_VerificationIssue_IsNamedDistinctlyFromUnavailableAndStale()
    {
        var p = OperationsDashboardPresentation.Build(Phase22CDashboardFixtures.StaleAfterFailure(Today)).DataHealth;

        Assert.Equal("Needs attention: Park Hours stale · Daily Admission stale · Fast Lane could not be verified", p.AttentionText);
        Assert.Equal("4 current · 2 stale · 1 unverified", p.SummaryText);
        Assert.Equal("Verification issue", p.OverallHeadline);
    }

    [Fact]
    public void OfficialData_NothingPublished_NeverClaimsCurrent()
    {
        var p = OperationsDashboardPresentation.Build(Phase22CDashboardFixtures.NoPublishedData(Today)).DataHealth;

        Assert.Equal("No published data yet", p.OverallHeadline);
        Assert.StartsWith("0 current", p.SummaryText);
        Assert.Null(p.AttentionText);
    }

    // ------------------------------------------------------------------ view model

    [Fact]
    public async Task SourceDetails_AreCollapsedByDefault_AndExpandingNeverReadsOrRetrieves()
    {
        var service = new FakeOperationsDashboardService { SnapshotFactory = Phase22CDashboardFixtures.BusyDayWithPartialFailure };
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;
        var (reads, checks, deep) = (service.GetTodayCallCount, service.CheckOnlineCallCount, service.DeepRefreshCallCount);

        Assert.False(viewModel.IsSourceDetailsExpanded);
        Assert.Equal("View Source Details", viewModel.SourceDetailsToggleText);

        viewModel.IsSourceDetailsExpanded = true;
        Assert.Equal("Hide Source Details", viewModel.SourceDetailsToggleText);
        viewModel.IsSourceDetailsExpanded = false;

        Assert.Equal(reads, service.GetTodayCallCount);
        Assert.Equal(checks, service.CheckOnlineCallCount);
        Assert.Equal(deep, service.DeepRefreshCallCount);
    }

    [Fact]
    public async Task LocalChange_ReloadsTheOverview_IncludingTheAdmissionPrice()
    {
        var service = new FakeOperationsDashboardService { SnapshotFactory = Phase22CDashboardFixtures.NoPublishedData };
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;
        Assert.False(viewModel.Presentation!.Admission.HasPrice);

        service.SnapshotFactory = Phase22CDashboardFixtures.VerifiedOperatingDay;
        viewModel.RequestLocalReload();
        await viewModel.PendingLoad;

        Assert.Equal("$64.99", viewModel.Presentation!.Admission.PriceText);
        Assert.Equal(0, service.CheckOnlineCallCount);
    }

    [Fact]
    public async Task TodaysShows_StayAccurate_UpcomingAndEarlierSplitAtTheSnapshotTime()
    {
        var viewModel = new OperationsDashboardViewModel(new FakeOperationsDashboardService { SnapshotFactory = Phase22CDashboardFixtures.VerifiedOperatingDay });
        await viewModel.PendingLoad;
        var shows = viewModel.ShowsPresentation!;

        Assert.Equal("Today's Shows", shows.Heading);
        Assert.Equal(["Synthetic Stage Revue", "Synthetic Street Band", "Synthetic Night Show"], shows.Rows.Select(r => r.Name));
        Assert.Equal("3:30 PM · 6:00 PM", shows.Rows[0].TimesText);
        Assert.Equal("+1 earlier", shows.Rows[0].EarlierText);
        Assert.Equal("Synthetic Amphitheater", shows.Rows[0].Venue);
        Assert.Null(shows.Rows[1].Venue);
        Assert.Equal("Official schedule checked 6:05 AM ET", shows.FreshnessText);

        var closed = new OperationsDashboardViewModel(new FakeOperationsDashboardService { SnapshotFactory = Phase22CDashboardFixtures.ClosedPark });
        await closed.PendingLoad;
        Assert.Equal("No shows — the park is closed today.", closed.ShowsPresentation!.StatusText);

        var unpublished = new OperationsDashboardViewModel(new FakeOperationsDashboardService { SnapshotFactory = Phase22CDashboardFixtures.NoPublishedData });
        await unpublished.PendingLoad;
        Assert.Equal("Today's show schedule is not published.", unpublished.ShowsPresentation!.StatusText);
        Assert.DoesNotContain("No shows scheduled", unpublished.ShowsPresentation!.StatusText);
    }

    [Theory]
    [InlineData(-9)]
    [InlineData(3)]
    public async Task ViewPricing_OpensTodaysProducts_FromAnyPreviouslySelectedDate(int offsetDays)
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 16, 14, 0, 0, TimeSpan.Zero)); // 10:00 AM EDT
        var dashboard = new OperationsDashboardViewModel(new FakeOperationsDashboardService());
        var associateService = new FakeAssociatePassdownService();
        var associate = new AssociateViewModel(associateService, clock);
        var main = new MainViewModel(
            dashboard,
            new OperationsCalendarViewModel(new FakeOperationsCalendarService(), clock),
            associate,
            new OperationalNotesViewModel(new FakeOperationalNotesService(), new SystemClock()),
            new PassdownHistoryViewModel(new FakePassdownHistoryService { Summaries = [new FinalizedPassdownSummary(Today.AddDays(-9), clock.UtcNow, FinalizationOrigin.Scheduled, true, 100)] }, new FakeFinalizedPassdownPdfRetrievalService()),
            new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator()));
        await dashboard.PendingLoad;
        await associate.PendingLoad;
        associate.SelectedDate = Today.AddDays(offsetDays);
        await associate.PendingLoad;
        main.ShowDashboardSectionCommand.Execute(null);

        Assert.True(dashboard.OpenPricingCommand.CanExecute(null));
        dashboard.OpenPricingCommand.Execute(null);
        await associate.PendingLoad;

        Assert.Equal(AppSection.AssociatePassdown, main.SelectedSection);
        Assert.Equal(AssociateSection.Products, associate.SelectedSection);
        Assert.Equal(Today, associate.SelectedDate);
        Assert.Equal(Today, associateService.LoadCalls[^1]);
    }

    // ------------------------------------------------------------------ markup structure

    [Fact]
    public void Overview_ReadsInPriorityOrder_HoursAdmissionPassdownNotesOfficialData_ThenShows()
    {
        var markup = DashboardMarkup;
        var order = new[]
        {
            "Text=\"PARK HOURS\"", "Text=\"DAILY ADMISSION\"", "Text=\"PASSDOWN\"", "Text=\"OPERATIONAL NOTES\"",
            "Text=\"OFFICIAL DATA\"", "Text=\"{Binding ShowsPresentation.Heading}\"", "Text=\"Today at a Glance\"", "Text=\"Latest Finalized\"",
        }.Select(token => markup.IndexOf(token, StringComparison.Ordinal)).ToList();

        Assert.All(order, index => Assert.True(index > 0));
        Assert.Equal(order.OrderBy(i => i), order);
        Assert.Single(Regex.Matches(markup, "Classes=\"pageTitle\"")); // one page heading, no repeated titles
        Assert.Contains("Text=\"Operations Overview\" Classes=\"pageTitle\"", markup);
    }

    [Fact]
    public void Overview_HasExactlyOnePrimaryAction_OpenPassdown_AndCheckOnlineIsSecondary()
    {
        var markup = DashboardMarkup;
        var primaries = Regex.Matches(markup, "<Button [^>]*Classes=\"primaryAction\"[^>]*>", RegexOptions.Singleline);
        Assert.Single(primaries);
        Assert.Contains("Command=\"{Binding OpenPassdownCommand}\"", primaries[0].Value);
        Assert.Matches(new Regex("<Button Content=\"Check Online\" Classes=\"secondaryAction\""), markup);
        Assert.Matches(new Regex("<Button Content=\"Run Deep Refresh\" Classes=\"secondaryAction\""), markup);
    }

    [Fact]
    public void SourceDetails_Collapse_KeepsTheSummaryCheckOnlineAndGenuineErrorsOutside()
    {
        var markup = DashboardMarkup;
        var expanderStart = markup.IndexOf("<Expander x:Name=\"SourceDetailsExpander\"", StringComparison.Ordinal);
        var expanderEnd = markup.IndexOf("</Expander>", expanderStart, StringComparison.Ordinal);
        Assert.True(expanderStart > 0 && expanderEnd > expanderStart);
        var inside = markup[expanderStart..expanderEnd];
        var before = markup[..expanderStart];

        Assert.Contains("Classes=\"disclosure\"", inside);
        Assert.Contains("IsExpanded=\"{Binding IsSourceDetailsExpanded}\"", inside);
        Assert.Contains("Header=\"{Binding SourceDetailsToggleText}\"", inside);
        Assert.Contains("AutomationProperties.Name=\"Source details\"", inside);

        // Visible while collapsed: the headline + counts, the named-problem warning, last verified, Check Online.
        Assert.Contains("{Binding Presentation.DataHealth.SummaryText}", before);
        Assert.Contains("Text=\"{Binding Presentation.DataHealth.AttentionText}\"", before);
        Assert.Contains("{Binding Presentation.DataHealth.LastVerifiedText}", before);
        Assert.Contains("Command=\"{Binding CheckOnlineCommand}\"", before);

        // Preserved in the details: chips, per-source rows with their timestamps, Deep Refresh.
        Assert.Contains("{Binding Presentation.DataHealth.Counts}", inside);
        Assert.Contains("{Binding Presentation.DataHealth.OperationalSources}", inside);
        Assert.Contains("{Binding Presentation.DataHealth.ProductSources}", inside);
        Assert.Contains("Text=\"{Binding TimestampText}\"", inside);
        Assert.Contains("Command=\"{Binding RunDeepRefreshCommand}\"", inside);
        Assert.Contains("{Binding DeepRefreshPresentation.HeadlineText}", inside);
    }

    [Fact]
    public void Overview_IdentifiesThePricedProduct_AndOffersTheDetailedPricingReference()
    {
        var markup = DashboardMarkup;
        Assert.Contains("Text=\"{Binding Presentation.Admission.ProductName}\"", markup);
        Assert.Contains("Text=\"{Binding Presentation.Admission.QualifierText}\"", markup);
        Assert.Contains("Text=\"{Binding Presentation.Admission.FreshnessText}\"", markup);
        Assert.Contains("AutomationProperties.Name=\"{Binding Presentation.Admission.AutomationText}\"", markup);
        Assert.Contains("Command=\"{Binding OpenPricingCommand}\"", markup);

        // Nothing operational is hard-coded in the view: no dollar amounts, dates or counts.
        Assert.DoesNotMatch(new Regex("\\$[0-9]"), markup);
        Assert.DoesNotMatch(new Regex("20[0-9]{2}"), markup.Replace("2006/xaml", string.Empty).Replace("2008\"", string.Empty).Replace("2006\"", string.Empty));
    }

    [Fact]
    public void DisclosureStyle_IsSharedTokensOnly_AndDoesNotUseAnAccentFill()
    {
        var start = StylesMarkup.IndexOf("<Style Selector=\"Expander.disclosure\">", StringComparison.Ordinal);
        var end = StylesMarkup.IndexOf("App shell: Fury banner", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start);
        var section = StylesMarkup[start..end];

        Assert.DoesNotMatch(new Regex("Value=\"#[0-9A-Fa-f]{3,8}\""), section);
        Assert.DoesNotContain("PassdownAccentBrush}", section);
        Assert.Contains("ToggleButton.disclosureHeader:checked /template/ ContentPresenter#PART_ContentPresenter", section);
        Assert.Contains("{DynamicResource PassdownHoverBackgroundBrush}", section);
        Assert.Contains("Expander.disclosure:expanded /template/ PathIcon#DisclosureChevron", section);
    }
}
