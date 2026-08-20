using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Sources;

/// <summary>
/// Contract tests for <see cref="ProductCardHtmlParser"/> against sanitized, committed fixtures
/// under <c>fixtures/catalog-source/</c> (spec §14.2) — no network access (CLAUDE.md "Testing").
/// Fixture provenance and the shared-mechanism evidence behind this parser is documented in
/// docs/retrieval/catalog-source-inventory.md. Written in Phase 3F1, when no adapter existed yet
/// (that phase was foundation only); kept exercising the parser directly rather than through a
/// source's <c>RetrieveAsync</c> even after Phase 3F2–3F5 added four real adapters, since these
/// tests are about the shared parser's own contract, not any one adapter's — each adapter's own
/// test class (e.g. <see cref="SeasonPassCatalogSourceTests"/>) covers it end to end instead.
/// </summary>
public class ProductCardHtmlParserTests
{
    private static string Fixture(string name) => FixtureFile.ReadText($"catalog-source/{name}");

    [Fact]
    public void Parse_SeasonPassesPage_ReturnsBothCardsWithExplicitIdsAndComparisonPrices()
    {
        var cards = ProductCardHtmlParser.Parse(Fixture("season-passes-page.html"));

        Assert.Equal(2, cards.Count);

        var gold = cards.Single(c => c.DisplayName == "2027 Gold Pass");
        Assert.Equal("550e3db6-5bad-45f8-8404-5d608c367617", gold.SanityProductId);
        Assert.Equal("FallGoldSP2027", gold.AccessoKeyword);
        Assert.Equal("Sale Price", gold.PriceQualifier);
        Assert.Equal("$115", gold.ComparisonDisplayPrice);
        Assert.Equal(115.00m, gold.ComparisonPriceAmount);
        Assert.Equal("$89", gold.CurrentDisplayPrice);
        Assert.Equal(89.00m, gold.CurrentPriceAmount);

        var prestige = cards.Single(c => c.DisplayName == "2027 Prestige Pass");
        Assert.Equal("5de73366-26f8-4c9d-8983-407dc10132ea", prestige.SanityProductId);
        Assert.Equal("FallPrestigeSP2027", prestige.AccessoKeyword);
        Assert.Equal(229.00m, prestige.CurrentPriceAmount);
        Assert.Equal(275.00m, prestige.ComparisonPriceAmount);
    }

    [Fact]
    public void Parse_TwoDifferentCardsOnTheSamePage_RemainIsolated()
    {
        // Guards against the two cards' field extraction bleeding into each other — each card's
        // own values must come only from its own fragment.
        var cards = ProductCardHtmlParser.Parse(Fixture("season-passes-page.html"));

        var gold = cards.Single(c => c.DisplayName == "2027 Gold Pass");
        var prestige = cards.Single(c => c.DisplayName == "2027 Prestige Pass");

        Assert.NotEqual(gold.SanityProductId, prestige.SanityProductId);
        Assert.NotEqual(gold.AccessoKeyword, prestige.AccessoKeyword);
        Assert.NotEqual(gold.CurrentPriceAmount, prestige.CurrentPriceAmount);
    }

    [Fact]
    public void Parse_CardWithNoComparisonPrice_LeavesComparisonFieldsNull()
    {
        // "Daily Fast Lane" is a real observed card with no strikethrough element at all.
        var cards = ProductCardHtmlParser.Parse(Fixture("fast-lane-page.html"));

        var dailyFastLane = cards.Single(c => c.DisplayName == "Daily Fast Lane");

        Assert.Equal("Starting From", dailyFastLane.PriceQualifier);
        Assert.Equal(49.00m, dailyFastLane.CurrentPriceAmount);
        Assert.Null(dailyFastLane.ComparisonDisplayPrice);
        Assert.Null(dailyFastLane.ComparisonPriceAmount);
    }

    [Fact]
    public void Parse_CardWithAnExplicitComparisonPrice_PreservesIt()
    {
        // "All Season Fast Lane" is a real observed card with a genuine $899 -> $499 markdown.
        var cards = ProductCardHtmlParser.Parse(Fixture("fast-lane-page.html"));

        var allSeason = cards.Single(c => c.DisplayName == "All Season Fast Lane");

        Assert.Equal("Only", allSeason.PriceQualifier);
        Assert.Equal("$899", allSeason.ComparisonDisplayPrice);
        Assert.Equal(899.00m, allSeason.ComparisonPriceAmount);
        Assert.Equal(499.00m, allSeason.CurrentPriceAmount);
    }

    [Fact]
    public void Parse_PageWithNoRecognizedCards_ReturnsEmptyListRatherThanThrowing()
    {
        // A well-formed page with zero product cards is a legitimate empty catalog, not an error
        // (Phase 3F1 Part D: successful-empty must be distinguishable from a parse failure).
        var cards = ProductCardHtmlParser.Parse(Fixture("no-products-page.html"));

        Assert.Empty(cards);
    }

    [Fact]
    public void Parse_CardMissingRequiredPriceContent_ThrowsRatherThanFabricatingAPrice()
    {
        var ex = Assert.Throws<FormatException>(() => ProductCardHtmlParser.Parse(Fixture("malformed-card.html")));

        Assert.Contains("title/price", ex.Message);
    }

    [Fact]
    public void Parse_CardWithNoOfficialIdAtAll_StillParsesWithNullIdsForCallerFallback()
    {
        // Synthetic (docs/retrieval/catalog-source-inventory.md): no real card observed during
        // investigation lacked both ids, but CatalogProductKey.Create's source+name fallback must
        // still be reachable — proven here by the parser degrading gracefully rather than assuming
        // an id always exists.
        var cards = ProductCardHtmlParser.Parse(Fixture("fallback-identity-card.html"));

        var card = Assert.Single(cards);
        Assert.Null(card.SanityProductId);
        Assert.Null(card.AccessoKeyword);
        Assert.Equal("Mystery Pass", card.DisplayName);
        Assert.Equal(59.00m, card.CurrentPriceAmount);
    }

    [Fact]
    public void Parse_NeverReadsTheQualifierOrSavingsTextAsThePrice()
    {
        // "Sale Price" / "Save $26" text must never leak into CurrentPriceAmount — a defensive
        // check that the exact-marker matching (not a loose "starts with" match) actually works.
        var cards = ProductCardHtmlParser.Parse(Fixture("season-passes-page.html"));

        var gold = cards.Single(c => c.DisplayName == "2027 Gold Pass");

        Assert.Equal(89.00m, gold.CurrentPriceAmount);
        Assert.DoesNotContain("Sale", gold.CurrentDisplayPrice);
        Assert.DoesNotContain("Save", gold.CurrentDisplayPrice);
    }
}
