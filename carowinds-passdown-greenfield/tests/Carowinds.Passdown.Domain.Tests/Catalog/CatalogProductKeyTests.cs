using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Domain.Tests.Catalog;

public class CatalogProductKeyTests
{
    [Fact]
    public void Create_PrefersOfficialIdOverUrlAndName()
    {
        var withId = CatalogProductKey.Create(
            SourceCategory.SeasonPasses,
            sourceName: "Season passes",
            productName: "Gold Pass",
            officialProductId: "gold-pass-2027",
            officialUrl: new Uri("https://www.sixflags.com/carowinds/passes/gold"));

        var byId = CatalogProductKey.FromOfficialId(SourceCategory.SeasonPasses, "gold-pass-2027");

        Assert.Equal(byId, withId);
    }

    [Fact]
    public void Create_PrefersUrlOverNameWhenNoOfficialId()
    {
        var url = new Uri("https://www.sixflags.com/carowinds/passes/gold");

        var withUrl = CatalogProductKey.Create(
            SourceCategory.SeasonPasses,
            sourceName: "Season passes",
            productName: "Gold Pass",
            officialProductId: null,
            officialUrl: url);

        var byUrl = CatalogProductKey.FromOfficialUrl(SourceCategory.SeasonPasses, url);

        Assert.Equal(byUrl, withUrl);
    }

    [Fact]
    public void Create_FallsBackToSourceAndNameWhenNoIdOrUrl()
    {
        var withName = CatalogProductKey.Create(
            SourceCategory.SeasonPasses,
            sourceName: "Season passes",
            productName: "Gold Pass");

        var byName = CatalogProductKey.FromSourceAndName(SourceCategory.SeasonPasses, "Season passes", "Gold Pass");

        Assert.Equal(byName, withName);
    }

    [Theory]
    [InlineData("Gold Pass", "gold pass")]
    [InlineData("  Gold   Pass  ", "gold pass")]
    [InlineData("GOLD PASS", "gold pass")]
    public void FromSourceAndName_NormalizesCaseAndWhitespaceToTheSameKey(string variant, string canonical)
    {
        var fromVariant = CatalogProductKey.FromSourceAndName(SourceCategory.SeasonPasses, "Season passes", variant);
        var fromCanonical = CatalogProductKey.FromSourceAndName(SourceCategory.SeasonPasses, "Season passes", canonical);

        Assert.Equal(fromCanonical, fromVariant);
    }

    [Fact]
    public void FromSourceAndName_DifferentProductNamesAreDifferentKeys()
    {
        var gold = CatalogProductKey.FromSourceAndName(SourceCategory.SeasonPasses, "Season passes", "Gold Pass");
        var platinum = CatalogProductKey.FromSourceAndName(SourceCategory.SeasonPasses, "Season passes", "Platinum Pass");

        Assert.NotEqual(gold, platinum);
    }

    [Fact]
    public void FromSourceAndName_SameNameDifferentCategoryAreDifferentKeys()
    {
        var seasonPass = CatalogProductKey.FromSourceAndName(SourceCategory.SeasonPasses, "Season passes", "Fast Lane Plus");
        var addOn = CatalogProductKey.FromSourceAndName(SourceCategory.AddOns, "Season passes", "Fast Lane Plus");

        Assert.NotEqual(seasonPass, addOn);
    }

    [Fact]
    public void FromOfficialId_BlankIdThrows()
    {
        Assert.Throws<ArgumentException>(() => CatalogProductKey.FromOfficialId(SourceCategory.SeasonPasses, "  "));
    }

    [Theory]
    [InlineData(SourceCategory.SeasonPasses)]
    [InlineData(SourceCategory.AddOns)]
    public void Parse_RoundTripsToStringOutputForEveryKeyShape(SourceCategory category)
    {
        var byId = CatalogProductKey.FromOfficialId(category, "gold-pass-2027");
        var byUrl = CatalogProductKey.FromOfficialUrl(category, new Uri("https://www.sixflags.com/carowinds/passes/gold"));
        var byName = CatalogProductKey.FromSourceAndName(category, "Season passes", "Gold Pass");

        Assert.Equal(byId, CatalogProductKey.Parse(byId.ToString()));
        Assert.Equal(byUrl, CatalogProductKey.Parse(byUrl.ToString()));
        Assert.Equal(byName, CatalogProductKey.Parse(byName.ToString()));
    }

    [Fact]
    public void Parse_UnrecognizedCategoryPrefixThrows()
    {
        Assert.Throws<ArgumentException>(() => CatalogProductKey.Parse("NotACategory:name:foo|bar"));
    }

    [Fact]
    public void Parse_MissingSeparatorThrows()
    {
        Assert.Throws<ArgumentException>(() => CatalogProductKey.Parse("SeasonPasses"));
    }

    [Fact]
    public void Parse_BlankInputThrows()
    {
        Assert.Throws<ArgumentException>(() => CatalogProductKey.Parse("  "));
    }
}
