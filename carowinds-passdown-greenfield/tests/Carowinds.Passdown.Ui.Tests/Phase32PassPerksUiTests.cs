using Carowinds.Passdown.Application.Links;
using Carowinds.Passdown.Application.PassPerks;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Application.Seasons;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain.PassPerks;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 32: the Products Pass Perks panel, the View details content for perks and program terms, the
/// Calendar's gift markers and selected-date list, and the "Show Pass Perks on Calendar" setting.
/// View-model and presentation assertions only; markup is asserted through binding strings (this
/// assembly's thread-affinity rule). Perks mirror the official November 2026 Carowinds list.
/// </summary>
public partial class Phase32PassPerksUiTests
{
    private static readonly TimeSpan Est = TimeSpan.FromHours(-5);
    private static readonly DateTimeOffset Checked = new(2026, 11, 21, 7, 0, 0, Est);
    private static readonly DateTimeOffset NowUtc = new(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);
    private static readonly Uri ParkPage = new("https://www.sixflags.com/carowinds/perks-and-play");
    private static readonly Uri TermsPage = new("https://www.sixflags.com/pass-perks-terms");
    private static readonly Uri FaqPage = new("https://www.sixflags.com/perks-and-play");
    private static readonly PassPerkCampaign November = new(2026, 11, "November Perks", YearInferred: true);

    private static OfficialPassPerk Perk(string title, string? dateLine, string body, bool monthly = true) =>
        PassPerkText.Interpret(title, dateLine, [body], November, ParkPage, Checked, monthly);

    private static readonly OfficialPassPerk Pretzel = Perk("BOGO 50% Off Soft Pretzel", "November 1 - November 30",
        "Passholders and members can redeem one (1) BOGO 50% Off Soft Pretzel by scanning your season pass or membership at any participating location. Offer valid at your home park. Pass Perks Terms & Conditions apply.");

    private static readonly OfficialPassPerk Mug = Perk("Buy Two, Get One FREE Hot Chocolate Mug", "November 27 - January 3",
        "Passholders and members can redeem one (1) Buy Two, Get One FREE Hot Chocolate Mug by scanning your season pass or membership at any participating location.");

    private static readonly OfficialPassPerk Camp = Perk("BOGO 50% Off Second Night at Carowinds Camp Wilderness", "Book by November 30",
        "New Reservations. BOGO 50% Off Second Night when you stay at Carowinds Camp Wilderness from October 31 to April 25. Promo code NOVPASS.");

    private static readonly OfficialPassPerk Bring = Perk("Earn 2 FREE Bring-A-Friend Tickets", null,
        "Visit twice between November 1 and November 22 to receive 2 FREE Bring-A-Friend Tickets, valid December 5 through January 3.", monthly: false);

    private static readonly PassPerksProgramTerms Terms = new PassPerksProgramTerms
    {
        TermsUpdatedText = "Updated July 30, 2026",
        EligibilityStatement = "PASS PERKS PROGRAM IS VALID FOR (I) REGULAR, GOLD, AND PRESTIGE SEASON PASSHOLDERS AND (II) MEMBERSHIPS.",
        IneligibilityStatement = "Pre-K Season Pass holders, SUMMER FUN PASS, HAUNT SEASON PASS, WILD CARD AND TICKET HOLDERS ARE NOT ELIGIBLE TO PARTICIPATE IN THE PASS PERKS PROGRAM.",
        FaqEligibilityAnswer = "All Season Passholders and Memberships, excluding the Pre-K Pass, KidZPass, and the Knott's Scary Farm Pass.",
        LoadingNote = "Perks & Play Loyalty Rewards are loaded to Season Passholders who hold an active pass on the first day of the month.",
        VisitCountingRule = "Passholders will only receive credit for visiting one Park per calendar day.",
        VisitRewardTimingRule = "Qualifying Passholders and Members will receive Pass Perks at the conclusion of the promotional period.",
        HomeParkRule = "Pass Perks can only be redeemed at your designated home park.",
        SourceUrls = [ParkPage, TermsPage, FaqPage],
        VerifiedAt = Checked,
    }.FillFrom(null);

