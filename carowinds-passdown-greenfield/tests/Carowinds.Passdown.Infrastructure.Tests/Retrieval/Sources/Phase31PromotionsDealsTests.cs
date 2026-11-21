using System.Net;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Promotions;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Sources;

/// <summary>
/// Phase 31: the repaired Promotions/Deals discovery against fixtures trimmed from the official
/// special-offers, daily-tickets and Black Friday pages as published on 2026-11-21. The 2026 prices
/// and dates are dated examples, asserted here only because they are what those fixtures say.
/// </summary>
public class Phase31PromotionsDealsTests
{
    private static readonly DateTimeOffset Nov21 = new(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);
    private static readonly Uri SpecialOffers = new("https://www.sixflags.com/carowinds/special-offers");
    private static readonly Uri BlackFridayPage = new("https://www.sixflags.com/carowinds/black-friday-deal");

    private static string Text(string fixture) => FixtureFile.ReadText("phase31/" + fixture);

    private static RetrievalHttpResponse Html(string body) => new(HttpStatusCode.OK, "text/html", body);

    private static async Task<(SourceResult<CatalogPayload> Result, StubHttpClient Client)> RetrieveAsync(
        string? specialOffers = null, string? dailyTickets = null, RetrievalHttpResponse? campaign = null)
    {
        var client = new StubHttpClient(
        [
            Html(specialOffers ?? Text("special-offers.html")),
            Html(dailyTickets ?? Text("daily-tickets.html")),
            campaign ?? Html(Text("black-friday-deal.html")),
        ]);
        var source = new PromotionsCatalogSource(client, new FakeClock(Nov21));
        var result = await source.RetrieveAsync(new RetrievalRequest(source.SourceId, source.Category), CancellationToken.None);
        return (result, client);
    }

    private static OfficialPromotion Deal(SourceResult<CatalogPayload> result, string title) =>
        Assert.Single(result.Payload!.Deals!.Promotions, p => p.Title == title);

    [Fact]
    public async Task PricelessPaymentOptionCard_NoLongerFailsTheRefresh()
    {
        var (result, client) = await RetrieveAsync();

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Equal(3, client.Requests.Count);
        Assert.Equal(BlackFridayPage, client.Requests[2].Uri); // discovered from the offers page's own tabs
    }

    [Fact]
    public async Task BlackFriday_SilverPass69_IsReadFromItsPaymentOption()
    {
        var (result, _) = await RetrieveAsync();

        var deal = Deal(result, "Black Friday Deal");
        Assert.Equal(PromotionCategory.SeasonPass, deal.Category);
        Assert.Null(deal.AdvertisedPriceAmount);
        var silver = Assert.Single(deal.PriceOptions, o => o.Title == "Silver Pass");
        Assert.Equal(69m, silver.PriceAmount);
        Assert.Equal("WinterSilverSP2027", silver.Keyword);
        Assert.Contains(deal.PriceOptions, o => o.Title == "Good Any Day Ticket" && o.PriceAmount == 69m && o.SavingsText == "$0 more for Silver");

        // Page banner deadline is kept as a campaign end (year inferred), not as the offer's own deadline.
        Assert.Null(deal.PurchaseDeadline);
        Assert.Equal(new DateOnly(2026, 11, 27), deal.CampaignEnd!.Date);
        Assert.True(deal.CampaignEnd.YearInferred);
        Assert.Contains(deal.Terms, t => t.Contains("Use by 1/3/27", StringComparison.Ordinal));
        Assert.Contains(deal.FeeDisclosures, f => f.Contains("processing fee", StringComparison.Ordinal));

        var silverRow = Assert.Single(result.Payload!.Products, p => p.DisplayName == "Black Friday Deal: Silver Pass");
        Assert.Equal(69m, silverRow.CurrentPriceAmount);
    }

