using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.ProductDetails;
using Carowinds.Passdown.Application.Seasons;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Promotions;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 31C: the compact Products interface — short deal and product summaries, the small View details
/// link, and the in-app details dialog. Presentation and view-model assertions only; markup is asserted
/// through binding strings (this assembly's thread-affinity rule). Nothing here changes official prices,
/// availability, grouping or retrieval; several tests assert exactly that.
/// </summary>
public class Phase31CCompactProductsUiTests
{
    private static readonly TimeSpan Est = TimeSpan.FromHours(-5);
    private static readonly DateTimeOffset NowUtc = new(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Verified = new(2026, 11, 21, 10, 0, 0, Est);
    private static readonly DateOnly Today = new(2026, 11, 21);
    private static readonly Uri OffersUrl = new("https://www.sixflags.com/carowinds/special-offers");
    private static readonly Uri PassesUrl = new("https://www.sixflags.com/carowinds/memberships-and-season-passes");
    private static readonly OfficialDocumentStatus Checked = new(OfficialDocumentFreshness.Current, Verified, Verified, null, OffersUrl);
    private static readonly OfficialDateStatement Banner = new(new DateOnly(2026, 11, 27), "Hurry! Black Friday Deal Ends 11/27", DeadlineSource.CampaignBanner, YearInferred: true);
    private static readonly OfficialDateStatement BookBy = new(new DateOnly(2026, 11, 30), "Book by November 30, 2026.", DeadlineSource.OfferTerms);

    private static OfficialPromotion Promo(string title, Func<OfficialPromotion, OfficialPromotion>? configure = null)
    {
        var promo = new OfficialPromotion
        {
            PromotionId = "offer:" + title,
            Title = title,
            PrimarySourceUrl = OffersUrl,
            SourceUrls = [OffersUrl],
            FirstSeenAt = Verified,
            LastVerifiedAt = Verified,
        };
        return configure is null ? promo : configure(promo);
    }

    private static DealsOverview Overview(params OfficialPromotion[] promotions) =>
        OfficialDealsService.Group(new OfficialDealsDocument(promotions), Checked, Today, Verified.AddHours(1));

    private static DealItemPresentation Deal(OfficialPromotion promotion) => DealsPresentation.Build(Overview(promotion)).Current.Single();

    private static OfficialPromotion Bundle => Promo("2027 Silver Pass Bundle", p => p with
    {
        Highlight = "BLACK FRIDAY DEAL",
        Category = PromotionCategory.Bundle,
        AdvertisedPriceText = "$159",
        AdvertisedPriceAmount = 159m,
        PriceQualifier = "Only",
        RegularPriceAmount = 287m,
        SavingsText = "Save $128",
        CampaignEnd = Banner,
        Terms = ["Pass and dining are non-transferable."],
        FeeDisclosures = ["*Plus processing fee of up to $9.99 & applicable taxes."],
    });

    private static OfficialPromotion Resort => Promo("Resorts Black Friday Deal", p => p with
    {
        Category = PromotionCategory.Lodging,
        AdvertisedPriceText = "3rd Night FREE",
        PurchaseDeadline = BookBy,
        RedemptionWindow = new OfficialDateRange(new(2026, 11, 2), new(2027, 12, 30), "November 2, 2026 – December 30, 2027"),
        Discrepancies = ["The page banner says \"Hurry! Black Friday Deal Ends 11/27\", but this offer's own terms say \"Book by November 30, 2026.\"."],
    });

    private static PromotionPriceOption Option(string title, string price, decimal amount, string? subtitle = null) =>
        new(title, subtitle, price, amount, "/ea*", null, null);

    // ------------------------------------------------------------------ Compact deal summaries

    [Fact]
    public void CompactDeal_ShowsNamePriceShortDescriptionAndOnlyAConfirmedDeadline()
    {
        var bundle = Deal(Bundle with { Subtitle = "Silver Pass plus All Season Dining" });

        Assert.Equal("Only $159", bundle.RowPriceText);
        Assert.Equal("Silver Pass plus All Season Dining", bundle.ShortDescription);
        Assert.Null(bundle.KeyDeadlineText); // the banner date is inferred → never promoted to the row
        Assert.Null(bundle.RowStatusLabel);
        Assert.Null(bundle.NoticeText);

        var resort = Deal(Resort);
        Assert.Equal("Buy by Nov 30, 2026", resort.KeyDeadlineText);
        Assert.Equal("Official sources disagree — see details.", resort.NoticeText);
    }

    [Fact]
    public void CompactDeal_WithoutANumericPrice_ShowsTheOfficialBenefit_NeverADollarAmount()
    {
        var resort = Deal(Resort);

        Assert.Equal("3rd Night FREE", resort.RowPriceText);
        Assert.DoesNotContain("$", resort.RowPriceText, StringComparison.Ordinal);
    }

    [Fact]
    public void CompactDeal_WithNoStatedPrice_ShowsNoPrice()
    {
        var deal = Deal(Promo("Bring-A-Friend Days"));

        Assert.Null(deal.RowPriceText);
        Assert.Null(deal.ShortDescription);
    }

    [Fact]
    public void MultiplePurchaseOptions_ShowFromTheLowestStatedAmount()
    {
        var deal = Deal(Promo("Black Friday Deal", p => p with
        {
            PriceOptions = [Option("Gold Pass", "$99", 99m), Option("Silver Pass", "$69", 69m), Option("Prestige Pass", "$199", 199m)],
        }));

        Assert.Equal("From $69", deal.RowPriceText);
        Assert.Equal("3 purchase options: Gold Pass, Silver Pass, Prestige Pass", deal.ShortDescription);
        Assert.Equal(3, deal.OptionLines.Count); // every option still reaches the dialog
    }

    [Fact]
    public void OneOrEquallyPricedOptions_ShowTheOfficialPriceText_WithoutFrom()
    {
        var single = Deal(Promo("Black Friday Deal", p => p with { PriceOptions = [Option("Silver Pass", "$69", 69m, "Unlimited Visits & Free Parking")] }));
        Assert.Equal("$69", single.RowPriceText);
        Assert.Equal("Silver Pass — Unlimited Visits & Free Parking", single.ShortDescription);

        var same = Deal(Promo("Twin Deal", p => p with { PriceOptions = [Option("Silver Pass", "$69", 69m), Option("Good Any Day Ticket", "$69", 69m)] }));
        Assert.Equal("$69", same.RowPriceText);
    }

    [Fact]
    public void OptionsWithoutAnAmount_NeverProduceAFromPrice()
    {
        var deal = Deal(Promo("Members Only", p => p with
        {
            PriceOptions = [new PromotionPriceOption("Gold Pass", null, null, null, null, "Free upgrade", null), new PromotionPriceOption("Silver Pass", null, "Free", null, null, null, null)],
        }));

        Assert.Null(deal.RowPriceText);
    }

    [Fact]
    public void UpcomingAndExpiredDeals_StayDistinguishable_WithAShortStatusWord()
    {
        var presentation = DealsPresentation.Build(OfficialDealsService.Group(
            new OfficialDealsDocument([
                Promo("WinterFest Ticket", p => p with { PurchaseStart = new OfficialDateStatement(new(2026, 11, 27), "On sale November 27, 2026.", DeadlineSource.OfferTerms) }),
                Promo("Summer Flash Sale", p => p with { PurchaseDeadline = new OfficialDateStatement(new(2026, 9, 7), "Ends September 7, 2026.", DeadlineSource.OfferTerms) }),
            ]),
            Checked, Today, Verified.AddHours(1)));

        var upcoming = Assert.Single(presentation.Upcoming);
        Assert.Equal("Upcoming", upcoming.RowStatusLabel);
        Assert.Equal("On sale Nov 27, 2026", upcoming.KeyDeadlineText);

        var expired = Assert.Single(presentation.Expired);
        Assert.Equal("Expired", expired.RowStatusLabel);
        Assert.Equal("Buy by Sep 7, 2026", expired.KeyDeadlineText); // the confirmed date that expired it, labeled "Expired" beside it
    }

    [Fact]
    public void AYearInferredDeadline_IsNotPromotedToTheRow()
    {
        var deal = Deal(Promo("Flash", p => p with { PurchaseDeadline = new OfficialDateStatement(new(2026, 11, 30), "Ends 11/30", DeadlineSource.OfferTerms, YearInferred: true) }));

        Assert.Null(deal.KeyDeadlineText);
        Assert.Contains(deal.DateLines, l => l.Contains("year not stated; inferred", StringComparison.Ordinal)); // still in full detail
    }

    [Fact]
    public void CompactFields_LeaveThePhase31DealValuesUnchanged()
    {
        var bundle = Deal(Bundle);

        Assert.Equal("Only $159", bundle.PriceText);
        Assert.Equal("Regular $287", bundle.RegularPriceText);
        Assert.Equal("Save $128", bundle.SavingsText);
        Assert.Equal("BLACK FRIDAY DEAL", bundle.Badge);
        Assert.Contains(bundle.DateLines, l => l.StartsWith("Page banner (inferred end date", StringComparison.Ordinal));
        Assert.Contains(bundle.TermLines, l => l.Contains("processing fee", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ Deal details dialog content

    [Fact]
    public void DealDetails_GroupTheFullOfficialWording_UnderCompactHeadings()
    {
        var details = OfficialDetailsPresentation.ForDeal(Deal(Bundle));

        Assert.Equal("2027 Silver Pass Bundle", details.Title);
        Assert.Equal("BLACK FRIDAY DEAL · Bundle", details.Subtitle);
        Assert.Equal("Only $159", details.PriceText);
        Assert.Equal(["Regular $287", "Save $128"], details.PriceNotes);
        Assert.Equal(["Purchase dates", "Terms and restrictions", "Fees"], details.Sections.Select(s => s.Heading));
        Assert.Contains(details.Sections[0].Lines, l => l.Text.Contains("\"Hurry! Black Friday Deal Ends 11/27\"", StringComparison.Ordinal));
        Assert.Equal("*Plus processing fee of up to $9.99 & applicable taxes.", details.Sections[2].Lines.Single().Text); // verbatim
        Assert.Equal(ProductImportantDetailsPresentation.InferredDatesNoteText, details.InferredDatesNote);
        Assert.StartsWith("Current — ", details.StatusText, StringComparison.Ordinal);
        Assert.StartsWith("Last verified ", details.VerifiedText, StringComparison.Ordinal);
        Assert.Equal([OffersUrl], details.SourceUrls);
        Assert.Null(details.EmptyTermsText);
    }

    [Fact]
    public void DealDetails_SeparatePurchaseDatesFromVisitWindows_AndFlagContradictions()
    {
        var details = OfficialDetailsPresentation.ForDeal(Deal(Resort));

        Assert.Equal("Purchase by Nov 30, 2026 (\"Book by November 30, 2026.\")", details.Sections.Single(s => s.Heading == "Purchase dates").Lines.Single().Text);
        Assert.StartsWith("Use/visit window: Nov 2, 2026 – Dec 30, 2027", details.Sections.Single(s => s.Heading == "Validity and visit windows").Lines.Single().Text, StringComparison.Ordinal);
        var conflict = details.Sections.Single(s => s.Heading == "Official sources disagree");
        Assert.True(conflict.IsWarning);
        Assert.Null(details.InferredDatesNote); // the confirmed deadline needs no inference note
    }

    [Fact]
    public void DealDetails_AnUnreadableLastCheck_IsAVerificationWarning_NotASourceContradiction()
    {
        var deal = DealsPresentation.Build(Overview(Promo("Unreadable Offer", p => p with { LastCheckUnreadable = true }))).PreviouslyVerified.Single();

        var details = OfficialDetailsPresentation.ForDeal(deal);

        Assert.Equal("The latest check could not read this offer — see details.", deal.NoticeText);
        Assert.Equal("Previously verified", deal.RowStatusLabel);
        Assert.DoesNotContain(details.Sections, s => s.Heading == "Official sources disagree");
        var verification = details.Sections.Single(s => s.Heading == "Verification");
        Assert.True(verification.IsWarning);
        Assert.StartsWith("The latest official check could not read this offer", verification.Lines.Single().Text, StringComparison.Ordinal);
        Assert.True(details.IsStatusWarning);
    }

    [Fact]
    public void DealDetails_WithoutTerms_SayNoneWereListed()
    {
        var details = OfficialDetailsPresentation.ForDeal(Deal(Promo("Plain Offer")));

        Assert.Equal("No other terms were listed with this offer on the official page.", details.EmptyTermsText);
        Assert.Equal("No purchase deadline is stated on the official page.", details.Sections.Single().Lines.Single().Text);
    }

    // ------------------------------------------------------------------ View details actions and binding

    [Fact]
    public async Task ViewDetails_OnADeal_OpensTheDialogForThatDeal_WithoutGoingOnline()
    {
        var service = new FakeAssociatePassdownService();
        var viewModel = new AssociateViewModel(service, new FakeClock(NowUtc), externalLinkLauncher: new RecordingLinkLauncher(),
            dealsService: new StubDealsService(Overview(Bundle, Resort)));
        await viewModel.PendingLoad;
        await viewModel.PendingDealsLoad;
        Assert.False(viewModel.OfficialDetails.IsOpen);

        var resort = viewModel.CurrentDeals.Single(d => d.Title == "Resorts Black Friday Deal");
        Assert.True(resort.CanViewDetails);
        Assert.Equal("View details for Resorts Black Friday Deal", resort.ViewDetailsAutomationName);
        resort.ViewDetailsCommand.Execute(null);

        Assert.True(viewModel.OfficialDetails.IsOpen);
        Assert.Same(resort.Details, viewModel.OfficialDetails.Details);
        Assert.Equal("Resorts Black Friday Deal", viewModel.OfficialDetails.Details!.Title);
        Assert.Empty(service.CheckOnlineCalls);
        Assert.Empty(service.SaveCalls);

        viewModel.OfficialDetails.CloseCommand.Execute(null);
        Assert.False(viewModel.OfficialDetails.IsOpen);
    }

    private static AssociateProduct GoldPass() =>
        new("catalog|" + CatalogProductKey.FromPassOffer(PassOffer.SeasonPass("Gold", PassPriceType.NewPurchase, 2027)), "Gold Season Pass", SourceCategory.SeasonPasses,
            false, 99m, "$99", 115m, "Only", false, null,
            new AssociateVerificationInfo(AssociateVerificationState.Verified, Verified, null, null), true, null, PriceKind.StandardCatalog)
        {
            PassOffer = PassOffer.SeasonPass("Gold", PassPriceType.NewPurchase, 2027),
            RenewalPriceAmount = 89m,
            OfficialSourceUrl = PassesUrl,
        };

    private static AssociateProduct SilverPass() =>
        new("catalog|" + CatalogProductKey.FromPassOffer(PassOffer.SeasonPass("Silver", PassPriceType.NewPurchase, 2027)), "Silver Season Pass", SourceCategory.SeasonPasses,
            false, 69m, "$69", null, null, false, null,
            new AssociateVerificationInfo(AssociateVerificationState.Stale, Verified.AddDays(-3), TimeSpan.FromDays(3), null), true, null, PriceKind.StandardCatalog)
        {
            PassOffer = PassOffer.SeasonPass("Silver", PassPriceType.NewPurchase, 2027),
        };

    private static AssociateProduct Plain(string name, SourceCategory category, AssociateVerificationInfo? verification = null) =>
        new($"catalog|{category}|{name}", name, category, false, 20m, "$20", null, null, false, null,
            verification ?? new AssociateVerificationInfo(AssociateVerificationState.Verified, Verified, null, null), true, null, PriceKind.StandardCatalog);

    private static ProductImportantDetailsIndex DetailsIndex()
    {
        OfficialProductDetails Card(string tier, params ProductDetailStatement[] statements) => new()
        {
            DetailsId = "details:SeasonPasses:" + tier,
            Title = tier + " Tier",
            Channel = OfficialProductChannel.SeasonPasses,
            TierName = tier,
            PrimarySourceUrl = PassesUrl,
            FirstSeenAt = Verified,
            LastVerifiedAt = Verified,
            Statements = statements,
        };

        var gold = Card("Gold",
            new ProductDetailStatement { Kind = ProductDetailKind.Fee, Text = "Plus processing fee of up to $9.99 & applicable taxes." },
            new ProductDetailStatement
            {
                Kind = ProductDetailKind.BringAFriend,
                Text = "Buy during the Black Friday Deal and get a FREE Bring-A-Friend ticket. Use by 1/3/27.",
                PurchaseDeadline = Banner,
                RedemptionDeadline = new OfficialDateStatement(new(2027, 1, 3), "Use by 1/3/27.", DeadlineSource.OfferTerms),
            },
            new ProductDetailStatement { Kind = ProductDetailKind.Eligibility, Text = "Must be 18 or older to purchase." });
        var silver = Card("Silver",
            new ProductDetailStatement { Kind = ProductDetailKind.Purchase, Text = "Price valid through November 30, 2026.", PurchaseDeadline = BookBy });

        return new ProductImportantDetailsIndex(
            new Dictionary<OfficialProductChannel, (OfficialProductDetailsDocument, OfficialDocumentStatus)>
            {
                [OfficialProductChannel.SeasonPasses] = (new OfficialProductDetailsDocument([gold, silver]), Checked),
            },
            Today, Verified.AddHours(1));
    }

    private static async Task<(AssociateViewModel ViewModel, FakeAssociatePassdownService Service)> ProductsViewModel(IExternalLinkLauncher? launcher = null)
    {
        var service = new FakeAssociatePassdownService
        {
            WorkspaceFactory = date => FakeAssociatePassdownService.DefaultWorkspace(date) with
            {
                Products =
                [
                    GoldPass(),
                    SilverPass(),
                    Plain("All Season Dining", SourceCategory.AddOns),
                    Plain("Park Ticket", SourceCategory.DatedAdmissionTickets),
                    Plain("Fast Lane", SourceCategory.DatedFastLane),
                    Plain("Military/First Responder", SourceCategory.MilitaryFirstResponder, AssociateVerificationInfo.NotCheckedYet),
                ],
            },
        };
        var viewModel = new AssociateViewModel(service, new FakeClock(NowUtc), autosaveDebounce: TimeSpan.Zero, autosaveDelay: (_, _) => Task.CompletedTask,
            externalLinkLauncher: launcher ?? new RecordingLinkLauncher(), importantDetailsService: new StubDetailsService(DetailsIndex()));
        await viewModel.PendingLoad;
        await viewModel.PendingImportantDetailsLoad;
        return (viewModel, service);
    }

    private static ProductRowViewModel Row(AssociateViewModel viewModel, string name) =>
        viewModel.ProductSections.SelectMany(s => s.Products).Single(r => r.DisplayName.StartsWith(name, StringComparison.Ordinal));

    [Fact]
    public async Task ViewDetails_OnAProduct_OpensThatProductsOfficialDetails()
    {
        var (viewModel, _) = await ProductsViewModel();

        Row(viewModel, "Gold Season Pass").ViewDetailsCommand.Execute(null);
        var gold = viewModel.OfficialDetails.Details!;
        Assert.Equal("Gold Season Pass", gold.Title);
        Assert.Equal(["Eligibility", "Included benefits and perks", "Fees"], gold.Sections.Select(s => s.Heading));
        Assert.Contains("Reference price $115.00", gold.PriceNotes);
        Assert.Contains("Renewal offer $89.00", gold.PriceNotes);
        Assert.Contains("Memberships & Season Passes", gold.VerifiedText, StringComparison.Ordinal);

        Row(viewModel, "Silver Season Pass").ViewDetailsCommand.Execute(null);
        var silver = viewModel.OfficialDetails.Details!;
        Assert.Equal("Silver Season Pass", silver.Title);
        Assert.Equal("Purchase terms and deadlines", silver.Sections.Single().Heading);
        Assert.Equal("Price valid through November 30, 2026.", silver.Sections.Single().Lines.Single().Text);
    }

    [Fact]
    public async Task ProductRows_ShowOnlyAConfirmedPurchaseDeadline()
    {
        var (viewModel, _) = await ProductsViewModel();

        Assert.Equal("Buy by November 30, 2026", Row(viewModel, "Silver Season Pass").CompactDeadlineText);
        Assert.Null(Row(viewModel, "Gold Season Pass").CompactDeadlineText); // the perk's buy-by date is inferred
        Assert.False(Row(viewModel, "Gold Season Pass").HasCompactDeadline);
    }

    [Fact]
    public async Task TheViewDetailsLink_AppearsOnPassAddOnAndMilitaryRows_NotOnDatedAdmissionOrFastLane()
    {
        var (viewModel, _) = await ProductsViewModel();

        Assert.True(Row(viewModel, "Gold Season Pass").HasViewDetails);
        Assert.True(Row(viewModel, "All Season Dining").HasViewDetails);
        Assert.True(Row(viewModel, "Military/First Responder").HasViewDetails);
        Assert.False(Row(viewModel, "Park Ticket").HasViewDetails);
        Assert.False(Row(viewModel, "Fast Lane").HasViewDetails);
        Assert.Equal("View details for Gold Season Pass", Row(viewModel, "Gold Season Pass").ViewDetailsAutomationName);
    }

    [Fact]
    public void WithoutADialog_NoRowOffersViewDetails()
    {
        var row = new ProductRowViewModel(GoldPass(), () => { });

        Assert.False(row.HasViewDetails);
        row.ViewDetailsCommand.Execute(null); // harmless no-op
    }

    [Fact]
    public async Task MissingTerms_SayNoneAreRecorded_AndUnverifiedPricesSaySo()
    {
        var (viewModel, _) = await ProductsViewModel();

        Row(viewModel, "All Season Dining").ViewDetailsCommand.Execute(null);
        var dining = viewModel.OfficialDetails.Details!;
        Assert.Empty(dining.Sections);
        Assert.Equal(OfficialDetailsPresentation.NoTermsRecordedText, dining.EmptyTermsText);
        Assert.StartsWith("Official price last verified ", dining.VerifiedText, StringComparison.Ordinal);

        Row(viewModel, "Military/First Responder").ViewDetailsCommand.Execute(null);
        Assert.Equal("Official price not verified yet", viewModel.OfficialDetails.Details!.VerifiedText);
        Assert.Empty(viewModel.OfficialDetails.Links); // no official source recorded → no link invented
        Assert.Null(viewModel.OfficialDetails.UnlinkedSourceText);
    }

    [Fact]
    public async Task StalePrices_AreFlaggedInTheDialog_AsAWarning()
    {
        var (viewModel, _) = await ProductsViewModel();

        Row(viewModel, "Silver Season Pass").ViewDetailsCommand.Execute(null);

        Assert.Equal("Stale — showing the last verified price.", viewModel.OfficialDetails.Details!.StatusText);
        Assert.True(viewModel.OfficialDetails.Details.IsStatusWarning);
    }

    [Fact]
    public async Task ViewingDetails_ChangesNoPriceInclusionOrDraftValue()
    {
        var (viewModel, service) = await ProductsViewModel();
        var rows = viewModel.ProductSections.SelectMany(s => s.Products).ToList();
        var before = rows.Select(r => (r.DisplayName, r.OfficialPriceText, r.IncludeOnPdf, r.CanIncludeOnPdf, r.EffectivePriceText)).ToList();

        foreach (var row in rows.Where(r => r.HasViewDetails))
        {
            row.ViewDetailsCommand.Execute(null);
            viewModel.OfficialDetails.CloseCommand.Execute(null);
        }

        Assert.Equal(before, rows.Select(r => (r.DisplayName, r.OfficialPriceText, r.IncludeOnPdf, r.CanIncludeOnPdf, r.EffectivePriceText)).ToList());
        Assert.Empty(service.SaveCalls);
        Assert.Empty(service.CheckOnlineCalls);
    }

    // ------------------------------------------------------------------ Official link handling

    [Fact]
    public void TheDialog_LinksOnlyAllowlistedOfficialPages_OncePerAddress()
    {
        var dialog = new OfficialDetailsDialogViewModel(new RecordingLinkLauncher());
        var details = OfficialDetailsPresentation.ForDeal(Deal(Bundle with
        {
            SourceUrls = [OffersUrl, new Uri("https://www.sixflags.com/carowinds/black-friday-deal"), OffersUrl, new Uri("https://example.com/carowinds-deals")],
        }));

        dialog.Open(details);

        Assert.Equal(2, dialog.Links.Count);
        Assert.All(dialog.Links, l => Assert.StartsWith("Official page: www.sixflags.com/carowinds/", l.Label, StringComparison.Ordinal));
        Assert.DoesNotContain(dialog.Links, l => l.Address.Host.Contains("example", StringComparison.Ordinal));
        Assert.Null(dialog.UnlinkedSourceText);
    }

    [Fact]
    public void WithoutALinkLauncher_TheOfficialAddressIsShownAsText()
    {
        var dialog = new OfficialDetailsDialogViewModel(launcher: null);

        dialog.Open(OfficialDetailsPresentation.ForDeal(Deal(Bundle)));

        Assert.False(dialog.HasLinks);
        Assert.Equal("Official page: www.sixflags.com/carowinds/special-offers", dialog.UnlinkedSourceText);
    }

    [Fact]
    public async Task OpeningAnOfficialLink_UsesTheLauncher_AndLeavesTheDialogOpen()
    {
        var launcher = new RecordingLinkLauncher();
        var dialog = new OfficialDetailsDialogViewModel(launcher);
        dialog.Open(OfficialDetailsPresentation.ForDeal(Deal(Bundle)));

        await dialog.Links.Single().OpenCommand.ExecuteAsync(null);

        Assert.Equal([OffersUrl], launcher.Opened);
        Assert.True(dialog.IsOpen);
    }

    // ------------------------------------------------------------------ Accessibility

    [Fact]
    public void DetailLines_NeverRelyOnColorAlone()
    {
        var line = new OfficialDetailsLine("Get a FREE Bring-A-Friend ticket.", "Previously verified", "Use by January 3, 2027", "Not re-confirmed.", IsMuted: true);

        Assert.Equal("Previously verified: Get a FREE Bring-A-Friend ticket.. Use by January 3, 2027. Not re-confirmed.", line.AutomationName);
        Assert.Equal(" · Use by January 3, 2027", line.DeadlineSuffix);
        Assert.Equal(string.Empty, new OfficialDetailsLine("Text", "Current").DeadlineSuffix);
        Assert.True(new OfficialDetailsLine("Text", DeadlineText: "Buy by May 1, 2027").ShowsPlainDeadline);
    }

    [Fact]
    public async Task DealRows_HaveASpokenSummary_WithPriceDescriptionAndDeadline()
    {
        var viewModel = new AssociateViewModel(new FakeAssociatePassdownService(), new FakeClock(NowUtc), dealsService: new StubDealsService(Overview(Resort)));
        await viewModel.PendingLoad;
        await viewModel.PendingDealsLoad;

        var row = viewModel.CurrentDeals.Single();
        Assert.Equal("Resorts Black Friday Deal. 3rd Night FREE. Buy by Nov 30, 2026. Current — Listed on the official Carowinds pages at the last check.. Official sources disagree — see details.", row.AccessibleSummary);
        Assert.Equal("Buy by Nov 30, 2026", row.MetaText);
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

    [Theory]
    [InlineData("<KeyBinding Gesture=\"Escape\" Command=\"{Binding CloseCommand}\" />")]
    [InlineData("KeyboardNavigation.TabNavigation=\"Cycle\"")]
    [InlineData("AutomationProperties.Name=\"{Binding Details.Title, StringFormat='Close details for {0}'}\"")]
    [InlineData("AutomationProperties.HeadingLevel=\"1\"")]
    [InlineData("AutomationProperties.HeadingLevel=\"2\"")]
    [InlineData("AutomationProperties.Name=\"{Binding AutomationName}\"")]
    [InlineData("Command=\"{Binding OpenCommand}\"")]
    [InlineData("HorizontalScrollBarVisibility=\"Disabled\"")]
    public void TheDialog_IsKeyboardAndNarratorAccessible(string binding) =>
        Assert.Contains(binding, Markup("src/Carowinds.Passdown.Desktop/Views/OfficialDetailsView.axaml"), StringComparison.Ordinal);

    [Fact]
    public void TheDialog_IsHostedOverTheShell_AndReturnsFocusToTheOpeningLink()
    {
        Assert.Contains("IsVisible=\"{Binding Associate.OfficialDetails.IsOpen}\"", Markup("src/Carowinds.Passdown.Desktop/Views/MainWindow.axaml"), StringComparison.Ordinal);
        var codeBehind = Markup("src/Carowinds.Passdown.Desktop/Views/OfficialDetailsView.axaml.cs");
        Assert.Contains("CloseButton.Focus(", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_returnFocusTo", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProductsScreen_UsesSmallViewDetailsLinks_NotTermsDropdowns()
    {
        var view = Markup("src/Carowinds.Passdown.Desktop/Views/AssociateView.axaml");

        Assert.DoesNotContain("Terms and details", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Important details and deadlines for {0}", view, StringComparison.Ordinal);
        // Deal rows and (Phase 32) Pass Perk rows each have one small View details link; Phase 35C product rows show
        // "Details" in their own right-aligned column (accessible name still "View details for X").
        Assert.Equal(2, CountOf(view, "Content=\"View details\""));
        Assert.Equal(1, CountOf(view, "Content=\"Details\""));
        Assert.Equal(3, CountOf(view, "AutomationProperties.Name=\"{Binding ViewDetailsAutomationName}\""));
        Assert.Contains("<Style Selector=\"Button.compactLink\">", view, StringComparison.Ordinal);
        Assert.Contains("<DataTemplate DataType=\"vm:DealItemViewModel\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("{Binding TermLines}", view, StringComparison.Ordinal); // long terms live in the dialog
        Assert.DoesNotContain("{Binding DateLines}", view, StringComparison.Ordinal);
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private sealed class StubDealsService(DealsOverview overview) : IOfficialDealsService
    {
        public Task<DealsOverview> GetOverviewAsync(CancellationToken cancellationToken = default) => Task.FromResult(overview);
    }

    private sealed class StubDetailsService(ProductImportantDetailsIndex index) : IProductImportantDetailsService
    {
        public Task<ProductImportantDetailsIndex> GetIndexAsync(CancellationToken cancellationToken = default) => Task.FromResult(index);
    }

    private sealed class RecordingLinkLauncher : IExternalLinkLauncher
    {
        public List<Uri> Opened { get; } = [];

        public Task<bool> OpenAsync(Uri officialPage, CancellationToken cancellationToken = default)
        {
            Opened.Add(officialPage);
            return Task.FromResult(true);
        }
    }
}
