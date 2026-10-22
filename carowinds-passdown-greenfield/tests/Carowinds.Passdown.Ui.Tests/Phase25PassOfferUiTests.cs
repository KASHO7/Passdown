using System.Text.RegularExpressions;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 25: Products rows and Price History for Season Passes vs Memberships — units, renewal,
/// reference and initiation labels, the not-available option, PDF controls, source attribution and
/// the price-type filter. View-model/presentation level plus markup-string checks (the mixed
/// partition's rule: never assert bound values from a live view). Values are fixtures.
/// </summary>
public class Phase25PassOfferUiTests
{
    private static readonly DateTimeOffset VerifiedAt = new(2026, 10, 22, 14, 17, 0, TimeSpan.Zero);
    private static readonly Uri PageUrl = new("https://www.sixflags.com/carowinds/memberships-and-season-passes");

    private static AssociateProduct Product(PassOffer offer, decimal? price, decimal? comparison = null, string? qualifier = null) =>
        new(
            AssociateProductIdentity.ForCatalogProduct(offer.ToKey().ToString()), offer.DisplayName, SourceCategory.SeasonPasses, IsCustom: false,
            price, price is null ? "Not Available" : $"${price}", comparison, qualifier, DateSpecific: false, ApplicableDate: null,
            new AssociateVerificationInfo(AssociateVerificationState.Verified, VerifiedAt, null,
                "Verified 3 Season Passes, 2 renewal offers, 2 Memberships (1 not available). Official note: *Plus processing fee of up to $9.99 & applicable taxes."),
            IncludeOnPdf: true, DisplayPriceOverride: null, PriceKind: comparison is null ? PriceKind.StandardCatalog : PriceKind.Sale)
        {
            PassOffer = offer,
            OfficialSourceUrl = PageUrl,
        };

    private static readonly AssociateProduct GoldPass =
        Product(PassOffer.SeasonPass("Gold", PassPriceType.NewPurchase, 2027), 99m, 115m) with { RenewalPriceAmount = 89m };

    private static readonly AssociateProduct GoldMembership =
        Product(PassOffer.Membership("Gold", PassPriceType.MonthlyRate), 7m, qualifier: "Auto-renews After 12mo") with { InitiationFeeAmount = 20m };

    private static readonly AssociateProduct SilverMembership = Product(PassOffer.Membership("Silver", PassPriceType.MonthlyRate), null);

    private static ProductRowViewModel Row(AssociateProduct product) => new(product, () => { });

    [Fact]
    public void SeasonPassRow_ShowsOneTimePrice_ReferencePrice_AndRenewalOffer()
    {
        var row = Row(GoldPass);

        Assert.Equal("$99.00", row.OfficialPriceText);
        Assert.Equal("Reference price $115.00", row.ReferencePriceNoteText);
        Assert.False(row.HasCompareAtLine);
        Assert.Equal("Renewal offer $89.00", row.RenewalNoteText);
        Assert.Null(row.InitiationFeeNoteText);
        Assert.True(row.CanIncludeOnPdf);
        Assert.Null(row.NotPrintedNoteText);
    }

    [Fact]
    public void MembershipRow_ShowsAMonthlyRateAndASeparateInitiationFee_AndIsNotPrinted()
    {
        var row = Row(GoldMembership);

        Assert.Equal("$7.00/month", row.OfficialPriceText);
        Assert.Equal("$20.00 initiation fee", row.InitiationFeeNoteText);
        Assert.Equal("Auto-renews After 12mo", row.PriceQualifier);
        Assert.True(row.HasPriceQualifier);
        Assert.Null(row.RenewalNoteText);
        Assert.False(row.CanIncludeOnPdf);
        Assert.Null(row.NotPrintedNoteText); // the Memberships group says it once
    }

    [Fact]
    public void UnavailableMembership_ReadsNotAvailable_NeverZeroDollars()
    {
        var row = Row(SilverMembership);

        Assert.Equal("Not available", row.OfficialPriceText);
        Assert.DoesNotContain("$", row.OfficialPriceText);
        Assert.False(row.CanIncludeOnPdf);
    }