    [Fact]
    public async Task BlackFriday_Bundle159_NeverUsesTheStruckThroughPrice()
    {
        var (result, _) = await RetrieveAsync();

        var bundle = Deal(result, "2027 Silver Pass Bundle");
        Assert.Equal(PromotionCategory.Bundle, bundle.Category);
        Assert.Equal(159m, bundle.AdvertisedPriceAmount);
        Assert.Equal(287m, bundle.RegularPriceAmount);
        Assert.Equal("Save $128", bundle.SavingsText);
        Assert.Equal("BLACK FRIDAY DEAL", bundle.Highlight);
        Assert.Equal(new DateOnly(2026, 11, 27), bundle.CampaignEnd!.Date);
        Assert.DoesNotContain(bundle.Terms, t => t.Contains("processing fee", StringComparison.Ordinal)); // fee kept apart from the price and terms

        var row = Assert.Single(result.Payload!.Products, p => p.DisplayName == "2027 Silver Pass Bundle");
        Assert.Equal(159m, row.CurrentPriceAmount);
        Assert.Equal(287m, row.ComparisonPriceAmount);
    }

    [Fact]
    public async Task CampWilderness_ThirdNightFree_HasNoNumericPrice_AndSeparateDeadlines()
    {
        var (result, _) = await RetrieveAsync();

        var resort = Deal(result, "Resorts Black Friday Deal");
        Assert.Equal(PromotionCategory.Lodging, resort.Category);
        Assert.Equal("3rd Night FREE", resort.AdvertisedPriceText);
        Assert.Null(resort.AdvertisedPriceAmount); // never $3, never $0

        Assert.Equal(new DateOnly(2026, 11, 30), resort.PurchaseDeadline!.Date);
        Assert.Equal(DeadlineSource.OfferTerms, resort.PurchaseDeadline.Source);
        Assert.Equal((new DateOnly(2026, 11, 2), new DateOnly(2027, 12, 30)), (resort.RedemptionWindow!.Start, resort.RedemptionWindow.End));

        // The page banner (Ends 11/27) disagrees with the offer's own terms (Book by Nov 30): both kept, discrepancy reported.
        Assert.Equal(new DateOnly(2026, 11, 27), resort.CampaignEnd!.Date);
        Assert.Contains(resort.Discrepancies, d => d.Contains("Book by November 30, 2026", StringComparison.Ordinal));

        var row = Assert.Single(result.Payload!.Products, p => p.DisplayName == "Resorts Black Friday Deal");
        Assert.Null(row.CurrentPriceAmount);
        Assert.Equal("3rd Night FREE", row.CurrentDisplayPrice);
    }

    [Fact]
    public async Task FreeAdmission_IsNotAGuessedZero()
    {
        var (result, _) = await RetrieveAsync();

        var senior = Deal(result, "Free Senior Admission");
        Assert.Equal("Free", senior.AdvertisedPriceText);
        Assert.Null(senior.AdvertisedPriceAmount);
        Assert.Null(Assert.Single(result.Payload!.Products, p => p.DisplayName == "Free Senior Admission").CurrentPriceAmount);
    }

    [Fact]
    public async Task DuplicateOffersAcrossPages_MergeIntoOne_WithEverySourceLink()
    {
        var (result, _) = await RetrieveAsync();

        var bundle = Deal(result, "2027 Silver Pass Bundle");
        Assert.Contains(bundle.SourceUrls, u => u == SpecialOffers);
        Assert.Contains(bundle.SourceUrls, u => u == BlackFridayPage);
        Assert.Equal(1, result.Payload!.Products.Count(p => p.DisplayName == "2027 Silver Pass Bundle"));
        Assert.Equal(1, result.Payload.Products.Count(p => p.DisplayName == "Free Senior Admission"));
    }

