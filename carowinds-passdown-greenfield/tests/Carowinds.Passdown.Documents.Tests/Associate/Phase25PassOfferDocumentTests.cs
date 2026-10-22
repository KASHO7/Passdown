using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Documents.Tests.Associate;

/// <summary>
/// Phase 25: the Associate PDF and Teams Copy keep their established Season Pass contract — one
/// printed row per Season Pass with its one-time new-purchase price — and never print a Membership
/// rate, an initiation fee, a renewal offer or an unavailable option. Every template still renders.
/// </summary>
public sealed class Phase25PassOfferDocumentTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 10, 22, 10, 0, 0, TimeSpan.Zero);

    private static AssociateProduct Pass(PassOffer offer, decimal? price, decimal? comparison = null, string? identity = null) =>
        new(
            identity ?? AssociateProductIdentity.ForCatalogProduct(offer.ToKey().ToString()),
            offer.DisplayName,
            SourceCategory.SeasonPasses,
            IsCustom: false,
            price,
            OfficialDisplayPrice: price is null ? "Not Available" : $"${price}",
            comparison,
            PriceQualifier: null,
            DateSpecific: false,
            ApplicableDate: null,
            AssociateVerificationInfo.NotCheckedYet,
            IncludeOnPdf: true,
            DisplayPriceOverride: null,
            PriceKind: comparison is null ? PriceKind.StandardCatalog : PriceKind.Sale)
        { PassOffer = offer };

    private static AssociateWorkspace Workspace() => AssociateWorkspaceFixtures.MinimalValid() with
    {
        Products =
        [
            Pass(PassOffer.SeasonPass("Gold", PassPriceType.NewPurchase, 2027), 99m, 115m) with { RenewalPriceAmount = 89m },
            Pass(PassOffer.SeasonPass("Prestige", PassPriceType.NewPurchase, 2027), 249m, 275m),
            Pass(PassOffer.SeasonPass("Prestige", PassPriceType.Renewal, 2027), 239m), // an unfolded renewal row
            Pass(PassOffer.Membership("Gold", PassPriceType.MonthlyRate), 7m) with { InitiationFeeAmount = 20m },
            Pass(PassOffer.Membership("Prestige", PassPriceType.InitiationFee), 20m), // an unfolded initiation fee
            Pass(PassOffer.Membership("Silver", PassPriceType.MonthlyRate), null),
        ],
    };

    private static AssociatePassdownDocumentModel Model() => AssociatePassdownDocumentModelBuilder.Build(Workspace(), GeneratedAt);

    [Fact]
    public void Pdf_PrintsOnlySeasonPassNewPurchasePrices_UnderSeasonPasses()
    {
        var model = Model();

        var seasonPasses = Assert.Single(model.ProductCategories, c => c.Section == ProductDisplaySection.SeasonPasses);
        Assert.Equal(["Gold Season Pass — 2027", "Prestige Season Pass — 2027"], seasonPasses.Products.Select(p => p.DisplayName).Order());
        Assert.DoesNotContain(model.ProductCategories, c => c.Section == ProductDisplaySection.Memberships);
        Assert.DoesNotContain(model.ProductCategories.SelectMany(c => c.Products), p =>
            p.DisplayName.Contains("Membership", StringComparison.Ordinal) || p.DisplayName.Contains("Renewal", StringComparison.Ordinal));
    }

    [Fact]
    public void TeamsCopy_CarriesTheSamePassPricesAndNoMembershipOrRenewalAmount()
    {
        var text = AssociateTeamsTextFormatter.FormatPricing(Model());

        Assert.Contains("Gold Season Pass — 2027", text);
        Assert.Contains("$99", text);
        Assert.DoesNotContain("Membership", text);
        Assert.DoesNotContain("Renewal", text);
        Assert.DoesNotContain("$7", text);
        Assert.DoesNotContain("$239", text);
        Assert.DoesNotContain("$89", text);
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void EveryTemplate_StillGenerates(AssociatePassdownTemplate template)
    {
        var result = new AssociatePassdownPdfGenerator().Generate(Model(), template);

        Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
    }

    [Fact]
    public void ModelWithoutPassOffers_IsUnchangedByPhase25()
    {
        var legacyWorkspace = AssociateWorkspaceFixtures.MinimalValid() with
        {
            Products =
            [
                new AssociateProduct("catalog|SeasonPasses:id:legacy", "2027 Gold Pass", SourceCategory.SeasonPasses, false, 89m, "$89", 115m, "Sale Price",
                    false, null, AssociateVerificationInfo.NotCheckedYet, true, null, PriceKind.Sale),
            ],
        };

        var model = AssociatePassdownDocumentModelBuilder.Build(legacyWorkspace, GeneratedAt);

        var row = Assert.Single(Assert.Single(model.ProductCategories).Products);
        Assert.Equal("2027 Gold Pass", row.DisplayName);
    }
}