    [Fact]
    public void UnfoldedRenewalRow_SaysItIsNotPrinted()
    {
        var row = Row(Product(PassOffer.SeasonPass("Gold", PassPriceType.Renewal, 2027), 89m));

        Assert.False(row.CanIncludeOnPdf);
        Assert.Equal("Not printed on the passdown.", row.NotPrintedNoteText);
        Assert.True(row.HasNotPrintedNote);
    }

    [Fact]
    public void NonPassProducts_KeepCompareAtWording()
    {
        var addOn = new AssociateProduct(
            "catalog|AddOns:id:fl", "All Season Fast Lane", SourceCategory.AddOns, false, 499m, "$499", 899m, null, false, null,
            AssociateVerificationInfo.NotCheckedYet, true, null, PriceKind.Sale);

        var row = Row(addOn);

        Assert.True(row.HasCompareAtLine);
        Assert.Equal("$899.00", row.ComparisonPriceText);
        Assert.Null(row.ReferencePriceNoteText);
        Assert.True(row.CanIncludeOnPdf);
    }

    [Fact]
    public void SourceNote_NamesTheOfficialPage_TheEasternVerifiedTime_AndTheFeeDisclosure()
    {
        var note = PassSourcePresentation.SourceNote(ProductDisplaySection.Memberships, [GoldMembership, SilverMembership]);

        Assert.NotNull(note);
        Assert.StartsWith("Official Carowinds source (www.sixflags.com/carowinds/memberships-and-season-passes) · last verified Oct 22", note);
        Assert.Contains("10:17", note); // 14:17 UTC = 10:17 AM Eastern
        Assert.Contains("*Plus processing fee of up to $9.99 & applicable taxes.", note);
        Assert.EndsWith(PassSourcePresentation.MembershipsNotPrintedText, note);
        Assert.DoesNotContain(PassSourcePresentation.MembershipsNotPrintedText, PassSourcePresentation.SourceNote(ProductDisplaySection.SeasonPasses, [GoldPass]));
        Assert.Null(PassSourcePresentation.SourceNote(ProductDisplaySection.AddOns, [GoldPass]));
    }

    [Fact]
    public void PriceHistory_FormatsMonthlySeriesWithTheirUnit()
    {
        var interval1 = new SeasonPriceHistoryInterval("k", 1, 7m, PriceKind.StandardCatalog, "$7/mo", null, "Gold Membership", "src", VerifiedAt, VerifiedAt, SeasonPriceHistoryOrigin.Observed);
        var interval2 = interval1 with { Sequence = 2, Amount = 9m, FirstObservedAt = VerifiedAt.AddDays(30), LastConfirmedAt = VerifiedAt.AddDays(30) };
        var entries = new List<PriceHistoryEntry>
        {
            new(interval1, null),
            new(interval2, SeasonPriceChange.Between(interval1.State, interval2.State)),
        };

        var rows = PriceHistoryPresentation.BuildRows(entries, PassPriceUnit.PerMonth);

        Assert.Equal(["$7.00/month", "$9.00/month"], rows.Select(r => r.PriceText));
        Assert.Equal("+$2.00/month (+28.6%)", rows[1].ChangeText);
        Assert.Equal("$99.00 (sale price)", PriceHistoryPresentation.FormatPrice(99m, PriceKind.Sale, PassPriceUnit.OneTime));
    }

    [Fact]
    public void PriceHistory_FamilyLabels_DistinguishEveryPriceType()
    {
        var labels = Enum.GetValues<PriceHistoryFamily>().Select(PriceHistoryPresentation.FamilyText).ToList();

        Assert.Equal(labels.Count, labels.Distinct().Count());
        Assert.Equal("Membership · monthly rate", PriceHistoryPresentation.FamilyText(PriceHistoryFamily.Membership));
        Assert.Equal("Season Pass renewal offer", PriceHistoryPresentation.FamilyText(PriceHistoryFamily.SeasonPassRenewal));
        Assert.Equal("Membership · initiation fee", PriceHistoryPresentation.FamilyText(PriceHistoryFamily.MembershipInitiationFee));
        Assert.Equal("Season Pass", PriceHistoryPresentation.FamilyText(PriceHistoryFamily.SeasonPass));
    }