    [Fact]
    public async Task EndedSeasonOffer_IsAbsentFromTheVerifiedCatalog()
    {
        var (result, _) = await RetrieveAsync();

        Assert.DoesNotContain(result.Payload!.Products, p => p.DisplayName.Contains("SCarowinds", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExistingProductIdentities_ArePreserved()
    {
        var (result, _) = await RetrieveAsync();

        // Same data-ab-product id the legacy markup parser used, so existing rows/overrides keep matching.
        var baf = Assert.Single(result.Payload!.Products, p => p.DisplayName == "Bring-A-Friend Ticket");
        Assert.Equal("Promotions:id:449e6fe8-a6ad-4af5-b162-b7835798b8ae", baf.Key.ToString());
        Assert.Equal(40m, baf.CurrentPriceAmount);
        Assert.Equal(80m, baf.ComparisonPriceAmount);
        Assert.Equal("Starting From", baf.PriceQualifier);
    }

    [Fact]
    public async Task ChangedDeal_IsReadAtItsNewPrice()
    {
        var changed = Text("special-offers.html").Replace("\\\"currentPrice\\\":\\\"$$159\\\"", "\\\"currentPrice\\\":\\\"$$179\\\"", StringComparison.Ordinal);
        var campaign = Text("black-friday-deal.html").Replace("\\\"currentPrice\\\":\\\"$$159\\\"", "\\\"currentPrice\\\":\\\"$$179\\\"", StringComparison.Ordinal);

        var (result, _) = await RetrieveAsync(specialOffers: changed, campaign: Html(campaign));

        Assert.Equal(179m, Deal(result, "2027 Silver Pass Bundle").AdvertisedPriceAmount);
    }

    [Fact]
    public async Task CampaignPageMissing_IsReported_AndItsOffersAreNotTreatedAsRemoved()
    {
        var (result, _) = await RetrieveAsync(campaign: new RetrievalHttpResponse(HttpStatusCode.NotFound, "text/html", "<html></html>"));

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Contains(result.Payload!.Deals!.UnreachablePageUrls, u => u == BlackFridayPage);
        Assert.Contains("404", result.Evidence.TechnicalDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UntitledCard_IsUnreadable_NotAFailureAndNotAnAbsence()
    {
        var broken = Text("special-offers.html").Replace("\\\"guestFacingTitle\\\":\\\"Free Senior Admission \\\"", "\\\"guestFacingTitle\\\":null", StringComparison.Ordinal);
        var daily = Text("daily-tickets.html").Replace("\\\"guestFacingTitle\\\":\\\"Free Senior Admission \\\"", "\\\"guestFacingTitle\\\":null", StringComparison.Ordinal);

        var (result, _) = await RetrieveAsync(specialOffers: broken, dailyTickets: daily);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Contains("offer:zW5psPpDcAAPtqwYTcDpfB", result.Payload!.Deals!.UnreadableOfferIds);
        Assert.Contains(result.Payload.UnconfirmedProductKeys, k => k.ToString() == "Promotions:id:zw5pspdcaaaptqwytcdpfb" || k.ToString().EndsWith("zW5psPpDcAAPtqwYTcDpfB", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CorePageFailure_IsAnError_SoNothingStoredChanges()
    {
        var client = new StubHttpClient([new RetrievalHttpResponse(HttpStatusCode.InternalServerError, "text/html", "oops")]);
        var source = new PromotionsCatalogSource(client, new FakeClock(Nov21));

        var result = await source.RetrieveAsync(new RetrievalRequest(source.SourceId, source.Category), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task LegacyMarkupPages_StillWork_AndAlsoYieldDeals()
    {
        var client = new StubHttpClient(
        [
            Html(FixtureFile.ReadText("promotions/published-page.html")),
            Html(FixtureFile.ReadText("promotions/daily-tickets-page.html")),
        ]);
        var source = new PromotionsCatalogSource(client, new FakeClock(Nov21));

        var result = await source.RetrieveAsync(new RetrievalRequest(source.SourceId, source.Category), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Equal(2, client.Requests.Count); // no structured tabs → no campaign pages
        Assert.Equal(result.Payload!.Products.Count, result.Payload.Deals!.Promotions.Count);
    }
}
