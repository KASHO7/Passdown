using System.Globalization;
using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Domain.Tests.Catalog;

/// <summary>
/// Phase 22E: <see cref="AdvertisedPriceText"/> — the one "From $x" rule shared by the PDFs, Teams Copy
/// and Associate → Products. The prefix follows <see cref="PriceKind"/> evidence only.
/// </summary>
public sealed class AdvertisedPriceTextTests
{
    [Theory]
    [InlineData(PriceKind.StartingFrom, "From $40.00")]
    [InlineData(PriceKind.ExactDated, "$40.00")]
    [InlineData(PriceKind.StandardCatalog, "$40.00")]
    [InlineData(PriceKind.Sale, "$40.00")]
    [InlineData(PriceKind.Bundle, "$40.00")]
    [InlineData(PriceKind.PerPerson, "$40.00")]
    [InlineData(PriceKind.Unknown, "$40.00")]
    public void Format_PrefixesFromOnlyForStartingFromEvidence(PriceKind kind, string expected) =>
        Assert.Equal(expected, AdvertisedPriceText.Format(40m, kind));

    [Theory]
    [InlineData(0, "From $0.00")]
    [InlineData(37, "From $37.00")]
    [InlineData(1234.5, "From $1234.50")]
    public void Format_UsesTheSharedUsdShape(double amount, string expected) =>
        Assert.Equal(expected, AdvertisedPriceText.Format((decimal)amount, PriceKind.StartingFrom));

    [Fact]
    public void FormatOrNull_MissingAmount_IsNullForEveryKind_NeverABareFrom()
    {
        foreach (var kind in Enum.GetValues<PriceKind>())
        {
            Assert.Null(AdvertisedPriceText.FormatOrNull(null, kind));
        }
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("en-GB")]
    [InlineData("ja-JP")]
    public void Format_IgnoresTheHostCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal("From $59.99", AdvertisedPriceText.Format(59.99m, PriceKind.StartingFrom));
            Assert.Equal("$59.99", AdvertisedPriceText.Format(59.99m, PriceKind.ExactDated));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
