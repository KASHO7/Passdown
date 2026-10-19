using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 22E: Associate → Products labels an advertised starting price "From $x" — from the row's
/// <see cref="PriceKind"/> evidence, the same rule the PDFs and Teams Copy print — and never makes a
/// starting price look exact (or an exact price look like a floor). Pure view-model tests; no rendering.
/// </summary>
public sealed class Phase22EProductPriceLabelTests
{
    private static readonly DateOnly Date = new(2026, 10, 24);
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 15, 0, 0, TimeSpan.Zero);

    private static AssociateVerificationInfo Verified => new(AssociateVerificationState.Verified, Now, null, null);

    private static AssociateProduct Product(
        string name,
        decimal? price,
        PriceKind kind,
        SourceCategory category = SourceCategory.DatedAdmissionTickets,
        string? qualifier = null,
        decimal? displayOverride = null,
        AssociateVerificationInfo? verification = null,
        string? officialDisplayPrice = null,
        DateOnly? date = null) => new(
        $"test|{category}|{name}",
        name,
        category,
        IsCustom: false,
        price,
        officialDisplayPrice,
        ComparisonPriceAmount: null,
        qualifier,
        DateSpecific: kind == PriceKind.ExactDated,
        date ?? Date,
        verification ?? Verified,
        IncludeOnPdf: true,
        displayOverride,
        kind);

    private static ProductRowViewModel Row(AssociateProduct product) => new(product, () => { });

    [Fact]
    public void ExactDatedPrice_IsShownExactly()
    {
        var row = Row(Product("Park Ticket", 59m, PriceKind.ExactDated));

        Assert.Equal("$59.00", row.OfficialPriceText);
        Assert.Equal("$59.00", row.EffectivePriceText);
        Assert.False(row.IsStartingPrice);
        Assert.Equal("Use official ($59.00)", row.OverridePlaceholderText);
    }

    [Fact]
    public void AdvertisedStartingPrice_IsLabeledFrom()
    {
        var row = Row(Product("Military & First Responder Tickets", 40m, PriceKind.StartingFrom, SourceCategory.MilitaryFirstResponder, "Starting From"));

        Assert.Equal("From $40.00", row.OfficialPriceText);
        Assert.Equal("From $40.00", row.EffectivePriceText);
        Assert.True(row.IsStartingPrice);
        Assert.Equal("Use official (From $40.00)", row.OverridePlaceholderText);
    }

    [Fact]
    public void StartingFromQualifier_IsFoldedIntoThePrice_OtherQualifiersStay()
    {
        Assert.False(Row(Product("A", 40m, PriceKind.StartingFrom, qualifier: "Starting From")).HasPriceQualifier);
        Assert.False(Row(Product("B", 40m, PriceKind.StartingFrom, qualifier: "  starting from ")).HasPriceQualifier);
        Assert.True(Row(Product("C", 40m, PriceKind.StartingFrom, qualifier: "Valid ID required")).HasPriceQualifier);
        Assert.True(Row(Product("D", 89m, PriceKind.Sale, qualifier: "Sale Price")).HasPriceQualifier);

        // A "Starting From" qualifier on a row whose evidence is not StartingFrom is not hidden — the
        // price does not carry it there.
        Assert.True(Row(Product("E", 40m, PriceKind.StandardCatalog, qualifier: "Starting From")).HasPriceQualifier);
    }

    [Theory]
    [InlineData(PriceKind.ExactDated)]
    [InlineData(PriceKind.StartingFrom)]
    [InlineData(PriceKind.StandardCatalog)]
    [InlineData(PriceKind.Unknown)]
    public void MissingPrice_IsHonestUnavailableText_NeverABareFrom(PriceKind kind)
    {
        var row = Row(Product("Retired Promotion", null, kind));

        Assert.Equal("Not priced", row.OfficialPriceText);
        Assert.Equal("Not priced", row.EffectivePriceText);
        Assert.DoesNotContain("From", row.OfficialPriceText, StringComparison.Ordinal);
    }

    [Fact]
    public void RetiredRow_WithOnlyASourceDisplayString_ShowsThatStringUnchanged()
    {
        var row = Row(Product("Retired Pass", null, PriceKind.StartingFrom, officialDisplayPrice: "Sold out"));

        Assert.Equal("Sold out", row.OfficialPriceText);
    }

    [Theory]
    [InlineData(PriceKind.StandardCatalog)]
    [InlineData(PriceKind.Sale)]
    [InlineData(PriceKind.Bundle)]
    [InlineData(PriceKind.Unknown)]
    public void NonStartingEvidence_WithAnAmount_IsNeverPrefixed(PriceKind kind)
    {
        // The amount's presence never decides exactness or "From" — only the evidence does.
        var row = Row(Product("Gold Season Pass", 129.99m, kind, SourceCategory.SeasonPasses));

        Assert.Equal("$129.99", row.OfficialPriceText);
        Assert.False(row.IsStartingPrice);
    }

    [Fact]
    public void StaleStartingPrice_KeepsFrom_AndItsStaleNote()
    {
        var stale = new AssociateVerificationInfo(AssociateVerificationState.Stale, Now.AddDays(-2), TimeSpan.FromDays(2), null);
        var row = Row(Product("Military & First Responder Tickets", 40m, PriceKind.StartingFrom, SourceCategory.MilitaryFirstResponder, verification: stale));

        Assert.Equal("From $40.00", row.OfficialPriceText);
        Assert.Equal("Stale — showing the last verified price.", row.VerificationNoteText);
    }

    [Fact]
    public void StaleExactPrice_StaysExact()
    {
        var stale = new AssociateVerificationInfo(AssociateVerificationState.Stale, Now.AddDays(-2), TimeSpan.FromDays(2), null);
        var row = Row(Product("Fast Lane", 65m, PriceKind.ExactDated, SourceCategory.DatedFastLane, verification: stale));

        Assert.Equal("$65.00", row.OfficialPriceText);
        Assert.NotNull(row.VerificationNoteText);
    }

    [Theory]
    [InlineData("Military Ticket")]
    [InlineData("First Responder Ticket")]
    [InlineData("Military & First Responder Tickets")]
    public void MilitaryAndFirstResponder_DatedExactPrice_IsExact(string name)
    {
        var row = Row(Product(name, 37m, PriceKind.ExactDated, SourceCategory.MilitaryFirstResponder));

        Assert.Equal("$37.00", row.OfficialPriceText);
        Assert.False(row.IsStartingPrice);
    }

    [Theory]
    [InlineData("Military Ticket")]
    [InlineData("First Responder Ticket")]
    [InlineData("Military & First Responder Tickets")]
    public void MilitaryAndFirstResponder_AdvertisedMinimumFallback_IsFrom(string name)
    {
        var row = Row(Product(name, 37m, PriceKind.StartingFrom, SourceCategory.MilitaryFirstResponder, "Starting From"));

        Assert.Equal("From $37.00", row.OfficialPriceText);
    }

    [Fact]
    public void HistoricalSelectedDate_KeepsTheRecordedEvidence()
    {
        var past = new DateOnly(2026, 8, 21);
        var exact = Row(Product("Bring-A-Friend Ticket", 45m, PriceKind.ExactDated, SourceCategory.Promotions, date: past));
        var from = Row(Product("Bring-A-Friend Ticket", 45m, PriceKind.StartingFrom, SourceCategory.Promotions, "Starting From", date: past));

        Assert.Equal("$45.00", exact.OfficialPriceText);
        Assert.Equal("From $45.00", from.OfficialPriceText);
    }

    [Fact]
    public void OverrideOnAStartingPriceRow_PrintsAsFrom_LikeThePdf_AndIsStoredAsTheNumberOnly()
    {
        var row = Row(Product("Military & First Responder Tickets", 40m, PriceKind.StartingFrom, SourceCategory.MilitaryFirstResponder, displayOverride: 35m));

        Assert.Equal("From $40.00", row.OfficialPriceText); // the official value is untouched
        Assert.Equal(35m, row.DisplayPriceOverride);
        Assert.Equal("$35.00", row.DisplayPriceOverrideText);
        Assert.Equal("From $35.00", row.EffectivePriceText);
        Assert.Equal("Prints as From $35.00", row.OverridePrintNoteText);
        Assert.True(row.HasOverridePrintNote);
    }

    [Fact]
    public void OverrideOnAnExactRow_HasNoPrintNote()
    {
        var row = Row(Product("Park Ticket", 59m, PriceKind.ExactDated, displayOverride: 45m));

        Assert.Equal("$45.00", row.EffectivePriceText);
        Assert.Null(row.OverridePrintNoteText);
        Assert.False(row.HasOverridePrintNote);
    }

    [Fact]
    public void EditingAndClearingAnOverride_UpdatesThePrintNote()
    {
        var row = Row(Product("Military & First Responder Tickets", 40m, PriceKind.StartingFrom, SourceCategory.MilitaryFirstResponder));
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row.DisplayPriceOverrideText = "33";
        Assert.Equal("Prints as From $33.00", row.OverridePrintNoteText);
        Assert.Contains(nameof(ProductRowViewModel.OverridePrintNoteText), changed);

        row.ResetToOfficialCommand.Execute(null);
        Assert.Null(row.OverridePrintNoteText);
        Assert.Equal("From $40.00", row.EffectivePriceText);
    }

    /// <summary>The key invariant: for every product, the Associate row says exactly what the PDF (and so Teams Copy) prints.</summary>
    [Fact]
    public void AssociateRows_MatchThePdfDocumentModel_ForEveryEvidenceKind()
    {
        List<AssociateProduct> products =
        [
            Product("Park Ticket", 49.99m, PriceKind.ExactDated),
            Product("Military & First Responder Tickets", 40m, PriceKind.StartingFrom, SourceCategory.MilitaryFirstResponder, "Starting From"),
            Product("First Responder Ticket", 37m, PriceKind.ExactDated, SourceCategory.MilitaryFirstResponder),
            Product("Bring-A-Friend Ticket", 45m, PriceKind.StartingFrom, SourceCategory.Promotions, "Starting From"),
            Product("Fast Lane", 65m, PriceKind.ExactDated, SourceCategory.DatedFastLane, displayOverride: 60m),
            Product("Fast Lane Plus", 109m, PriceKind.StartingFrom, SourceCategory.DatedFastLane, displayOverride: 99m),
            Product("Gold Season Pass", 129.99m, PriceKind.StandardCatalog, SourceCategory.SeasonPasses),
            Product("All Season Dining", 159.99m, PriceKind.Sale, SourceCategory.AddOns, "Sale Price"),
        ];
        var workspace = FakeAssociatePassdownService.DefaultWorkspace(Date) with { Products = products };
        workspace = workspace with { Draft = workspace.Draft with { SectionSelection = PassdownSectionSelection.AllIncluded } };

        var pdfRows = AssociatePassdownDocumentModelBuilder.Build(workspace, Now)
            .ProductCategories.SelectMany(category => category.Products)
            .ToDictionary(row => row.DisplayName, row => row.EffectivePriceText);

        foreach (var product in products)
        {
            Assert.True(pdfRows.TryGetValue(product.DisplayName, out var pdfPrice), $"PDF is missing {product.DisplayName}");
            Assert.Equal(pdfPrice, Row(product).EffectivePriceText);
        }

        var teams = AssociateTeamsTextFormatter.FormatPricing(AssociatePassdownDocumentModelBuilder.Build(workspace, Now))!;
        Assert.Contains("Military & First Responder Tickets: From $40.00", teams, StringComparison.Ordinal);
        Assert.Contains("First Responder Ticket: $37.00", teams, StringComparison.Ordinal);
        Assert.Contains("Fast Lane Plus: From $99.00", teams, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssociateProductsSection_ShowsFromThroughTheRealViewModel_WithoutAnOnlineCall()
    {
        var service = new FakeAssociatePassdownService
        {
            WorkspaceFactory = date => FakeAssociatePassdownService.DefaultWorkspace(date) with
            {
                Products =
                [
                    Product("Park Ticket", 49.99m, PriceKind.ExactDated),
                    Product("Military & First Responder Tickets", 40m, PriceKind.StartingFrom, SourceCategory.MilitaryFirstResponder, "Starting From"),
                ],
            },
        };
        var viewModel = new AssociateViewModel(service, new FakeClock(Now));
        await viewModel.PendingLoad;

        var rows = viewModel.ProductSections.SelectMany(section => section.Products).ToDictionary(row => row.DisplayName);
        Assert.Equal("$49.99", rows["Park Ticket"].OfficialPriceText);
        Assert.Equal("From $40.00", rows["Military & First Responder Tickets"].OfficialPriceText);
        Assert.Empty(service.CheckOnlineCalls);
    }

    [Fact]
    public void ProductsMarkup_BindsTheEvidenceLabelledPrice_AndThePrintNote()
    {
        var markup = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/AssociateView.axaml"));

        Assert.Contains("Text=\"{Binding OfficialPriceText}\" Classes=\"officialPrice\"", markup);
        Assert.Contains("Text=\"{Binding OverridePrintNoteText}\" IsVisible=\"{Binding HasOverridePrintNote}\"", markup);
        Assert.Contains("IsVisible=\"{Binding HasPriceQualifier}\"", markup);
    }
}
