using System.Net;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Sources;

/// <summary>
/// Contract tests for <see cref="SeasonPassCatalogSource"/> against the redesigned official
/// Memberships &amp; Season Passes page (Phase 25), using sanitized fixtures under
/// <c>fixtures/memberships-and-season-passes/</c> built from one live capture by
/// <c>tools/research/memberships-season-passes/build_fixtures.py</c> — no network access (CLAUDE.md
/// "Testing"). The amounts asserted are the fixture's captured values, not production constants.
/// </summary>
public class SeasonPassCatalogSourceTests
{
    private static readonly DateTimeOffset SampleUtcNow = new(2026, 10, 22, 14, 17, 0, TimeSpan.Zero);

    private const string Fixtures = "memberships-and-season-passes/";

    private static RetrievalHttpResponse HtmlResponse(string fixtureName, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode, "text/html", FixtureFile.ReadText(Fixtures + fixtureName));

    private static SeasonPassCatalogSource BuildSource(RetrievalHttpResponse response, out FakeRetrievalHttpClient httpClient)
    {
        httpClient = new FakeRetrievalHttpClient(response);
        return new SeasonPassCatalogSource(httpClient, new FakeClock(SampleUtcNow));
    }

    private static RetrievalRequest Request() => new("season-passes.official", SourceCategory.SeasonPasses);

    private static async Task<SourceResult<CatalogPayload>> RetrieveAsync(string fixtureName)
    {
        var source = BuildSource(HtmlResponse(fixtureName), out _);
        return await source.RetrieveAsync(Request(), CancellationToken.None);
    }

    private static string Key(PassOffer offer) => offer.ToKey().ToString();

    private static readonly PassOffer SilverPass = PassOffer.SeasonPass("Silver", PassPriceType.NewPurchase, 2027);
    private static readonly PassOffer GoldPass = PassOffer.SeasonPass("Gold", PassPriceType.NewPurchase, 2027);
    private static readonly PassOffer PrestigePass = PassOffer.SeasonPass("Prestige", PassPriceType.NewPurchase, 2027);
    private static readonly PassOffer GoldRenewal = PassOffer.SeasonPass("Gold", PassPriceType.Renewal, 2027);
    private static readonly PassOffer PrestigeRenewal = PassOffer.SeasonPass("Prestige", PassPriceType.Renewal, 2027);
    private static readonly PassOffer SilverMembership = PassOffer.Membership("Silver", PassPriceType.MonthlyRate);
    private static readonly PassOffer GoldMembership = PassOffer.Membership("Gold", PassPriceType.MonthlyRate);
    private static readonly PassOffer PrestigeMembership = PassOffer.Membership("Prestige", PassPriceType.MonthlyRate);
    private static readonly PassOffer GoldInitiation = PassOffer.Membership("Gold", PassPriceType.InitiationFee);
    private static readonly PassOffer PrestigeInitiation = PassOffer.Membership("Prestige", PassPriceType.InitiationFee);

    private static ObservedProduct Product(SourceResult<CatalogPayload> result, PassOffer offer) =>
        Assert.Single(result.Payload!.Products, p => p.Key.ToString() == Key(offer));

    // ---- published page ------------------------------------------------------------------------

    [Fact]
    public async Task PublishedPage_IsVerifiedFromTheRedesignedOfficialUrlWithoutABrowser()
    {
        var source = BuildSource(HtmlResponse("published-page.html"), out var httpClient);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Equal(RetrievalStrategy.Html, result.Evidence.Strategy);
        Assert.Equal(new Uri("https://www.sixflags.com/carowinds/memberships-and-season-passes"), httpClient.LastRequestedUri);
        Assert.Equal(httpClient.LastRequestedUri, result.Evidence.SourceUrl);
        Assert.Equal("Carowinds Season Passes", result.Evidence.SourceName); // unchanged: keeps source state + absence scoping
        Assert.Equal(EasternTimeZone.ConvertFromUtc(SampleUtcNow), result.Evidence.TimeChecked);
    }

    [Fact]
    public async Task PublishedPage_YieldsEverySeparatePassAndMembershipOfferExactlyOnce()
    {
        var result = await RetrieveAsync("published-page.html");

        var keys = result.Payload!.Products.Select(p => p.Key.ToString()).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Equal(
            new[] { SilverPass, GoldPass, PrestigePass, GoldRenewal, PrestigeRenewal, SilverMembership, GoldMembership, PrestigeMembership, GoldInitiation, PrestigeInitiation }
                .Select(Key).Order(),
            keys.Order());
        Assert.Empty(result.Payload.UnconfirmedProductKeys);
    }

