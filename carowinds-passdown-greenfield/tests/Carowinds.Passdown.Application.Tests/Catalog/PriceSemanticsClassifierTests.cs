using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Application.Tests.Catalog;

/// <summary>Phase 13C Part AF: unit tests for the standalone classification/derivation logic — no Application.Associate composition involved.</summary>
public class PriceSemanticsClassifierTests
{
    [Fact]
    public void ClassifyCatalog_StartingFromQualifier_ClassifiesAsStartingFrom()
    {
        Assert.Equal(PriceKind.StartingFrom, PriceSemanticsClassifier.ClassifyCatalog("Starting From", comparisonPriceAmount: 80m));
    }

    [Fact]
    public void ClassifyCatalog_SalePriceQualifier_ClassifiesAsSale()
    {
        Assert.Equal(PriceKind.Sale, PriceSemanticsClassifier.ClassifyCatalog("Sale Price", comparisonPriceAmount: 115m));
    }

    [Fact]
    public void ClassifyCatalog_NoQualifierWithComparisonPrice_ClassifiesAsSale()
    {
        Assert.Equal(PriceKind.Sale, PriceSemanticsClassifier.ClassifyCatalog(null, comparisonPriceAmount: 60m));
    }

    [Fact]
    public void ClassifyCatalog_NoQualifierNoComparisonPrice_ClassifiesAsStandardCatalog()
    {
        Assert.Equal(PriceKind.StandardCatalog, PriceSemanticsClassifier.ClassifyCatalog(null, comparisonPriceAmount: null));
    }

    [Fact]
    public void ClassifyCatalog_QualifierIsCaseInsensitive()
    {
        Assert.Equal(PriceKind.StartingFrom, PriceSemanticsClassifier.ClassifyCatalog("starting from", comparisonPriceAmount: null));
    }

    [Fact]
    public void ClassifyCatalog_BundleShapedQualifierWithNoComparisonPrice_ClassifiesAsBundle()
    {
        Assert.Equal(PriceKind.Bundle, PriceSemanticsClassifier.ClassifyCatalog("Two Friends Just", comparisonPriceAmount: null));
    }

    [Fact]
    public void ClassifyCatalog_UnrecognizedQualifier_FallsBackToStandardCatalogRatherThanFabricating()
    {
        // "Passholder Exclusive" doesn't match the bundle shape ("<number word> <noun> Just") and
        // isn't "Starting From"/"Sale Price" — must not be guessed at, only the two known labels and
        // the one proven structural shape are ever specifically classified.
        Assert.Equal(PriceKind.StandardCatalog, PriceSemanticsClassifier.ClassifyCatalog("Passholder Exclusive", comparisonPriceAmount: null));
    }

    [Theory]
    [InlineData("Two Friends Just", 50, 2, 25)]
    [InlineData("Three People Just", 60, 3, 20)]
    [InlineData("Four Friends Just", 100, 4, 25)]
    public void TryParseBundle_RecognizedShape_DerivesQuantityAndPerPersonPrice(string qualifier, decimal total, int expectedQuantity, decimal expectedPerPerson)
    {
        var bundle = PriceSemanticsClassifier.TryParseBundle(qualifier, total);

        Assert.NotNull(bundle);
        Assert.Equal(expectedQuantity, bundle!.Quantity);
        Assert.Equal(expectedPerPerson, bundle.PerPersonPriceAmount);
    }

    [Fact]
    public void TryParseBundle_RoundsToTwoDecimalPlaces()
    {
        var bundle = PriceSemanticsClassifier.TryParseBundle("Three Friends Just", 100m);

        Assert.NotNull(bundle);
        Assert.Equal(33.33m, bundle!.PerPersonPriceAmount);
    }

    [Fact]
    public void TryParseBundle_NullQualifier_ReturnsNullRatherThanGuessing()
    {
        Assert.Null(PriceSemanticsClassifier.TryParseBundle(null, 50m));
    }

    [Fact]
    public void TryParseBundle_NullPrice_ReturnsNullRatherThanGuessing()
    {
        Assert.Null(PriceSemanticsClassifier.TryParseBundle("Two Friends Just", null));
    }

    [Fact]
    public void TryParseBundle_QualifierNotEndingInJust_ReturnsNull()
    {
        Assert.Null(PriceSemanticsClassifier.TryParseBundle("Starting From", 35m));
    }

    [Fact]
    public void TryParseBundle_NoRecognizedNumberWord_ReturnsNull()
    {
        Assert.Null(PriceSemanticsClassifier.TryParseBundle("Group Rate Just", 50m));
    }
}