    private static FakeSeasonPriceHistoryService Service() => new FakeSeasonPriceHistoryService()
        .With("gold-pass", "Gold Season Pass — 2027", active: true, PriceHistoryFamily.SeasonPass, (89m, PriceKind.Sale, VerifiedAt), (99m, PriceKind.Sale, VerifiedAt.AddDays(1)))
        .With("gold-renewal", "Gold Season Pass Renewal — 2027", active: true, PriceHistoryFamily.SeasonPassRenewal, (89m, PriceKind.StandardCatalog, VerifiedAt))
        .With("gold-membership", "Gold Membership", active: true, PriceHistoryFamily.Membership, (7m, PriceKind.StandardCatalog, VerifiedAt))
        .With("gold-fee", "Gold Membership Initiation Fee", active: true, PriceHistoryFamily.MembershipInitiationFee, (20m, PriceKind.StandardCatalog, VerifiedAt))
        .With("dining", "All Season Dining", active: true, PriceHistoryFamily.SeasonPassAddOn, (65m, PriceKind.StandardCatalog, VerifiedAt));

    [Fact]
    public async Task PriceHistory_FilterByPriceType_ShowsOnlyThatType()
    {
        var vm = new PriceHistoryViewModel(Service());
        await vm.OpenAsync(null);
        Assert.Equal(5, vm.Products.Count);

        vm.SelectedFamilyFilter = vm.FamilyFilters.Single(f => f.Family == PriceHistoryFamily.Membership);
        await vm.PendingHistoryLoad;

        Assert.Equal(["Gold Membership"], vm.Products.Select(p => p.DisplayName));
        Assert.Equal("gold-membership", vm.SelectedProduct!.ProductKey);
        Assert.Equal(["$7.00/month"], vm.Rows.Select(r => r.PriceText));

        vm.SelectedFamilyFilter = vm.FamilyFilters[0];
        Assert.Equal(5, vm.Products.Count);
    }

    [Fact]
    public async Task PriceHistory_OneTimeSeries_IsNeverShownPerMonth()
    {
        var vm = new PriceHistoryViewModel(Service());

        await vm.OpenAsync("gold-pass");
        await vm.PendingHistoryLoad;

        Assert.Equal(["$89.00 (sale price)", "$99.00 (sale price)"], vm.Rows.Select(r => r.PriceText));
        Assert.All(vm.Rows, r => Assert.DoesNotContain("/month", r.PriceText));
    }

    private static string Markup(string view) =>
        Regex.Replace(File.ReadAllText(MarkupFiles.Find($"src/Carowinds.Passdown.Desktop/Views/{view}.axaml")), "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    [Fact]
    public void ProductsMarkup_BindsTheNewPassLabels_WithAccessibleNames()
    {
        var markup = Markup("AssociateView");

        Assert.Contains("{Binding RenewalNoteText}", markup);
        Assert.Contains("{Binding InitiationFeeNoteText}", markup);
        Assert.Contains("{Binding ReferencePriceNoteText}", markup);
        Assert.Contains("IsVisible=\"{Binding CanIncludeOnPdf}\"", markup);
        Assert.Contains("{Binding NotPrintedNoteText}", markup);
        Assert.Contains("IsVisible=\"{Binding HasNotPrintedNote}\"", markup);
        Assert.Contains("AutomationProperties.Name=\"{Binding SourceNoteText}\"", markup);
        Assert.Contains("AutomationProperties.Name=\"{Binding RenewalNoteText}\"", markup);
        Assert.Contains("AutomationProperties.Name=\"{Binding InitiationFeeNoteText}\"", markup);
    }

    [Fact]
    public void PriceHistoryMarkup_HasAnAccessiblePriceTypeFilter()
    {
        var markup = Markup("PriceHistoryView");

        Assert.Contains("ItemsSource=\"{Binding FamilyFilters}\"", markup);
        Assert.Contains("SelectedItem=\"{Binding SelectedFamilyFilter}\"", markup);
        Assert.Contains("AutomationProperties.Name=\"Filter by price type\"", markup);
    }

    [Fact]
    public void NewPassText_UsesOnlyThemeTokens_NoHardCodedColors()
    {
        var markup = Markup("AssociateView");
        var passBlock = markup[markup.IndexOf("{Binding SourceNoteText}", StringComparison.Ordinal)..markup.IndexOf("{Binding ResetToOfficialCommand}", StringComparison.Ordinal)];

        Assert.DoesNotMatch("(Foreground|Background)=\"#", passBlock);
    }
}