    [Fact]
    public async Task SilverSeasonPass_IsAOneTimePriceWithItsReferenceAmountAndNoInventedRenewal()
    {
        var result = await RetrieveAsync("published-page.html");

        var silver = Product(result, SilverPass);
        Assert.Equal("Silver Season Pass — 2027", silver.DisplayName);
        Assert.Equal(79m, silver.CurrentPriceAmount);
        Assert.Equal("$79", silver.CurrentDisplayPrice);
        Assert.Equal(109m, silver.ComparisonPriceAmount); // crossed-out reference, never the current price
        Assert.DoesNotContain(result.Payload!.Products, p => p.Key.ToString() == Key(PassOffer.SeasonPass("Silver", PassPriceType.Renewal, 2027)));
    }

    [Fact]
    public async Task SilverMembership_IsAnUnavailableUnpricedOption_NeverZeroDollars()
    {
        var result = await RetrieveAsync("published-page.html");

        var silver = Product(result, SilverMembership);
        Assert.Equal("Silver Membership", silver.DisplayName);
        Assert.Null(silver.CurrentPriceAmount);
        Assert.Equal("Not Available", silver.CurrentDisplayPrice);
        Assert.False(silver.IsExplicitlyUnavailable); // listed-but-locked is not "expired/sold out"
        Assert.DoesNotContain(result.Payload!.Products, p => p.Key.ToString() == Key(PassOffer.Membership("Silver", PassPriceType.InitiationFee)));
        Assert.DoesNotContain(result.Payload.Products, p => p.CurrentPriceAmount == 0m);
    }

    [Fact]
    public async Task GoldSeasonPass_UsesItsOwnLabeledFields_NotTheMembershipOrTheLargestNumber()
    {
        var result = await RetrieveAsync("published-page.html");

        var pass = Product(result, GoldPass);
        Assert.Equal("Gold Season Pass — 2027", pass.DisplayName);
        Assert.Equal(99m, pass.CurrentPriceAmount);
        Assert.Equal(115m, pass.ComparisonPriceAmount);
        Assert.Null(pass.PriceQualifier);

        var renewal = Product(result, GoldRenewal);
        Assert.Equal("Gold Season Pass Renewal — 2027", renewal.DisplayName);
        Assert.Equal(89m, renewal.CurrentPriceAmount);
        Assert.Null(renewal.ComparisonPriceAmount);
    }

    [Fact]
    public async Task GoldMembership_IsAMonthlyRateWithASeparateInitiationFeeAndVerbatimTerms()
    {
        var result = await RetrieveAsync("published-page.html");

        var membership = Product(result, GoldMembership);
        Assert.Equal("Gold Membership", membership.DisplayName);
        Assert.Equal(7m, membership.CurrentPriceAmount);
        Assert.Equal("$7/mo", membership.CurrentDisplayPrice);
        Assert.Equal("Auto-renews After 12mo", membership.PriceQualifier);
        Assert.Null(membership.ComparisonPriceAmount);

        var fee = Product(result, GoldInitiation);
        Assert.Equal("Gold Membership Initiation Fee", fee.DisplayName);
        Assert.Equal(20m, fee.CurrentPriceAmount);
        Assert.Equal("$20 initiation fee due today", fee.PriceQualifier);
        Assert.NotEqual(membership.Key, fee.Key);
    }

    [Fact]
    public async Task PrestigeOffers_AreParsedIndependentlyOfGold()
    {
        var result = await RetrieveAsync("published-page.html");

        Assert.Equal(249m, Product(result, PrestigePass).CurrentPriceAmount);
        Assert.Equal(275m, Product(result, PrestigePass).ComparisonPriceAmount);
        Assert.Equal(239m, Product(result, PrestigeRenewal).CurrentPriceAmount);
        Assert.Equal(20m, Product(result, PrestigeMembership).CurrentPriceAmount);
        Assert.Equal(20m, Product(result, PrestigeInitiation).CurrentPriceAmount);
    }

    [Fact]
    public async Task ProcessingFeeDisclosure_IsKeptVerbatimAndNeverAddedToAnyPrice()
    {
        var result = await RetrieveAsync("published-page.html");

        Assert.Contains("*Plus processing fee of up to $9.99 & applicable taxes.", result.Evidence.Detail);
        Assert.DoesNotContain(result.Payload!.Products, p => p.CurrentPriceAmount is 9.99m or 88.99m or 108.99m);
        Assert.StartsWith("Verified 3 Season Passes, 2 renewal offers, 2 Memberships (1 not available).", result.Evidence.Detail);
    }

