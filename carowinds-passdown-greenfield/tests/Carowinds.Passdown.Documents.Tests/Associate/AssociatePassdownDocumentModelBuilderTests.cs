using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Documents.Tests.Associate;

/// <summary>
/// Phase 8A: <see cref="AssociatePassdownDocumentModelBuilder"/> section-visibility tests, asserting
/// directly against the built <see cref="AssociatePassdownDocumentModel"/> rather than rendered PDF
/// text/pixels (spec's "prefer testing document/PDF content models rather than brittle
/// pixel-level PDF tests") — a hidden section must not appear in the model at all, which is what
/// then keeps it from consuming any PDF space.
/// </summary>
public sealed class AssociatePassdownDocumentModelBuilderTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 21, 6, 8, 0, TimeSpan.Zero);

    private static AssociatePassdownDocumentModel Build(SourceCategory category, bool included)
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        var selection = category switch
        {
            SourceCategory.DatedAdmissionTickets => PassdownSectionSelection.AllIncluded with { IncludeDailyAdmission = included },
            SourceCategory.DatedFastLane => PassdownSectionSelection.AllIncluded with { IncludeFastLane = included },
            SourceCategory.Promotions => PassdownSectionSelection.AllIncluded with { IncludePromotions = included },
            SourceCategory.SeasonPasses => PassdownSectionSelection.AllIncluded with { IncludeSeasonPasses = included },
            SourceCategory.AddOns => PassdownSectionSelection.AllIncluded with { IncludeAddOns = included },
            SourceCategory.HauntedAttractions => PassdownSectionSelection.AllIncluded with { IncludeHauntedAttractions = included },
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

        workspace = workspace with { Draft = workspace.Draft with { SectionSelection = selection } };
        return AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);
    }

    private static bool HasCategory(AssociatePassdownDocumentModel model, string categoryDisplayName) =>
        model.ProductCategories.Any(category => category.CategoryName == categoryDisplayName);

    [Fact]
    public void DailyAdmission_CanBeHidden()
    {
        Assert.True(HasCategory(Build(SourceCategory.DatedAdmissionTickets, included: true), "Daily Admission"));
        Assert.False(HasCategory(Build(SourceCategory.DatedAdmissionTickets, included: false), "Daily Admission"));
    }

    [Fact]
    public void FastLane_CanBeHidden()
    {
        Assert.True(HasCategory(Build(SourceCategory.DatedFastLane, included: true), "Fast Lane"));
        Assert.False(HasCategory(Build(SourceCategory.DatedFastLane, included: false), "Fast Lane"));
    }

    [Fact]
    public void Promotions_CanBeHidden()
    {
        // NormalDay carries no Promotions products of its own; add one so hiding it is observable.
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        workspace = workspace with { Products = [.. workspace.Products, PromotionProduct()] };

        var shown = AssociatePassdownDocumentModelBuilder.Build(
            workspace with { Draft = workspace.Draft with { SectionSelection = PassdownSectionSelection.AllIncluded } }, GeneratedAt);
        var hidden = AssociatePassdownDocumentModelBuilder.Build(
            workspace with { Draft = workspace.Draft with { SectionSelection = PassdownSectionSelection.AllIncluded with { IncludePromotions = false } } }, GeneratedAt);

        Assert.True(HasCategory(shown, "Promotions"));
        Assert.False(HasCategory(hidden, "Promotions"));
    }

    [Fact]
    public void SeasonPasses_CanBeHidden()
    {
        Assert.True(HasCategory(Build(SourceCategory.SeasonPasses, included: true), "Season Passes"));
        Assert.False(HasCategory(Build(SourceCategory.SeasonPasses, included: false), "Season Passes"));
    }

    [Fact]
    public void AddOns_CanBeHidden()
    {
        Assert.True(HasCategory(Build(SourceCategory.AddOns, included: true), "Add-Ons"));
        Assert.False(HasCategory(Build(SourceCategory.AddOns, included: false), "Add-Ons"));
    }

    // Phase 8B: corrects the Phase 8A omission that left SCarowinds/Haunt always included with no
    // visibility flag of its own.
    [Fact]
    public void SCarowinds_CanBeHidden()
    {
        // NormalDay carries no Haunted Attractions products of its own; add one so hiding it is observable.
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        workspace = workspace with { Products = [.. workspace.Products, HauntedAttractionProduct()] };

        var shown = AssociatePassdownDocumentModelBuilder.Build(
            workspace with { Draft = workspace.Draft with { SectionSelection = PassdownSectionSelection.AllIncluded } }, GeneratedAt);
        var hidden = AssociatePassdownDocumentModelBuilder.Build(
            workspace with { Draft = workspace.Draft with { SectionSelection = PassdownSectionSelection.AllIncluded with { IncludeHauntedAttractions = false } } }, GeneratedAt);

        Assert.True(HasCategory(shown, "SCarowinds"));
        Assert.False(HasCategory(hidden, "SCarowinds"));
    }

    /// <summary>Phase 8B: SCarowinds defaults to included, matching every other optional section.</summary>
    [Fact]
    public void SCarowinds_DefaultsToIncluded()
    {
        Assert.True(PassdownSectionSelection.AllIncluded.IncludeHauntedAttractions);
    }

    [Fact]
    public void OperationalNotes_CanBeHidden()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();

        var shown = AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);
        Assert.NotEmpty(shown.OperationalNotes);

        var hiddenWorkspace = workspace with { Draft = workspace.Draft with { SectionSelection = PassdownSectionSelection.AllIncluded with { IncludeOperationalNotes = false } } };
        var hidden = AssociatePassdownDocumentModelBuilder.Build(hiddenWorkspace, GeneratedAt);
        Assert.Empty(hidden.OperationalNotes);
    }

    /// <summary>Mandatory content has no corresponding flag in <see cref="PassdownSectionSelection"/> at all, so excluding every optional section cannot remove it from the model.</summary>
    [Fact]
    public void MandatoryContent_IsUnaffectedByAnySectionSelection()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        var allExcluded = new PassdownSectionSelection(false, false, false, false, false, false, false); // Daily Admission, Fast Lane, Promotions, Season Passes, Add-Ons, Operational Notes, SCarowinds.
        workspace = workspace with { Draft = workspace.Draft with { SectionSelection = allExcluded } };

        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);

        Assert.NotNull(model.OperatingDate);
        Assert.NotEqual(PdfParkHoursStatus.NotCheckedYet, model.ParkHours.Status);
        Assert.NotNull(model.Attendance.ProjectedAttendance);
        Assert.True(model.EarlyRideTime.Enabled);
        Assert.True(model.DailyFeature.HasContent);
    }

    private static AssociateProduct HauntedAttractionProduct() => new(
        "catalog|haunted|test",
        "Test Haunted Attraction",
        SourceCategory.HauntedAttractions,
        IsCustom: false,
        OfficialPriceAmount: 15m,
        OfficialDisplayPrice: null,
        ComparisonPriceAmount: null,
        PriceQualifier: null,
        DateSpecific: false,
        ApplicableDate: null,
        Verification: AssociateVerificationInfo.NotCheckedYet,
        IncludeOnPdf: true,
        DisplayPriceOverride: null);

    private static AssociateProduct PromotionProduct() => new(
        "catalog|promo|test",
        "Test Promotion",
        SourceCategory.Promotions,
        IsCustom: false,
        OfficialPriceAmount: 5m,
        OfficialDisplayPrice: null,
        ComparisonPriceAmount: null,
        PriceQualifier: null,
        DateSpecific: false,
        ApplicableDate: null,
        Verification: AssociateVerificationInfo.NotCheckedYet,
        IncludeOnPdf: true,
        DisplayPriceOverride: null);
}
