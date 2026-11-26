using Carowinds.Passdown.Application.ProductDetails;
using Carowinds.Passdown.Application.Seasons;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Promotions;

// Phase 31C: synthetic official deals and Important Details for the Products screen, so the compact
// rows and the View details dialog can be reviewed with realistic long terms. Every value is invented.
internal static class Phase31CFixtures
{
    private static readonly Uri OffersUrl = new("https://www.sixflags.com/carowinds/special-offers");
    private static readonly Uri PassesUrl = new("https://www.sixflags.com/carowinds/memberships-and-season-passes");
    private static readonly Uri MilitaryUrl = new("https://www.sixflags.com/carowinds/military-and-first-responders");
    private static DateTimeOffset Verified => BusyDayFixtures.Verified;
    private static DateOnly Today => DateOnly.FromDateTime(Fixtures.Now.ToOffset(TimeSpan.FromHours(-4)).DateTime);

    private static OfficialPromotion Promo(string title, Func<OfficialPromotion, OfficialPromotion> configure) => configure(new OfficialPromotion
    {
        PromotionId = "syn:" + title,
        Title = title,
        PrimarySourceUrl = OffersUrl,
        SourceUrls = [OffersUrl],
        FirstSeenAt = Verified.AddDays(-5),
        LastVerifiedAt = Verified,
    });

    private static readonly OfficialDateStatement Banner = new(new DateOnly(2026, 11, 27), "Hurry! Synthetic Deal Ends 11/27", DeadlineSource.CampaignBanner, YearInferred: true);

    public static IOfficialDealsService Deals()
    {
        OfficialPromotion[] promotions =
        [
            Promo("2027 Synthetic Silver Pass Bundle", p => p with
            {
                Highlight = "SYNTHETIC DEAL",
                Category = PromotionCategory.Bundle,
                Subtitle = "Silver Pass plus All Season Dining and a souvenir bottle",
                AdvertisedPriceText = "$159",
                AdvertisedPriceAmount = 159m,
                PriceQualifier = "Only",
                RegularPriceAmount = 287m,
                SavingsText = "Save $128",
                CampaignEnd = Banner,
                Terms = ["Synthetic: bundle components are non-transferable and must be used by the same passholder.", "Synthetic: dining is limited to one meal and one snack per visit; not valid at select carts."],
                FeeDisclosures = ["*Synthetic: plus processing fee of up to $9.99 & applicable taxes."],
            }),
            Promo("Synthetic Season Pass Sale", p => p with
            {
                Category = PromotionCategory.SeasonPass,
                PriceOptions =
                [
                    new PromotionPriceOption("Silver Pass", "Unlimited visits", "$69", 69m, "/ea*", null, "SYN1"),
                    new PromotionPriceOption("Gold Pass", "Unlimited visits & free parking", "$99", 99m, "/ea*", null, "SYN2"),
                    new PromotionPriceOption("Prestige Pass", "All parks & perks", "$199", 199m, "/ea*", null, "SYN3"),
                ],
                PurchaseDeadline = new OfficialDateStatement(new DateOnly(2026, 11, 2), "Offer ends November 2, 2026.", DeadlineSource.OfferTerms),
                RedemptionWindow = new OfficialDateRange(new DateOnly(2027, 3, 13), new DateOnly(2027, 12, 31), "Valid March 13 – December 31, 2027"),
                Terms = ["Synthetic: passes are valid during the 2027 operating season only."],
            }),
            Promo("Synthetic Resort Getaway", p => p with
            {
                Category = PromotionCategory.Lodging,
                AdvertisedPriceText = "Third night free",
                Subtitle = "Stay two nights at a partner resort and the third night is free",
                PurchaseDeadline = new OfficialDateStatement(new DateOnly(2026, 11, 30), "Book by November 30, 2026.", DeadlineSource.OfferTerms),
                RedemptionWindow = new OfficialDateRange(new DateOnly(2026, 11, 2), new DateOnly(2027, 12, 30), "Stay November 2, 2026 – December 30, 2027"),
                Discrepancies = ["Synthetic: the page banner says \"Hurry! Synthetic Deal Ends 11/27\", but this offer's own terms say \"Book by November 30, 2026.\"."],
                CampaignEnd = Banner,
            }),
            Promo("Synthetic WinterFest Ticket", p => p with
            {
                Category = PromotionCategory.DailyTicket,
                AdvertisedPriceText = "$39.99",
                AdvertisedPriceAmount = 39.99m,
                PurchaseStart = new OfficialDateStatement(new DateOnly(2026, 11, 1), "On sale November 1.", DeadlineSource.OfferTerms),
            }),
            Promo("Synthetic Summer Flash Sale", p => p with
            {
                Category = PromotionCategory.DailyTicket,
                AdvertisedPriceText = "$29.99",
                AdvertisedPriceAmount = 29.99m,
                PurchaseDeadline = new OfficialDateStatement(new DateOnly(2026, 9, 7), "Ends September 7, 2026.", DeadlineSource.OfferTerms),
            }),
        ];
        var status = new OfficialDocumentStatus(OfficialDocumentFreshness.Current, Verified, Verified, null, OffersUrl);
        return new StaticDeals(OfficialDealsService.Group(new OfficialDealsDocument(promotions), status, Today, Verified.AddHours(1)));
    }

