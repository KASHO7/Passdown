using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Ert;

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

    private static AssociateOperationalNoteSelection ScannerSelection(OperationalNotePriority priority = OperationalNotePriority.Normal, string? body = "Direct guests to gate 2.") =>
        new(Guid.NewGuid(), "South Gate scanner #3 out of service", body, OperationalNoteCategory.Equipment, priority,
            new DateOnly(2026, 8, 20), null, new DateTimeOffset(2026, 8, 21, 6, 0, 0, TimeSpan.Zero));

    // Phase 11D Part AB/L: selected persistent notes are combined with the manual daily notes into
    // the same flat AssociatePassdownDocumentModel.OperationalNotes list (no new document-model or
    // snapshot field — see docs/phase11-operational-notes.md's Phase 11D schema decision), manual
    // notes first, then selected persistent notes in selection order.
    [Fact]
    public void SelectedOperationalNotes_AppendedAfterManualNotes_WithPersistentPrefix()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        var manualCount = workspace.Draft.OperationalNotes.Count;
        workspace = workspace with { Draft = workspace.Draft with { SelectedOperationalNotes = [ScannerSelection()] } };

        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);

        Assert.Equal(manualCount + 1, model.OperationalNotes.Count);
        Assert.Equal(workspace.Draft.OperationalNotes, model.OperationalNotes.Take(manualCount));
        Assert.Equal("Persistent: South Gate scanner #3 out of service — Direct guests to gate 2.", model.OperationalNotes[manualCount]);
    }

    [Fact]
    public void SelectedOperationalNotes_ImportantPriority_PrefixedWithImportant()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        workspace = workspace with
        {
            Draft = workspace.Draft with { SelectedOperationalNotes = [ScannerSelection(OperationalNotePriority.Important, body: null)] },
        };

        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);

        Assert.Equal("Persistent (Important): South Gate scanner #3 out of service", model.OperationalNotes[^1]);
    }

    [Fact]
    public void SelectedOperationalNotes_NoManualNotesButOneSelected_StillAppearsInPanel()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        workspace = workspace with
        {
            Draft = workspace.Draft with { OperationalNotes = [], SelectedOperationalNotes = [ScannerSelection()] },
        };

        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);

        Assert.Single(model.OperationalNotes);
    }

    [Fact]
    public void SelectedOperationalNotes_HiddenWithOperationalNotesSection()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        workspace = workspace with
        {
            Draft = workspace.Draft with
            {
                SelectedOperationalNotes = [ScannerSelection()],
                SectionSelection = PassdownSectionSelection.AllIncluded with { IncludeOperationalNotes = false },
            },
        };

        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);

        Assert.Empty(model.OperationalNotes);
    }

    // Part AB: no internal id/timestamp/status ever reaches the PDF/snapshot display content.
    [Fact]
    public void SelectedOperationalNotes_NeverExposesSourceIdOrTimestamps()
    {
        var selection = ScannerSelection();
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        workspace = workspace with { Draft = workspace.Draft with { OperationalNotes = [], SelectedOperationalNotes = [selection] } };

        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);

        var line = Assert.Single(model.OperationalNotes);
        Assert.DoesNotContain(selection.SourceOperationalNoteId.ToString(), line);
        Assert.DoesNotContain(selection.SelectedAtUtc.Year.ToString(), line);
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
        Assert.Equal(EffectiveErtKind.Configured, model.EarlyRideTime.Kind);
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

    // Phase 18D: "The Conjuring: Beyond Fear" is a HauntedAttractions product (official keyword
    // THECONJURING); it prints under SCarowinds only when SCarowinds / Haunt is selected, and is
    // never moved into Add-Ons whether or not Add-Ons is selected.
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Phase18D_Conjuring_PrintsUnderSCarowindsOnlyWhenThatSectionIsSelected(bool includeHaunted, bool includeAddOns)
    {
        var date = new DateOnly(2026, 10, 17);
        var conjuring = new AssociateProduct(
            AssociateProductIdentity.ForDatedProduct(SourceCategory.HauntedAttractions, "10098308"),
            "The Conjuring: Beyond Fear",
            SourceCategory.HauntedAttractions,
            IsCustom: false,
            OfficialPriceAmount: 30m,
            OfficialDisplayPrice: null,
            ComparisonPriceAmount: null,
            PriceQualifier: null,
            DateSpecific: true,
            ApplicableDate: date,
            Verification: AssociateVerificationInfo.NotCheckedYet,
            IncludeOnPdf: true,
            DisplayPriceOverride: null);
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        workspace = workspace with
        {
            Products = [.. workspace.Products, conjuring],
            Draft = workspace.Draft with
            {
                SectionSelection = PassdownSectionSelection.AllIncluded with { IncludeHauntedAttractions = includeHaunted, IncludeAddOns = includeAddOns },
            },
        };

        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);

        var printed = model.ProductCategories.Where(c => c.Products.Any(p => p.DisplayName == "The Conjuring: Beyond Fear")).ToList();
        if (includeHaunted)
        {
            Assert.Equal("SCarowinds", Assert.Single(printed).CategoryName);
        }
        else
        {
            Assert.Empty(printed);
        }
    }
}
