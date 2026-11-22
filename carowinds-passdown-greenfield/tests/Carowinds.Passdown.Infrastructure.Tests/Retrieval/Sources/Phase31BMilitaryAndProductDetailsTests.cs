using System.Net;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Promotions;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Sources;

/// <summary>
/// Phase 31B: the redesigned official Military &amp; First Responder page (shared <c>productv2-card</c>
/// markup, captured 2026-11-21) and official Important Details &amp; Deadlines extraction, against
/// committed, trimmed fixtures under <c>fixtures/phase31b/</c>. No network access.
/// </summary>
public class Phase31BMilitaryAndProductDetailsTests
{
    private static readonly DateTimeOffset SampleUtcNow = new(2026, 11, 21, 21, 0, 0, TimeSpan.Zero);

    private static RetrievalHttpResponse Html(string body) => new(HttpStatusCode.OK, "text/html", body);

    private static async Task<SourceResult<CatalogPayload>> RetrieveMilitaryAsync(string body)
    {
        var source = new MilitaryFirstResponderCatalogSource(new FakeRetrievalHttpClient(Html(body)), new FakeClock(SampleUtcNow));
        return await source.RetrieveAsync(new RetrievalRequest("military-first-responder.official", SourceCategory.MilitaryFirstResponder), CancellationToken.None);
    }

    [Fact]
    public async Task RedesignedMilitaryPage_IsVerified_WithBothOffers_UnderTheirEstablishedKeywordIdentities()
    {
        var result = await RetrieveMilitaryAsync(FixtureFile.ReadText("phase31b/military-discounts.html"));

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        var products = result.Payload!.Products;
        Assert.Equal(2, products.Count);

        var gold = Assert.Single(products, p => p.DisplayName == "2027 Military Gold Pass");
        Assert.Equal(CatalogProductKey.FromOfficialId(SourceCategory.MilitaryFirstResponder, "FallMilitaryGoldSP2027"), gold.Key);
        Assert.Equal(95m, gold.CurrentPriceAmount);
        Assert.Equal("Only", gold.PriceQualifier);

        var tickets = Assert.Single(products, p => p.Key == CatalogProductKey.FromOfficialId(SourceCategory.MilitaryFirstResponder, "MilitaryWizard"));
        Assert.Equal(40m, tickets.CurrentPriceAmount);
        Assert.Equal("Starting From", tickets.PriceQualifier);
        Assert.Empty(result.Payload.UnconfirmedProductKeys);
    }

