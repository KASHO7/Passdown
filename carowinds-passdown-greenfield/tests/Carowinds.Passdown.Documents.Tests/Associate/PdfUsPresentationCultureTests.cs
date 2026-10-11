using System.Globalization;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;

namespace Carowinds.Passdown.Documents.Tests.Associate;

/// <summary>
/// Phase 18B: the Associate PDF's date header, hours, generated stamp, counts, and prices are en-US
/// on any Windows region — the model built under a foreign culture is identical to the en-US one.
/// </summary>
public sealed class PdfUsPresentationCultureTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 9, 19, 14, 30, 0, TimeSpan.Zero);

    private static AssociatePassdownDocumentModel BuildUnder(string culture)
    {
        var previous = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            return AssociatePassdownDocumentModelBuilder.Build(AssociateWorkspaceFixtures.NormalDay(), GeneratedAt);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = previous;
        }
    }

    [Theory]
    [InlineData("en-GB")]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("th-TH")]
    public void Model_UnderForeignCulture_IsIdenticalToEnUs(string culture)
    {
        var expected = BuildUnder("en-US");
        var actual = BuildUnder(culture);

        Assert.Equal(expected.OperatingDateText, actual.OperatingDateText);
        Assert.Equal(expected.GeneratedAtText, actual.GeneratedAtText);
        Assert.Equal(expected.ParkHours.HeadlineText, actual.ParkHours.HeadlineText);
        Assert.Equal(
            expected.ProductCategories.SelectMany(c => c.Products).Select(p => p.EffectivePriceText),
            actual.ProductCategories.SelectMany(c => c.Products).Select(p => p.EffectivePriceText));
        Assert.Matches(@"^(Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday), [A-Z][a-z]+ \d{1,2}, 2026$", actual.OperatingDateText);
        Assert.Contains("9:00 AM", actual.ParkHours.HeadlineText);
        Assert.Matches(@"^[A-Z][a-z]{2} \d{1,2} • \d{1,2}:\d\d (AM|PM)$", actual.GeneratedAtText);
        Assert.All(actual.ProductCategories.SelectMany(c => c.Products).Select(p => p.EffectivePriceText).OfType<string>(),
            price => Assert.Matches(@"^(From )?\$\d+\.\d\d", price));
    }
}
