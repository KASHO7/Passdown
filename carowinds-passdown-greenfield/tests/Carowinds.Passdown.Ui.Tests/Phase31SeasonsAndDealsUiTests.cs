using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Seasons;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Promotions;
using Carowinds.Passdown.Domain.Seasons;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 31: season context above "Include in Passdown", the Deals panel, and past-season products on
/// the Products screen. View-model and presentation assertions only (bound text is asserted through
/// markup binding strings, per this assembly's thread-affinity rule).
/// </summary>
public class Phase31SeasonsAndDealsUiTests
{
    private static readonly TimeSpan Est = TimeSpan.FromHours(-5);
    private static readonly DateTimeOffset Nov21Utc = new(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Nov21Eastern = new(2026, 11, 21, 10, 0, 0, Est);
    private static readonly DateOnly Nov21 = new(2026, 11, 21);
    private static readonly Uri EventsUrl = new("https://www.sixflags.com/carowinds/events/oktoberfest");
    private static readonly Uri OffersUrl = new("https://www.sixflags.com/carowinds/special-offers");

    private static OfficialOperatingEvent Event(OperatingEventKind kind, DateOnly start, DateOnly end, string label) =>
        new($"{kind}:{start:yyyy-MM-dd}", kind.ToString(), kind, start, end, IsSelectDates: true, label, EventsUrl, Nov21Eastern, Nov21Eastern);

    private static readonly OperatingEventSchedule Schedule = new(
    [
        Event(OperatingEventKind.SCarowinds, new(2026, 9, 11), new(2026, 11, 7), "Select Nights. September 11 - November 7, 2026"),
        Event(OperatingEventKind.Oktoberfest, new(2026, 11, 7), new(2026, 11, 22), "Select Days, November 7-22, 2026"),
        Event(OperatingEventKind.WinterFest, new(2026, 11, 27), new(2027, 1, 3), "Select Dates, Nov 27 - Jan 3, 2027"),
    ]);

    private static readonly OfficialDocumentStatus CheckedStatus = new(OfficialDocumentFreshness.Current, Nov21Eastern, Nov21Eastern, null, EventsUrl);

    private static AssociateSeasonView Season(DateOnly date, ParkDayOperation parkDay = ParkDayOperation.Open, OperatingEventSchedule? schedule = null, OfficialDocumentStatus? status = null)
    {
        var s = schedule ?? Schedule;
        return new AssociateSeasonView(date, SeasonResolver.Resolve(date, s, parkDay), s, status ?? CheckedStatus, parkDay);
    }

    // ------------------------------------------------------------------ Season context

    [Fact]
    public void OktoberfestDate_ShowsOktoberfestWithOfficialWording_AndNotSCarowinds()
    {
        var presentation = SeasonContextPresentation.Build(Season(Nov21), hauntedSelected: false, hasHauntedProductsForDate: false);

        Assert.Equal("Official events for Saturday, November 21, 2026", presentation.HeadingText);
        var line = Assert.Single(presentation.Events);
        Assert.Equal("Oktoberfest", line.Name);
        Assert.Equal("Select Days, November 7-22, 2026", line.DateText);
        Assert.Equal("Park open this date", line.StatusText);
        Assert.Equal("SCarowinds 2026 ended November 7, 2026.", presentation.RecentlyEndedText);
        Assert.StartsWith("Next: WinterFest begins Friday, November 27, 2026", presentation.UpcomingText, StringComparison.Ordinal);
        Assert.False(presentation.ShowHauntedOption);
        Assert.Contains("not offered for this date", presentation.HauntedNote, StringComparison.Ordinal);
        Assert.False(presentation.IsFreshnessWarning);
    }

    [Fact]
    public void SavedHauntedChoice_OutsideSCarowinds_IsKeptVisible_WithAMismatchExplanation()
    {
        var presentation = SeasonContextPresentation.Build(Season(Nov21), hauntedSelected: true, hasHauntedProductsForDate: false);

        Assert.True(presentation.ShowHauntedOption);
        Assert.Contains("Your saved choice is kept", presentation.HauntedNote, StringComparison.Ordinal);
    }

    [Fact]
    public void PastSCarowindsDate_WithRecordedPrices_OffersTheHauntedOption()
    {
        var presentation = SeasonContextPresentation.Build(Season(new DateOnly(2026, 10, 17)), hauntedSelected: false, hasHauntedProductsForDate: true);

        Assert.True(presentation.ShowHauntedOption);
        Assert.Null(presentation.HauntedNote);
        Assert.Equal("SCarowinds", Assert.Single(presentation.Events).Name);
    }

    [Fact]
    public void UnknownSchedule_NeverHidesTheHauntedOption()
    {
        var unknown = new AssociateSeasonView(Nov21, SeasonResolution.Unknown(Nov21), null, OfficialDocumentStatus.NotChecked, ParkDayOperation.Unknown);

        var presentation = SeasonContextPresentation.Build(unknown, hauntedSelected: false, hasHauntedProductsForDate: false);

        Assert.True(presentation.ShowHauntedOption);
        Assert.Equal("Event schedule not checked yet.", presentation.FreshnessText);
        Assert.True(presentation.IsFreshnessWarning);
        Assert.StartsWith("Event schedule not available yet", presentation.NoEventText, StringComparison.Ordinal);
    }

    [Fact]
    public void WinterFestDate_SelectDatesWithoutHours_IsNotClaimedAsAnEventDay()
    {
        var presentation = SeasonContextPresentation.Build(Season(new DateOnly(2026, 12, 15), ParkDayOperation.Unknown), false, false);

        var line = Assert.Single(presentation.Events);
        Assert.Equal("WinterFest", line.Name);
        Assert.StartsWith("Select dates — this date is not confirmed", line.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedScheduleCheck_SaysItIsShowingTheLastVerifiedSchedule()
    {
        var failed = new OfficialDocumentStatus(OfficialDocumentFreshness.LastCheckFailed, Nov21Eastern.AddDays(-1), Nov21Eastern, "We couldn't check the official event schedule right now.", EventsUrl);

        var presentation = SeasonContextPresentation.Build(Season(Nov21, status: failed), false, false);

        Assert.StartsWith("Last check failed", presentation.FreshnessText, StringComparison.Ordinal);
        Assert.Contains("showing the schedule verified", presentation.FreshnessText, StringComparison.Ordinal);
        Assert.True(presentation.IsFreshnessWarning);
    }

    [Fact]
    public void DayWithoutANamedEvent_SaysSo()
    {
        var presentation = SeasonContextPresentation.Build(Season(new DateOnly(2026, 11, 24), ParkDayOperation.Unknown), false, false);

        Assert.Empty(presentation.Events);
        Assert.Equal("No official seasonal event is published for this date.", presentation.NoEventText);
    }

    // ------------------------------------------------------------------ Deals presentation

    private static OfficialPromotion Promo(string title, Func<OfficialPromotion, OfficialPromotion>? configure = null)
    {
        var promo = new OfficialPromotion
        {
            PromotionId = "offer:" + title,
            Title = title,
            PrimarySourceUrl = OffersUrl,
            SourceUrls = [OffersUrl],
            FirstSeenAt = Nov21Eastern,
            LastVerifiedAt = Nov21Eastern,
        };
        return configure is null ? promo : configure(promo);
    }

    private static DealsOverview Overview(IReadOnlyList<OfficialPromotion> promotions, OfficialDocumentStatus status, DateOnly? today = null) =>
        OfficialDealsService.Group(new OfficialDealsDocument(promotions), status, today ?? Nov21, Nov21Eastern.AddHours(1));

    private static readonly OfficialDocumentStatus DealsChecked = new(OfficialDocumentFreshness.Current, Nov21Eastern, Nov21Eastern, null, OffersUrl);

    [Fact]
    public void DealsNotCheckedYet_ExplainsTheEmptySection()
    {
        var presentation = DealsPresentation.Build(DealsOverview.NotChecked);

        Assert.Empty(presentation.Current);
        Assert.Equal("Deals not checked yet.", presentation.EmptyStateText);
        Assert.StartsWith("Deals not checked yet.", presentation.StatusText, StringComparison.Ordinal);
        Assert.True(presentation.IsStatusWarning);
    }

    [Fact]
    public void NoCurrentOffers_AfterASuccessfulCheck_SaysNoneVerified()
    {
        var presentation = DealsPresentation.Build(Overview([], DealsChecked));

        Assert.Equal("No current offers verified on the official Carowinds pages.", presentation.EmptyStateText);
        Assert.False(presentation.IsStatusWarning);
    }

    [Fact]
    public void FailedCheck_ShowsPreviouslyVerifiedOffers_WithAWarning()
    {
        var failed = new OfficialDocumentStatus(OfficialDocumentFreshness.LastCheckFailed, Nov21Eastern, Nov21Eastern.AddHours(1), "We couldn't check promotions right now.", OffersUrl);

        var presentation = DealsPresentation.Build(Overview([Promo("2027 Silver Pass Bundle")], failed));

        Assert.StartsWith("Last check failed", presentation.StatusText, StringComparison.Ordinal);
        Assert.Contains("showing previously verified offers", presentation.StatusText, StringComparison.Ordinal);
        Assert.True(presentation.IsStatusWarning);
    }

    [Fact]
    public void BlackFridayDeals_ShowPricesOptionsDatesAndDiscrepancies()
    {
        var bundle = Promo("2027 Silver Pass Bundle", p => p with
        {
            Highlight = "BLACK FRIDAY DEAL",
            Category = PromotionCategory.Bundle,
            AdvertisedPriceText = "$159",
            AdvertisedPriceAmount = 159m,
            PriceQualifier = "Only",
            RegularPriceAmount = 287m,
            SavingsText = "Save $128",
            CampaignEnd = new OfficialDateStatement(new(2026, 11, 27), "Hurry! Black Friday Deal Ends 11/27", DeadlineSource.CampaignBanner, YearInferred: true),
            FeeDisclosures = ["*Plus processing fee of up to $9.99 & applicable taxes."],
        });
        var silver = Promo("Black Friday Deal", p => p with
        {
            Category = PromotionCategory.SeasonPass,
            PriceOptions = [new PromotionPriceOption("Silver Pass", "Unlimited Visits & Free Parking", "$69", 69m, "/ea*", null, "WinterSilverSP2027")],
        });
        var resort = Promo("Resorts Black Friday Deal", p => p with
        {
            Category = PromotionCategory.Lodging,
            AdvertisedPriceText = "3rd Night FREE",
            PurchaseDeadline = new OfficialDateStatement(new(2026, 11, 30), "Book by November 30, 2026.", DeadlineSource.OfferTerms),
            RedemptionWindow = new OfficialDateRange(new(2026, 11, 2), new(2027, 12, 30), "November 2, 2026 – December 30, 2027"),
            Discrepancies = ["The page banner says \"Hurry! Black Friday Deal Ends 11/27\", but this offer's own terms say \"Book by November 30, 2026.\"."],
        });

        var presentation = DealsPresentation.Build(Overview([bundle, silver, resort], DealsChecked));

        Assert.Equal(3, presentation.Current.Count);
        Assert.Null(presentation.EmptyStateText);

        var b = Assert.Single(presentation.Current, d => d.Title == "2027 Silver Pass Bundle");
        Assert.Equal("Only $159", b.PriceText);
        Assert.Equal("Regular $287", b.RegularPriceText);
        Assert.Equal("BLACK FRIDAY DEAL", b.Badge);
        Assert.Contains(b.DateLines, l => l.StartsWith("Page banner (inferred end date, not stated in this offer's own terms): \"Hurry! Black Friday Deal Ends 11/27\" — read as Nov 27, 2026, year not stated", StringComparison.Ordinal));
        Assert.Contains(b.TermLines, l => l.Contains("processing fee", StringComparison.Ordinal));

        var s = Assert.Single(presentation.Current, d => d.Title == "Black Friday Deal");
        Assert.Null(s.PriceText);
        Assert.Equal("Silver Pass — $69/ea* — Unlimited Visits & Free Parking", Assert.Single(s.OptionLines));
        Assert.Contains("No purchase deadline is stated on the official page.", s.DateLines);

        var r = Assert.Single(presentation.Current, d => d.Title == "Resorts Black Friday Deal");
        Assert.Equal("3rd Night FREE", r.PriceText);
        Assert.Contains("Purchase by Nov 30, 2026 (\"Book by November 30, 2026.\")", r.DateLines); // confirmed: no inference label
        Assert.Contains(r.DateLines, l => l.StartsWith("Use/visit window: Nov 2, 2026 – Dec 30, 2027", StringComparison.Ordinal));
        Assert.Single(r.WarningLines);
    }

    [Fact]
    public void ExpiredOffer_MovesOutOfCurrentDeals_AndSaysWhy()
    {
        var resort = Promo("Resorts Black Friday Deal", p => p with
        {
            PurchaseDeadline = new OfficialDateStatement(new(2026, 11, 30), "Book by November 30, 2026.", DeadlineSource.OfferTerms),
        });

        var presentation = DealsPresentation.Build(Overview([resort], DealsChecked, today: new DateOnly(2026, 12, 1)));

        Assert.Empty(presentation.Current);
        var expired = Assert.Single(presentation.Expired);
        Assert.StartsWith("Offer has expired — Purchase deadline passed", expired.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void DealItem_LinksEveryOfficialPage()
    {
        var item = DealsPresentation.Build(Overview([Promo("2027 Silver Pass Bundle", p => p with { SourceUrls = [OffersUrl, new Uri("https://www.sixflags.com/carowinds/black-friday-deal")] })], DealsChecked)).Current[0];

        var viewModel = new DealItemViewModel(item, new RecordingLinkLauncher());

        Assert.Equal(2, viewModel.Links.Count);
        Assert.All(viewModel.Links, link => Assert.StartsWith("Official page: ", link.Label, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ Associate view model

    private static AssociateProduct Product(string identity, string name, bool pastSeason) =>
        new(identity, name, SourceCategory.Promotions, IsCustom: false, 55m, "$55", null, "Starting From", false, null,
            new AssociateVerificationInfo(AssociateVerificationState.Verified, Nov21Utc, null, "Verified."), IncludeOnPdf: true, DisplayPriceOverride: null)
        {
            PastSeasonNote = pastSeason ? "SCarowinds 2026 ended November 7, 2026 (official dates: Select Nights. September 11 - November 7, 2026)." : null,
        };

    [Fact]
    public async Task PastSeasonProduct_IsListedApart_AndItsSavedChoiceSurvivesAutosave()
    {
        const string bundleIdentity = "catalog|Promotions:id:scarowinds-bundles";
        var savedDraft = AssociateDraftPayload.CreateDefault() with
        {
            ProductOverrides = new Dictionary<string, ProductDraftOverride> { [bundleIdentity] = new(false, 50m) },
        };
        var service = new FakeAssociatePassdownService
        {
            WorkspaceFactory = date => FakeAssociatePassdownService.DefaultWorkspace(date) with
            {
                Draft = savedDraft,
                Products = [Product(bundleIdentity, "SCarowinds Bundles", pastSeason: true), Product("catalog|Promotions:id:baf", "Bring-A-Friend Ticket", pastSeason: false)],
                Season = Season(date),
            },
        };
        var viewModel = new AssociateViewModel(service, new FakeClock(Nov21Utc), autosaveDebounce: TimeSpan.Zero, autosaveDelay: (_, _) => Task.CompletedTask);
        await viewModel.PendingLoad;

        Assert.Equal("SCarowinds Bundles", Assert.Single(viewModel.PastSeasonProducts).DisplayName);
        Assert.DoesNotContain(viewModel.ProductSections.SelectMany(s => s.Products), row => row.DisplayName == "SCarowinds Bundles");
        Assert.Contains(viewModel.ProductSections.SelectMany(s => s.Products), row => row.DisplayName == "Bring-A-Friend Ticket");
        Assert.False(viewModel.ShowHauntedOption);
        Assert.True(viewModel.HasHauntedSeasonNote);

        viewModel.IncludeFastLane = !viewModel.IncludeFastLane; // any edit → autosave
        await viewModel.PendingAutosave;

        var saved = Assert.Single(service.SaveCalls).Draft;
        Assert.Equal(new ProductDraftOverride(false, 50m), saved.ProductOverrides[bundleIdentity]);
        Assert.Equal(savedDraft.SectionSelection.IncludeHauntedAttractions, saved.SectionSelection.IncludeHauntedAttractions);
    }

    [Fact]
    public void WithoutADealsService_TheDealsPanelIsHidden()
    {
        var viewModel = new AssociateViewModel(new FakeAssociatePassdownService(), new FakeClock(Nov21Utc));

        Assert.False(viewModel.HasDealsPanel);
    }

    [Fact]
    public async Task DealsPanel_LoadsCachedDeals_WithoutGoingOnline()
    {
        var service = new FakeAssociatePassdownService();
        var deals = new StubDealsService(Overview([Promo("2027 Silver Pass Bundle")], DealsChecked));
        var viewModel = new AssociateViewModel(service, new FakeClock(Nov21Utc), dealsService: deals);
        await viewModel.PendingLoad;
        await viewModel.PendingDealsLoad;

        Assert.True(viewModel.HasDealsPanel);
        Assert.Equal("2027 Silver Pass Bundle", Assert.Single(viewModel.CurrentDeals).Title);
        Assert.False(viewModel.HasDealsEmptyText);
        Assert.Empty(service.CheckOnlineCalls);
        Assert.True(deals.Calls >= 1);
    }

    [Fact]
    public async Task DealsServiceFailure_ShowsTheNotCheckedEmptyState_NotABlankSection()
    {
        var viewModel = new AssociateViewModel(new FakeAssociatePassdownService(), new FakeClock(Nov21Utc), dealsService: new StubDealsService(null));
        await viewModel.PendingLoad;
        await viewModel.PendingDealsLoad;

        Assert.True(viewModel.HasDealsEmptyText);
        Assert.Empty(viewModel.CurrentDeals);
    }

    // ------------------------------------------------------------------ Markup

    private static string AssociateViewMarkup() => File.ReadAllText(Path.Combine(RepoRoot(), "src", "Carowinds.Passdown.Desktop", "Views", "AssociateView.axaml"));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CarowindsPassdown.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Theory]
    [InlineData("IsVisible=\"{Binding ShowHauntedOption}\"")]
    [InlineData("Text=\"{Binding HauntedSeasonNote}\"")]
    [InlineData("Text=\"{Binding SeasonContext.HeadingText}\"")]
    [InlineData("Text=\"{Binding SeasonContext.FreshnessText}\"")]
    [InlineData("<TextBlock Text=\"Current Deals\" />")]
    [InlineData("Text=\"{Binding DealsStatusText}\"")]
    [InlineData("Text=\"{Binding DealsEmptyText}\"")]
    [InlineData("Header=\"{Binding ExpiredDealsHeaderText}\"")]
    [InlineData("Header=\"{Binding PastSeasonHeaderText}\"")]
    [InlineData("Command=\"{Binding ViewDetailsCommand}\"")] // Phase 31C: official links moved into the View details dialog
    public void AssociateView_BindsTheNewSeasonAndDealsSurfaces(string binding) =>
        Assert.Contains(binding, AssociateViewMarkup(), StringComparison.Ordinal);

    private sealed class StubDealsService(DealsOverview? overview) : IOfficialDealsService
    {
        public int Calls { get; private set; }

        public Task<DealsOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return overview is null
                ? Task.FromException<DealsOverview>(new IOException("cache unreadable"))
                : Task.FromResult(overview);
        }
    }

    private sealed class RecordingLinkLauncher : Desktop.Services.IExternalLinkLauncher
    {
        public Task<bool> OpenAsync(Uri officialPage, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