    [Fact]
    public async Task APageWithOfficialBuyNowOffers_ButNoReadableCards_IsAnError_NeverAnEmptyVerifiedCatalog()
    {
        // Purchase markup present, card markup in an unknown layout: the misreading that retired the Military Gold Pass.
        const string body = """
            <main><div class="new-card-layout"><h2>2027 Military Gold Pass</h2><b>$95</b>
            <button data-accesso-keyword="FallMilitaryGoldSP2027">Buy Now</button></div></main>
            """;

        var result = await RetrieveMilitaryAsync(body);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task OneUnreadableCard_KeepsItsLastVerifiedState_WhileTheReadableOneIsConfirmed()
    {
        var fixture = FixtureFile.ReadText("phase31b/military-discounts.html");
        // Remove only the Military Gold Pass card's price element, so that card cannot be read.
        var damaged = fixture.Replace("data-ab=\"productv2-card-price\" class=\"text-[40px] font-semibold leading-none text-navy\">$95<", "data-ab=\"x\">$95<", StringComparison.Ordinal);
        Assert.NotEqual(fixture, damaged);

        var result = await RetrieveMilitaryAsync(damaged);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.StartsWith("Partially verified", result.Evidence.Detail, StringComparison.Ordinal);
        Assert.Single(result.Payload!.Products);
        Assert.Equal(
            [CatalogProductKey.FromOfficialId(SourceCategory.MilitaryFirstResponder, "FallMilitaryGoldSP2027")],
            result.Payload.UnconfirmedProductKeys);
    }

    [Fact]
    public async Task OriginalAndRedesignedLayouts_ProduceTheSameEstablishedIdentities()
    {
        var original = await RetrieveMilitaryAsync(FixtureFile.ReadText("military-first-responder/published-page.html"));
        var redesigned = await RetrieveMilitaryAsync(FixtureFile.ReadText("phase31b/military-discounts.html"));

        var redesignedKeys = redesigned.Payload!.Products.Select(p => p.Key).ToHashSet();
        Assert.All(original.Payload!.Products, p => Assert.Contains(p.Key, redesignedKeys));
        Assert.Equal(
            new[]
            {
                CatalogProductKey.FromOfficialId(SourceCategory.MilitaryFirstResponder, "FallMilitaryGoldSP2027"),
                CatalogProductKey.FromOfficialId(SourceCategory.MilitaryFirstResponder, "MilitaryWizard"),
            }.OrderBy(k => k.ToString(), StringComparer.Ordinal),
            redesignedKeys.OrderBy(k => k.ToString(), StringComparer.Ordinal));
    }

    [Fact]
    public async Task OriginalLayoutFixture_StillParses_AndAnEmptyPageWithoutOffersIsStillAVerifiedEmptyCatalog()
    {
        var original = await RetrieveMilitaryAsync(FixtureFile.ReadText("military-first-responder/published-page.html"));
        Assert.Equal(RetrievalStatus.Verified, original.Evidence.Status);
        Assert.Single(original.Payload!.Products);

        var empty = await RetrieveMilitaryAsync(FixtureFile.ReadText("military-first-responder/no-products-page.html"));
        Assert.Equal(RetrievalStatus.Verified, empty.Evidence.Status);
        Assert.Empty(empty.Payload!.Products);
    }

    [Fact]
    public async Task MilitaryGoldPassDetails_ShowItsOwnEligibilityAndFootnotedTerms_ButNeverTheSharedFinePrintsBlackFridayPerk()
    {
        var result = await RetrieveMilitaryAsync(FixtureFile.ReadText("phase31b/military-discounts.html"));

        var details = result.Payload!.ProductDetails!;
        Assert.Equal(OfficialProductChannel.MilitaryFirstResponder, details.Channel);
        var gold = Assert.Single(details.Products, d => d.Keyword == "FallMilitaryGoldSP2027");

        Assert.Contains(gold.Statements, s => s.Kind == ProductDetailKind.Eligibility && s.Text.Contains("active and retired military", StringComparison.Ordinal));
        Assert.Contains(gold.Statements, s => s is { Kind: ProductDetailKind.Restriction, FootnoteMarker: "**" } && s.Text.Contains("Home Park", StringComparison.Ordinal));
        Assert.Contains(gold.Statements, s => s.Kind == ProductDetailKind.Fee && s.Text.Contains("processing fee of up to $9.99", StringComparison.Ordinal));

        // The modal shared by every pass card carries "^Purchase a Season Pass during the Black Friday Deal…
        // Free Bring-A-Friend … through 01/03/2027". The military card never references "^", so it is not attached.
        Assert.DoesNotContain(gold.Statements, s => s.Text.Contains("Black Friday", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(gold.Statements, s => s.RedemptionDeadline is not null);
        Assert.DoesNotContain(gold.Statements, s => s.Text.Contains("Enroll in a Membership", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MilitaryTicketDetails_StateTheIdMeVerificationAndTicketLimit()
    {
        var result = await RetrieveMilitaryAsync(FixtureFile.ReadText("phase31b/military-discounts.html"));

        var tickets = Assert.Single(result.Payload!.ProductDetails!.Products, d => d.Keyword == "MilitaryWizard");
        Assert.Contains(tickets.Statements, s => s.Kind == ProductDetailKind.Eligibility && s.Text == "ID.me Verified");
        Assert.Contains(tickets.Statements, s => s.Kind == ProductDetailKind.Purchase && s.Text == "Up to 6 tickets");
        Assert.Contains(tickets.Statements, s => s.Kind == ProductDetailKind.Restriction && s.Text.Contains("Not valid for Haunted Mazes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SeasonPassTierDetails_CarryTheBlackFridayBringAFriendPerk_WithSeparatePurchaseAndRedemptionDeadlines()
    {
        var source = new SeasonPassCatalogSource(
            new FakeRetrievalHttpClient(Html(FixtureFile.ReadText("phase31b/memberships-and-season-passes.html"))), new FakeClock(SampleUtcNow));

        var result = await source.RetrieveAsync(new RetrievalRequest("season-passes.official", SourceCategory.SeasonPasses), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        var details = result.Payload!.ProductDetails!;
        Assert.Equal(["Gold", "Prestige", "Silver"], details.Products.Select(d => d.TierName).Order(StringComparer.Ordinal));

        foreach (var tier in details.Products)
        {
            var perk = Assert.Single(tier.Statements, s => s.Kind == ProductDetailKind.BringAFriend && s.RedemptionDeadline is not null);
            Assert.Contains("FREE Bring-A-Friend ticket", perk.Text, StringComparison.Ordinal);
            Assert.Equal(new DateOnly(2026, 11, 27), perk.PurchaseDeadline!.Date);
            Assert.Equal(DeadlineSource.CampaignBanner, perk.PurchaseDeadline.Source);
            Assert.True(perk.PurchaseDeadline.YearInferred);
            Assert.Equal(new DateOnly(2027, 1, 3), perk.RedemptionDeadline!.Date);
            Assert.Contains(tier.Statements, s => s.Kind == ProductDetailKind.Fee);
            // Footnoted "+Enroll in a Membership…" is not referenced by any featured benefit, so it is not attached.
            Assert.DoesNotContain(tier.Statements, s => s.Text.Contains("Enroll in a Membership", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ConflictingOfficialPurchaseDeadlines_AreReportedAsADiscrepancy_NotMerged()
    {
        var card = new OfferCard(
            OfficialId: "card-1", Title: "Holiday Deal", Keyword: "HolidayDeal", BeforePriceText: null, CurrentPriceText: "$50",
            CurrentPriceUnitText: null, StrikethroughPriceText: null, SavingsText: null, Subtitle: null, Highlight: null,
            DescriptionText: "Holiday Deal: buy by December 1, 2026 and get a free Bring-A-Friend ticket. Use by 1/3/27.",
            BenefitLabels: [], Disclosures: [], Options: []);
        var banner = new CampaignDeadline("Holiday Deal", new OfficialDateStatement(new DateOnly(2026, 12, 5), "Holiday Deal Ends 12/5", DeadlineSource.CampaignBanner, YearInferred: true));

        var details = OfficialProductDetailsReader.FromCard(card, new Uri("https://www.sixflags.com/carowinds/special-offers"),
            new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.FromHours(-5)), [banner], OfficialProductChannel.Promotions);

        var discrepancy = Assert.Single(details.Discrepancies);
        Assert.Contains("Holiday Deal Ends 12/5", discrepancy, StringComparison.Ordinal);
        Assert.Contains("December 1, 2026", discrepancy, StringComparison.Ordinal);
        // The card's own stated deadline is the one used for the perk; the banner is never silently substituted.
        var perk = Assert.Single(details.Statements, s => s.Kind == ProductDetailKind.BringAFriend);
        Assert.Equal(new DateOnly(2026, 12, 1), perk.PurchaseDeadline!.Date);
        Assert.Equal(new DateOnly(2027, 1, 3), perk.RedemptionDeadline!.Date);
    }

    [Fact]
    public void ACardWithNoTerms_HasNoInventedStatements()
    {
        var card = new OfferCard(
            OfficialId: "card-2", Title: "Plain Card", Keyword: null, BeforePriceText: null, CurrentPriceText: "$10",
            CurrentPriceUnitText: null, StrikethroughPriceText: null, SavingsText: null, Subtitle: null, Highlight: null,
            DescriptionText: "Fun for the whole family.", BenefitLabels: ["Park Admission"], Disclosures: [], Options: []);

        var details = OfficialProductDetailsReader.FromCard(card, new Uri("https://www.sixflags.com/carowinds/special-offers"),
            new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.FromHours(-5)), [], OfficialProductChannel.Promotions);

        Assert.Empty(details.Statements);
        Assert.Empty(details.Discrepancies);
    }
}