    [Fact]
    public async Task NoOfferIsDateSpecificOrExpired()
    {
        var result = await RetrieveAsync("published-page.html");

        Assert.All(result.Payload!.Products, p =>
        {
            Assert.False(p.DateSpecific);
            Assert.Null(p.ApplicableDate);
            Assert.False(p.IsExplicitlyUnavailable);
            Assert.Null(p.ValidThroughDate);
        });
    }

    // ---- layout variation -----------------------------------------------------------------------

    [Fact]
    public async Task ReorderedTiersAndOptions_ProduceTheSameOffersAndValues()
    {
        var published = await RetrieveAsync("published-page.html");
        var reordered = await RetrieveAsync("reordered-page.html");

        Assert.Equal(
            published.Payload!.Products.OrderBy(p => p.Key.ToString()).ToList(),
            reordered.Payload!.Products.OrderBy(p => p.Key.ToString()).ToList());
    }

    [Fact]
    public async Task RepeatedIdenticalOffers_AreOneObservationEach()
    {
        var published = await RetrieveAsync("published-page.html");
        var duplicated = await RetrieveAsync("duplicated-offers-page.html");

        Assert.Equal(published.Payload!.Products, duplicated.Payload!.Products);
        Assert.Empty(duplicated.Payload.UnconfirmedProductKeys);
    }

    [Fact]
    public async Task ConflictingRepeatOfAnOffer_TrustsNeitherValue_AndKeepsTheRestVerified()
    {
        var result = await RetrieveAsync("conflicting-duplicate-page.html");

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.DoesNotContain(result.Payload!.Products, p => p.Key.ToString() == Key(GoldPass));
        Assert.Contains(Key(GoldPass), result.Payload.UnconfirmedProductKeys.Select(k => k.ToString()));
        Assert.Equal(249m, Product(result, PrestigePass).CurrentPriceAmount);
        Assert.StartsWith("Partially verified:", result.Evidence.Detail);
        Assert.Contains("Gold Season Pass — 2027", result.Evidence.Detail);
    }

    [Fact]
    public async Task MissingTier_IsSimplyAbsent_NotAnError()
    {
        var result = await RetrieveAsync("missing-tier-page.html");

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.DoesNotContain(result.Payload!.Products, p => p.DisplayName.StartsWith("Prestige", StringComparison.Ordinal));
        Assert.Empty(result.Payload.UnconfirmedProductKeys);
        Assert.Equal(99m, Product(result, GoldPass).CurrentPriceAmount);
    }

    [Fact]
    public async Task UnknownNewTier_IsParsedUnderItsOwnIdentityAndFlagged()
    {
        var result = await RetrieveAsync("unknown-tier-page.html");

        var diamond = Product(result, PassOffer.SeasonPass("Diamond", PassPriceType.NewPurchase, 2027));
        Assert.Equal(499m, diamond.CurrentPriceAmount);
        Assert.Null(diamond.ComparisonPriceAmount);
        Assert.Equal(45m, Product(result, PassOffer.Membership("Diamond", PassPriceType.MonthlyRate)).CurrentPriceAmount);
        Assert.Contains("New official tier found: Diamond.", result.Evidence.TechnicalDetail);
        Assert.Equal(99m, Product(result, GoldPass).CurrentPriceAmount);
    }

    [Fact]
    public async Task MissingRenewalAndReference_AreLeftUnknownRatherThanInvented()
    {
        var result = await RetrieveAsync("no-renewal-no-reference-page.html");

        Assert.Null(Product(result, PrestigePass).ComparisonPriceAmount);
        Assert.DoesNotContain(result.Payload!.Products, p => p.Key.ToString() == Key(PrestigeRenewal));
        Assert.Empty(result.Payload.UnconfirmedProductKeys); // genuinely not published, so not "unreadable"
    }

    [Fact]
    public async Task UnexpectedExtraPricing_IsNeverMistakenForAPassOrMembershipPrice()
    {
        var result = await RetrieveAsync("unexpected-extra-pricing-page.html");

        Assert.Equal(99m, Product(result, GoldPass).CurrentPriceAmount);
        Assert.Equal(89m, Product(result, GoldRenewal).CurrentPriceAmount);
        Assert.Equal(7m, Product(result, GoldMembership).CurrentPriceAmount);
        Assert.Equal("Auto-renews After 12mo", Product(result, GoldMembership).PriceQualifier); // promotional text is not billing terms
        Assert.DoesNotContain(result.Payload!.Products, p => p.CurrentPriceAmount is 49m or 35m or 40m);
        Assert.Contains("without a recognizable tier title ('Group Outing')", result.Evidence.TechnicalDetail);
        Assert.Contains("has no purchase options", result.Evidence.TechnicalDetail);
    }