    public static IProductImportantDetailsService Details()
    {
        var status = new OfficialDocumentStatus(OfficialDocumentFreshness.Current, Verified, Verified, null, null);
        var gold = new OfficialProductDetails
        {
            DetailsId = "syn:gold",
            Title = "Gold Tier",
            Channel = OfficialProductChannel.SeasonPasses,
            TierName = "Gold",
            PrimarySourceUrl = PassesUrl,
            FirstSeenAt = Verified.AddDays(-20),
            LastVerifiedAt = Verified,
            Statements =
            [
                new ProductDetailStatement { Kind = ProductDetailKind.Incentive, Text = "Synthetic: free parking for the 2027 season and two one-time-use guest tickets." },
                new ProductDetailStatement
                {
                    Kind = ProductDetailKind.BringAFriend,
                    Text = "Synthetic: buy during the deal and get a FREE Bring-A-Friend ticket. Use by 1/3/27.",
                    PurchaseDeadline = Banner,
                    RedemptionDeadline = new OfficialDateStatement(new DateOnly(2027, 1, 3), "Use by 1/3/27.", DeadlineSource.OfferTerms),
                },
                new ProductDetailStatement { Kind = ProductDetailKind.Restriction, Text = "Synthetic: not valid for special ticketed events; blackout dates may apply at other parks." },
                new ProductDetailStatement { Kind = ProductDetailKind.Fee, Text = "Synthetic: plus processing fee of up to $9.99 & applicable taxes." },
            ],
        };
        var silver = gold with
        {
            DetailsId = "syn:silver",
            Title = "Silver Tier",
            TierName = "Silver",
            Statements =
            [
                new ProductDetailStatement
                {
                    Kind = ProductDetailKind.Purchase,
                    Text = "Synthetic: price valid through October 31, 2026.",
                    PurchaseDeadline = new OfficialDateStatement(new DateOnly(2026, 10, 31), "through October 31, 2026", DeadlineSource.OfferTerms),
                },
                new ProductDetailStatement { Kind = ProductDetailKind.VisitValidity, Text = "Synthetic: valid on all 2027 operating days at Carowinds." },
            ],
        };
        var military = new OfficialProductDetails
        {
            DetailsId = "syn:military",
            Title = "Military/First Responder",
            Channel = OfficialProductChannel.MilitaryFirstResponder,
            PrimarySourceUrl = MilitaryUrl,
            FirstSeenAt = Verified.AddDays(-20),
            LastVerifiedAt = Verified,
            Statements =
            [
                new ProductDetailStatement { Kind = ProductDetailKind.Eligibility, Text = "Synthetic: valid military or first responder ID required at the gate for each ticket holder." },
                new ProductDetailStatement { Kind = ProductDetailKind.Purchase, Text = "Synthetic: limit of six tickets per ID; must be purchased online in advance." },
            ],
        };
        var channels = new Dictionary<OfficialProductChannel, (OfficialProductDetailsDocument, OfficialDocumentStatus)>
        {
            [OfficialProductChannel.SeasonPasses] = (new OfficialProductDetailsDocument([gold, silver]), status),
            [OfficialProductChannel.MilitaryFirstResponder] = (new OfficialProductDetailsDocument([military]), status),
        };
        return new StaticDetails(new ProductImportantDetailsIndex(channels, Today, Verified.AddHours(1)));
    }

    /// <summary>Lets the official links render as links; opening one does nothing (never a browser).</summary>
    public sealed class NoOpLinkLauncher : Carowinds.Passdown.Desktop.Services.IExternalLinkLauncher
    {
        public Task<bool> OpenAsync(Uri officialPage, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class StaticDeals(DealsOverview overview) : IOfficialDealsService
    {
        public Task<DealsOverview> GetOverviewAsync(CancellationToken cancellationToken = default) => Task.FromResult(overview);
    }

    private sealed class StaticDetails(ProductImportantDetailsIndex index) : IProductImportantDetailsService
    {
        public Task<ProductImportantDetailsIndex> GetIndexAsync(CancellationToken cancellationToken = default) => Task.FromResult(index);
    }
}