    private static PassPerksDocument Document => PassPerksDocument.Empty.Merge(new PassPerksObservation(November, [Pretzel, Mug, Camp, Bring]) { ProgramTerms = Terms }, Checked);

    private static readonly OfficialDocumentStatus CheckedStatus = new(OfficialDocumentFreshness.Current, Checked, Checked, null, ParkPage);

    private static PassPerksOverview Overview(DateOnly operatingDate, OfficialDocumentStatus? status = null, PassPerksDocument? document = null) =>
        new(document ?? Document, status ?? CheckedStatus, operatingDate, operatingDate.ToDateTime(new TimeOnly(9, 0)) is var local ? new DateTimeOffset(local, Est) : default);

    private static PassPerkItemPresentation Item(OfficialPassPerk perk, DateOnly on) =>
        PassPerksPresentation.Item(new PassPerkEntry(perk, PassPerkStatusEvaluator.Evaluate(perk, on, new DateTimeOffset(on.ToDateTime(new TimeOnly(9, 0)), Est))), Terms);

    // ------------------------------------------------------------------ Rows

    [Fact]
    public void Rows_DistinguishAutomaticVisitBasedAndBookingPerks()
    {
        var today = new DateOnly(2026, 11, 21);

        Assert.Equal("Available to eligible passholders", Item(Pretzel, today).RequirementText);
        Assert.Equal("Requires 2 qualifying visits", Item(Bring, today).RequirementText);
        Assert.Equal("Promo code NOVPASS", Item(Camp, today).RequirementText);

        Assert.Equal("Monthly perk", Item(Pretzel, today).TypeLabel);
        Assert.Equal("Visit-based reward", Item(Bring, today).TypeLabel);
        Assert.Equal("Passholder offer", Item(Camp, today).TypeLabel);
    }

    [Fact]
    public void Rows_ShowConfirmedDatesCompactly_AndAStatusWordOnlyWhenNotCurrent()
    {
        var today = new DateOnly(2026, 11, 21);

        Assert.Equal("Valid all month (Nov 1 – Nov 30)", Item(Pretzel, today).KeyDateText);
        Assert.Equal("Redeem Nov 27 – Jan 3", Item(Mug, today).KeyDateText);
        Assert.Equal("Book by Nov 30", Item(Camp, today).KeyDateText);
        Assert.Equal("Visit by Nov 22 · Redeem Dec 5 – Jan 3", Item(Bring, today).KeyDateText);

        Assert.Null(Item(Pretzel, today).RowStatusLabel);
        Assert.Equal("Upcoming", Item(Mug, today).RowStatusLabel);
        Assert.Equal("Expired", Item(Pretzel, new DateOnly(2026, 12, 1)).RowStatusLabel);
        Assert.Contains("Upcoming", Item(Mug, today).AccessibleSummary, StringComparison.Ordinal); // status is text, not colour
    }

    // ------------------------------------------------------------------ Details dialog

    [Fact]
    public void Details_VisitBasedPerk_SeparatesEarningLoadingAndRedemption()
    {
        var details = Item(Bring, new DateOnly(2026, 11, 21)).Details;

        var earning = Assert.Single(details.Sections, s => s.Heading == "How it is earned");
        Assert.Contains(earning.Lines, l => l.Text == "Requires 2 qualifying visits (stated in this perk).");
        Assert.Contains(earning.Lines, l => l.Text == "Qualifying period: Nov 1, 2026 – Nov 22, 2026");
        Assert.Contains(earning.Lines, l => l.Note == "Program terms: how visits are counted");
        Assert.Contains(details.Sections.Single(s => s.Heading == "Reward loading").Lines, l => l.Text.Contains("conclusion of the promotional period", StringComparison.Ordinal));
        Assert.Contains(details.Sections.Single(s => s.Heading == "Dates").Lines, l => l.Text == "Redemption: Dec 5, 2026 – Jan 3, 2027");
        Assert.NotNull(details.InferredDatesNote); // years were assigned, and it says so
        Assert.Contains(details.Sections.Single(s => s.Heading == "Your pass").Lines, l => l.Text == PassPerksPresentation.IndividualPassNote);
    }