    // ---- partial success ------------------------------------------------------------------------

    [Fact]
    public async Task MissingMembershipPrice_KeepsTheVerifiedOffers_AndMarksOnlyThatMembershipUnconfirmed()
    {
        var result = await RetrieveAsync("missing-membership-price-page.html");

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.DoesNotContain(result.Payload!.Products, p => p.Key.ToString() == Key(GoldMembership) || p.Key.ToString() == Key(GoldInitiation));
        Assert.Equal(
            new[] { Key(GoldMembership), Key(GoldInitiation) }.Order(),
            result.Payload.UnconfirmedProductKeys.Select(k => k.ToString()).Order());
        Assert.Equal(99m, Product(result, GoldPass).CurrentPriceAmount);
        Assert.Equal(20m, Product(result, PrestigeMembership).CurrentPriceAmount);
        Assert.StartsWith("Partially verified:", result.Evidence.Detail);
        Assert.Contains("Could not verify Gold Membership, Gold Membership Initiation Fee", result.Evidence.Detail);
    }

    [Fact]
    public async Task UnreadableRenewalLabel_LeavesTheRenewalUnconfirmed_AndThePassVerified()
    {
        var result = await RetrieveAsync("unreadable-renewal-page.html");

        Assert.Equal(249m, Product(result, PrestigePass).CurrentPriceAmount);
        Assert.Equal([Key(PrestigeRenewal)], result.Payload!.UnconfirmedProductKeys.Select(k => k.ToString()));
    }

    [Fact]
    public async Task UnresolvedDataReference_IsNeverReadAsAPrice()
    {
        var result = await RetrieveAsync("unresolved-reference-page.html");

        Assert.DoesNotContain(result.Payload!.Products, p => p.Key.ToString() == Key(GoldPass) || p.Key.ToString() == Key(GoldRenewal));
        Assert.Contains(Key(GoldPass), result.Payload.UnconfirmedProductKeys.Select(k => k.ToString()));
        Assert.Equal(7m, Product(result, GoldMembership).CurrentPriceAmount);
    }

    [Fact]
    public async Task SeasonPassQuotedMonthly_IsNotTrusted()
    {
        var result = await RetrieveAsync("season-pass-shown-monthly-page.html");

        Assert.DoesNotContain(result.Payload!.Products, p => p.Key.ToString() == Key(SilverPass));
        Assert.Contains(Key(SilverPass), result.Payload.UnconfirmedProductKeys.Select(k => k.ToString()));
    }

    // ---- failures -------------------------------------------------------------------------------

    [Theory]
    [InlineData("truncated-data-page.html")]
    [InlineData("cards-without-data-page.html")]
    [InlineData("no-page-data.html")]
    public async Task UnreadablePageData_IsAnErrorWithADiagnostic_NeverAnEmptyCatalog(string fixture)
    {
        var result = await RetrieveAsync(fixture);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
        Assert.False(string.IsNullOrWhiteSpace(result.Evidence.TechnicalDetail));
        Assert.Equal("We couldn't check season passes and memberships right now.", result.Evidence.Detail);
    }

    [Fact]
    public async Task LegacyCardOnlyPage_FromBeforeTheRedesign_IsAnErrorRatherThanAGuess()
    {
        var source = BuildSource(new RetrievalHttpResponse(HttpStatusCode.OK, "text/html", FixtureFile.ReadText("season-passes/published-page.html")), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task WellFormedPageWithNoPassTiers_IsAVerifiedEmptyCatalog()
    {
        var result = await RetrieveAsync("empty-catalog-page.html");

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Empty(result.Payload!.Products);
    }

    [Fact]
    public async Task NonSuccessStatusCode_ReturnsError()
    {
        var source = BuildSource(HtmlResponse("published-page.html", HttpStatusCode.ServiceUnavailable), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task TransportFailure_ReturnsErrorWithTechnicalDetail()
    {
        var source = new SeasonPassCatalogSource(new FakeRetrievalHttpClient(new HttpRequestException("Connection reset")), new FakeClock(SampleUtcNow));

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
        Assert.Contains("Connection reset", result.Evidence.TechnicalDetail);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesRatherThanBecomingAnErrorResult()
    {
        var source = new SeasonPassCatalogSource(new FakeRetrievalHttpClient(new OperationCanceledException()), new FakeClock(SampleUtcNow));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.RetrieveAsync(Request(), CancellationToken.None));
    }
}