    [Fact]
    public void Details_AutomaticPerk_ShowsProgramEligibilityAsProgramTerms_AndTheDiscrepancy()
    {
        var details = Item(Pretzel, new DateOnly(2026, 11, 21)).Details;

        var who = details.Sections.Single(s => s.Heading == "Eligible passholders");
        Assert.Contains(who.Lines, l => l.Text == "“Passholders and members”" && l.Note == "Official wording in this perk");
        Assert.Contains(who.Lines, l => l.Text.Contains("REGULAR, GOLD, AND PRESTIGE", StringComparison.Ordinal) && l.Note!.StartsWith("Program terms", StringComparison.Ordinal));
        Assert.Contains(details.Sections.Single(s => s.Heading == "How it is earned").Lines, l => l.Text.Contains("no visit requirement", StringComparison.Ordinal));
        Assert.Contains(details.Sections.Single(s => s.Heading == "Reward loading").Lines, l => l.Text.Contains("first day of the month", StringComparison.Ordinal));
        Assert.Contains(details.Sections, s => s.Heading == "Official sources disagree" && s.IsWarning);
        Assert.Contains(details.Sections.Single(s => s.Heading == "Usage limits").Lines, l => l.Text == "“one (1)”");
        Assert.False(details.IsStatusWarning);
        Assert.StartsWith("Current — ", details.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void Details_LinksOfferTheParkPageTermsAndFaq_ThroughTheAllowlist()
    {
        var launcher = new RecordingLinkLauncher();
        var dialog = new OfficialDetailsDialogViewModel(launcher);

        dialog.Open(Item(Pretzel, new DateOnly(2026, 11, 21)).Details);

        Assert.Equal([ParkPage, TermsPage, FaqPage], dialog.Links.Select(l => l.Address));
        Assert.True(dialog.IsOpen);
    }

    [Theory]
    [InlineData("https://www.sixflags.com/pass-perks-terms", true)]
    [InlineData("https://www.sixflags.com/perks-and-play", true)]
    [InlineData("https://www.sixflags.com/carowinds/perks-and-play", true)]
    [InlineData("https://www.sixflags.com/perks-and-play/other", false)]
    [InlineData("https://www.sixflags.com/greatadventure/perks-and-play", false)]
    [InlineData("http://www.sixflags.com/pass-perks-terms", false)]
    public void Allowlist_AcceptsOnlyTheTwoExactProgramPages(string url, bool allowed) =>
        Assert.Equal(allowed, OfficialSourceLinkPolicy.TryValidate(new Uri(url), out _));

    [Fact]
    public void ProgramTermsDetails_KeepEachOfficialStatementWithItsSource()
    {
        var details = PassPerksPresentation.ProgramTermsDetails(Terms)!;

        Assert.Equal("Terms updated July 30, 2026", details.Subtitle);
        var eligibility = details.Sections.Single(s => s.Heading == "Eligibility");
        Assert.Equal(["Pass Perks Terms and Conditions", "Pass Perks Terms and Conditions", "Perks & Play FAQ"], eligibility.Lines.Select(l => l.Note));
        Assert.Contains(details.Sections, s => s.Heading == "Official sources disagree");
        Assert.Null(PassPerksPresentation.ProgramTermsDetails(new PassPerksProgramTerms()));
    }

    // ------------------------------------------------------------------ Products panel

    [Fact]
    public async Task Panel_CurrentMonth_ListsTheVerifiedPerks()
    {
        var service = new StubPassPerksService(Overview(new DateOnly(2026, 11, 21)));
        var panel = new PassPerksPanelViewModel(service, _ => { });

        await panel.LoadAsync();

        Assert.Equal("November 2026 (this month)", panel.SelectedMonth!.Label);
        Assert.Equal(4, panel.Items.Count);
        Assert.Equal("Earn 2 FREE Bring-A-Friend Tickets", panel.Items[0].Title); // current visit-based campaign first
        Assert.False(panel.IsStatusWarning);
        Assert.StartsWith("Official Carowinds Perks & Play page checked Nov 21, 2026", panel.StatusText, StringComparison.Ordinal);
        Assert.Null(panel.EmptyStateText);
        Assert.True(panel.HasProgramTerms);
    }

    [Fact]
    public async Task Panel_UnpublishedMonth_SaysNotVerified_AndHistoricalMonthIsLabelled()
    {
        var service = new StubPassPerksService(Overview(new DateOnly(2026, 12, 2)));
        var panel = new PassPerksPanelViewModel(service, _ => { });

        await panel.LoadAsync();

        Assert.Equal("December 2026 (this month)", panel.SelectedMonth!.Label);
        Assert.Empty(panel.Items);
        Assert.Equal("Pass Perks for December 2026 have not been verified. Last successful check: Nov 21, 2026 at 7:00 AM.", panel.EmptyStateText);

        panel.SelectedMonth = panel.Months.Single(m => m.Month == new DateOnly(2026, 11, 1));

        Assert.Equal(4, panel.Items.Count);
        Assert.Null(panel.HistoryNoticeText); // November is still the latest list observed
        Assert.Equal(1, service.Calls); // choosing a month never reads (or retrieves) again
    }

    [Fact]
    public async Task Panel_EarlierMonth_AfterANewerList_IsMarkedAsHistory()
    {
        var decemberCheck = new DateTimeOffset(2026, 12, 1, 7, 0, 0, Est);
        var december = new PassPerkCampaign(2026, 12, "December Perks", YearInferred: true);
        var churro = PassPerkText.Interpret("BOGO 50% Off Churro", "December 1 - December 31", ["Passholders and members can redeem one (1)."], december, ParkPage, decemberCheck, true);
        var document = Document.Merge(new PassPerksObservation(december, [churro]), decemberCheck);
        var panel = new PassPerksPanelViewModel(new StubPassPerksService(Overview(new DateOnly(2026, 12, 2), document: document)), _ => { });
        await panel.LoadAsync();

        panel.SelectedMonth = panel.Months.Single(m => m.Month == new DateOnly(2026, 11, 1));

        Assert.StartsWith("Earlier month — listed on the official page Nov 21, 2026 to Nov 21, 2026", panel.HistoryNoticeText, StringComparison.Ordinal);
        Assert.All(panel.Items, i => Assert.True(i.IsNotCurrent)); // never presented as current
        Assert.All(panel.Items, i => Assert.Contains("No longer listed", i.MetaText, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Panel_FailedRefresh_ShowsPreviouslyVerifiedRewards()
    {
        var failed = new OfficialDocumentStatus(OfficialDocumentFreshness.LastCheckFailed, Checked, Checked.AddHours(4), "We couldn't check the official Pass Perks right now.", ParkPage);
        var panel = new PassPerksPanelViewModel(new StubPassPerksService(Overview(new DateOnly(2026, 11, 21), failed)), _ => { });

        await panel.LoadAsync();

        Assert.True(panel.IsStatusWarning);
        Assert.Equal("Unable to refresh; showing previously verified rewards (last successful check Nov 21, 2026 at 7:00 AM).", panel.StatusText);
        Assert.Equal(4, panel.Items.Count);
    }

    [Fact]
    public async Task Panel_NeverChecked_ExplainsItself()
    {
        var panel = new PassPerksPanelViewModel(new StubPassPerksService(PassPerksOverview.NotChecked(new DateOnly(2026, 11, 21), Checked)), _ => { });

        await panel.LoadAsync();

        Assert.Equal(PassPerksPresentation.NotCheckedText, panel.StatusText);
        Assert.Equal("Pass Perks for November 2026 have not been verified.", panel.EmptyStateText);
        Assert.False(panel.HasProgramTerms);
    }

    [Fact]
    public async Task Panel_ViewDetails_OpensTheSharedDialogContent()
    {
        OfficialDetailsPresentation? opened = null;
        var panel = new PassPerksPanelViewModel(new StubPassPerksService(Overview(new DateOnly(2026, 11, 21))), d => opened = d);
        await panel.LoadAsync();

        panel.Items.Single(i => i.Title == Mug.Title).ViewDetailsCommand.Execute(null);
        Assert.Equal(Mug.Title, opened!.Title);

        panel.ViewProgramTermsCommand.Execute(null);
        Assert.Equal("Pass Perks program terms", opened.Title);
    }

    // ------------------------------------------------------------------ Calendar

    private static async Task<(OperationsCalendarViewModel Calendar, CalendarDisplaySettingsViewModel Settings)> CalendarAsync(DateOnly today, bool showPerks = true)
    {
        var settings = new CalendarDisplaySettingsViewModel(new MemoryCalendarStore(new CalendarDisplayPreferences(showPerks)));
        await settings.PendingLoad;
        var calendar = new OperationsCalendarViewModel(
            new FakeOperationsCalendarService(),
            new FakeClock(new DateTimeOffset(today.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero)),
            uiDispatcher: new ImmediateUiDispatcher(),
            passPerksService: new StubPassPerksService(Overview(today)),
            calendarDisplay: settings);
        await calendar.PendingLoad;
        return (calendar, settings);
    }

    [Fact]
    public async Task Calendar_MarksOnlyConfirmedDateSpecificDays()
    {
        var (calendar, _) = await CalendarAsync(new DateOnly(2026, 11, 21));

        var marked = calendar.Days.Where(d => d.HasPassPerks).Select(d => d.Date).ToList();

        // Qualifying ends Nov 22, the mug window begins Nov 27, booking deadline Nov 30, Bring-A-Friend redemption begins Dec 5 (in the grid's trailing week).
        Assert.Equal([new DateOnly(2026, 11, 22), new DateOnly(2026, 11, 27), new DateOnly(2026, 11, 30), new DateOnly(2026, 12, 5)], marked);
        Assert.DoesNotContain(calendar.Days, d => d.Date == new DateOnly(2026, 11, 15) && d.HasPassPerks); // month-long pretzel is not a daily marker
        Assert.Equal("Pass perk redemption date: Buy Two, Get One FREE Hot Chocolate Mug", calendar.Days.Single(d => d.Date == new DateOnly(2026, 11, 27)).PassPerkAccessibilityText);
        Assert.Contains("Pass perk redemption date", calendar.Days.Single(d => d.Date == new DateOnly(2026, 11, 27)).AutomationName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Calendar_SeveralPerksOnOneDay_ShareOneMarkerWithACount_AndAreAllListed()
    {
        var (calendar, _) = await CalendarAsync(new DateOnly(2027, 1, 2));

        var jan3 = calendar.Days.Single(d => d.Date == new DateOnly(2027, 1, 3));
        Assert.True(jan3.HasPassPerks);
        var marker = Assert.Single(jan3.PassPerkMarkers); // both are redemption: one marker, with a count
        Assert.True(marker.IsRedemption);
        Assert.Equal("2", marker.CountText);
        Assert.StartsWith("Pass perk redemption dates (2):", jan3.PassPerkAccessibilityText, StringComparison.Ordinal);

        calendar.SelectDateCommand.Execute(new DateOnly(2027, 1, 3));
        await calendar.PendingLoad;

        Assert.Equal(2, calendar.SelectedDatePassPerks.Count);
        Assert.All(calendar.SelectedDatePassPerks, p => Assert.Equal("Redemption / use · Last day to redeem", p.HeadlineText));
    }

    [Fact]
    public async Task Calendar_SelectedDate_ListsDatedPerksAndSummarizesMonthLongOnes()
    {
        var (calendar, _) = await CalendarAsync(new DateOnly(2026, 11, 21));

        calendar.SelectDateCommand.Execute(new DateOnly(2026, 11, 30));
        await calendar.PendingLoad;

        var camp = Assert.Single(calendar.SelectedDatePassPerks);
        Assert.Equal("Booking deadline · Last day to book", camp.HeadlineText);
        Assert.True(camp.IsDeadline);
        Assert.Equal(["BOGO 50% Off Soft Pretzel"], calendar.MonthLongPassPerks.Select(p => p.Title));
        Assert.Equal("Valid all November (1)", calendar.MonthLongPassPerksHeaderText);
        Assert.True(calendar.HasPassPerksSection);
    }

    [Fact]
    public async Task Calendar_ViewDetails_RaisesTheSharedDialogRequest()
    {
        var (calendar, _) = await CalendarAsync(new DateOnly(2026, 11, 21));
        OfficialDetailsPresentation? requested = null;
        calendar.PassPerkDetailsRequested += (_, d) => requested = d;

        calendar.SelectDateCommand.Execute(new DateOnly(2026, 11, 27));
        await calendar.PendingLoad;
        calendar.SelectedDatePassPerks.Single().ViewDetailsCommand.Execute(null);

        Assert.Equal(Mug.Title, requested!.Title);
    }

    [Fact]
    public async Task Calendar_VisibilitySetting_HidesMarkersWithoutDeletingAnything()
    {
        var (calendar, settings) = await CalendarAsync(new DateOnly(2026, 11, 21));
        Assert.Contains(calendar.Days, d => d.HasPassPerks);

        settings.ShowPassPerks = false;

        Assert.DoesNotContain(calendar.Days, d => d.HasPassPerks);
        Assert.False(calendar.HasPassPerksSection);
        Assert.Empty(calendar.MonthLongPassPerks);

        settings.ShowPassPerks = true;

        Assert.Contains(calendar.Days, d => d.HasPassPerks); // the cached perks were never removed
    }

    [Fact]
    public async Task Calendar_SettingOffAtStartup_ShowsNoMarkers()
    {
        var (calendar, _) = await CalendarAsync(new DateOnly(2026, 11, 21), showPerks: false);

        Assert.DoesNotContain(calendar.Days, d => d.HasPassPerks);
        Assert.False(calendar.ShowPassPerks);
    }

    [Fact]
    public async Task Calendar_WithoutPerksService_IsUnchanged()
    {
        var calendar = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), new FakeClock(NowUtc));
        await calendar.PendingLoad;

        Assert.DoesNotContain(calendar.Days, d => d.HasPassPerks);
        Assert.False(calendar.HasPassPerksSection);
        Assert.DoesNotContain("Pass Perk", calendar.Days[10].AutomationName, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ Phase 32A: marker types and layout

    [Fact]
    public async Task Calendar_MarkerTypes_QualifyingRedemptionAndDeadline()
    {
        var (calendar, _) = await CalendarAsync(new DateOnly(2026, 11, 21));
        PassPerkDayMarker Only(int month, int day) => Assert.Single(calendar.Days.Single(d => d.Date == new DateOnly(2026, month, day)).PassPerkMarkers);

        Assert.True(Only(11, 22).IsQualifyingVisit);   // last day for the Bring-A-Friend qualifying visits
        Assert.True(Only(11, 27).IsRedemption);        // Hot Chocolate Mug redemption begins
        Assert.True(Only(11, 30).IsDeadline);          // Camp Wilderness: last day to book
        Assert.True(Only(12, 5).IsRedemption);         // earned Bring-A-Friend tickets redeemable from Dec 5
        Assert.Equal("Pass perk qualifying visit date: Earn 2 FREE Bring-A-Friend Tickets", Only(11, 22).AccessibilityText);
        Assert.Equal("Pass perk booking deadline: BOGO 50% Off Second Night at Carowinds Camp Wilderness", Only(11, 30).AccessibilityText);
        Assert.Contains("Pass perk qualifying visit date", calendar.Days.Single(d => d.Date == new DateOnly(2026, 11, 22)).AutomationName, StringComparison.Ordinal);
        Assert.True(calendar.HasPassPerkLegend);
    }

    [Fact]
    public async Task Calendar_DateWithSeveralTypes_ShowsOneMarkerPerTypeInAFixedOrder_AndTypedRows()
    {
        // A short "redeemable" window ending on the qualifying deadline puts both types on Nov 22.
        var weekend = Perk("Passholder Weekend Treat", "November 21 - November 22", "Passholders and members can redeem one (1) treat.");
        var document = PassPerksDocument.Empty.Merge(new PassPerksObservation(November, [Pretzel, Camp, Bring, weekend]) { ProgramTerms = Terms }, Checked);
        var settings = new CalendarDisplaySettingsViewModel(new MemoryCalendarStore(null));
        await settings.PendingLoad;
        var calendar = new OperationsCalendarViewModel(
            new FakeOperationsCalendarService(),
            new FakeClock(NowUtc),
            uiDispatcher: new ImmediateUiDispatcher(),
            passPerksService: new StubPassPerksService(Overview(new DateOnly(2026, 11, 21), document: document)),
            calendarDisplay: settings);
        await calendar.PendingLoad;

        var nov22 = calendar.Days.Single(d => d.Date == new DateOnly(2026, 11, 22));
        Assert.Equal([PassPerkMarkerKind.QualifyingVisit, PassPerkMarkerKind.Redemption], nov22.PassPerkMarkers.Select(m => m.Kind));
        Assert.All(nov22.PassPerkMarkers, m => Assert.False(m.HasCount));
        Assert.Equal(
            "Pass perk qualifying visit date: Earn 2 FREE Bring-A-Friend Tickets. Pass perk redemption date: Passholder Weekend Treat",
            nov22.PassPerkAccessibilityText);

        calendar.SelectDateCommand.Execute(new DateOnly(2026, 11, 22));
        await calendar.PendingLoad;

        Assert.Equal(
            ["Qualifying visit · Last day for qualifying visits", "Redemption / use · Redeemable this day"],
            calendar.SelectedDatePassPerks.Select(p => p.HeadlineText));
        Assert.StartsWith("Qualifying visit · Last day for qualifying visits: Earn 2 FREE", calendar.SelectedDatePassPerks[0].AccessibleSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductsRows_HaveNoCalendarMarkerType()
    {
        var row = new PassPerkItemViewModel(Item(Pretzel, new DateOnly(2026, 11, 21)), _ => { });

        Assert.Null(row.MarkerKind);
        Assert.False(row.HasHeadlineText);
        Assert.Equal(Item(Pretzel, new DateOnly(2026, 11, 21)).AccessibleSummary, row.AccessibleSummary); // Products unchanged
    }

    [Fact]
    public void CellLayout_FixedTopRow_FixedSlots_StretchedContent_AndShrinkOnlyFallback()
    {
        var view = Markup("src/Carowinds.Passdown.Desktop/Views/OperationsCalendarView.axaml");

        Assert.Contains("<Grid ColumnDefinitions=\"Auto,*\" Height=\"18\" Classes=\"dayTopRow\">", view, StringComparison.Ordinal);
        Assert.Contains("<Panel Width=\"15\" Height=\"14\" ToolTip.Tip=\"{Binding AccessibilityText}\">", view, StringComparison.Ordinal);
        Assert.Contains("<StackPanel Orientation=\"Horizontal\" Spacing=\"3\" />", view, StringComparison.Ordinal);
        Assert.Contains("<Viewbox Grid.Column=\"1\" Stretch=\"Uniform\" StretchDirection=\"DownOnly\"", view, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"HorizontalContentAlignment\" Value=\"Stretch\" />", view, StringComparison.Ordinal);
        // The count badge lives inside the fixed slot, so a count never moves its icon.
        Assert.True(view.IndexOf("<Border Classes=\"passPerkCountBadge\"", StringComparison.Ordinal) < view.IndexOf("</Panel>", view.IndexOf("<Panel Width=\"15\"", StringComparison.Ordinal), StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ Setting

    [Fact]
    public async Task Setting_DefaultsOn_AndSavesEachChangeLocally()
    {
        var store = new MemoryCalendarStore(null);
        var settings = new CalendarDisplaySettingsViewModel(store);
        await settings.PendingLoad;
        Assert.True(settings.ShowPassPerks);

        settings.ShowPassPerks = false;
        Assert.True(await settings.PendingSave);

        Assert.Equal(new CalendarDisplayPreferences(false), store.Saved);
        Assert.Null(settings.SaveMessage);
    }

    [Fact]
    public async Task Setting_SaveFailure_IsReported()
    {
        var settings = new CalendarDisplaySettingsViewModel(new MemoryCalendarStore(null) { FailSaves = true });
        await settings.PendingLoad;

        settings.ShowPassPerks = false;

        Assert.False(await settings.PendingSave);
        Assert.Equal(CalendarDisplaySettingsViewModel.SaveFailedMessage, settings.SaveMessage);
    }

    // ------------------------------------------------------------------ Markup (binding strings only)

    [Theory]
    [InlineData("<DataTemplate DataType=\"vm:PassPerkItemViewModel\"")]
    [InlineData("ItemsSource=\"{Binding Months}\" SelectedItem=\"{Binding SelectedMonth}\"")]
    [InlineData("AutomationProperties.Name=\"Pass Perks month\"")]
    [InlineData("AutomationProperties.Name=\"View Pass Perks program terms\"")]
    [InlineData("Text=\"{Binding IndividualPassNote}\"")]
    [InlineData("AutomationProperties.Name=\"{Binding AccessibleSummary}\"")]
    public void ProductsMarkup_HasTheAccessiblePassPerksPanel(string expected) =>
        Assert.Contains(expected, Markup("src/Carowinds.Passdown.Desktop/Views/AssociateView.axaml"), StringComparison.Ordinal);

    [Theory]
    [InlineData("Data=\"{StaticResource PassdownGiftIconGeometry}\"")]
    [InlineData("Data=\"{StaticResource PassdownCheckCircleIconGeometry}\"")]
    [InlineData("Data=\"{StaticResource PassdownClockIconGeometry}\"")]
    [InlineData("ToolTip.Tip=\"{Binding PassPerkAccessibilityText}\"")]
    [InlineData("ToolTip.Tip=\"{Binding AccessibilityText}\"")]
    [InlineData("IsVisible=\"{Binding HasPassPerkLegend}\"")]
    [InlineData("IsVisible=\"{Binding HasPassPerksSection}\"")]
    [InlineData("Header=\"{Binding MonthLongPassPerksHeaderText}\"")]
    [InlineData("AutomationProperties.Name=\"{Binding ViewDetailsAutomationName}\"")]
    public void CalendarMarkup_HasTheMarkerAndSelectedDateList(string expected) =>
        Assert.Contains(expected, Markup("src/Carowinds.Passdown.Desktop/Views/OperationsCalendarView.axaml"), StringComparison.Ordinal);

    [Fact]
    public void SettingsMarkup_HasTheShowPassPerksCheckbox()
    {
        var view = Markup("src/Carowinds.Passdown.Desktop/Views/PersonalSyncSettingsView.axaml");

        Assert.Contains("Content=\"Show Pass Perks on Calendar\"", view, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding CalendarDisplay.ShowPassPerks}\"", view, StringComparison.Ordinal);
    }

    [Fact]
    public void GiftIcon_IsDefinedOnce()
    {
        var tokens = Markup("src/Carowinds.Passdown.Desktop/Themes/PassdownTokens.axaml");

        foreach (var key in new[] { "PassdownGiftIconGeometry", "PassdownCheckCircleIconGeometry", "PassdownClockIconGeometry" })
        {
            Assert.Single(tokens.Split($"x:Key=\"{key}\"").Skip(1));
        }
    }

    private static string Markup(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CarowindsPassdown.slnx")))
        {
            directory = directory.Parent;
        }

        return File.ReadAllText(Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."), relative));
    }

    private sealed class StubPassPerksService(PassPerksOverview overview) : IPassPerksService
    {
        public int Calls { get; private set; }

        public Task<PassPerksOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(overview);
        }
    }

    private sealed class MemoryCalendarStore(CalendarDisplayPreferences? initial) : ICalendarDisplayPreferenceStore
    {
        public CalendarDisplayPreferences? Saved { get; private set; }

        public bool FailSaves { get; init; }

        public Task<CalendarDisplayPreferences> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(initial ?? CalendarDisplayPreferences.BuiltIn);

        public Task SaveAsync(CalendarDisplayPreferences preferences, CancellationToken cancellationToken = default)
        {
            if (FailSaves)
            {
                throw new IOException("disk full");
            }

            Saved = preferences;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLinkLauncher : IExternalLinkLauncher
    {
        public Task<bool> OpenAsync(Uri officialPage, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
