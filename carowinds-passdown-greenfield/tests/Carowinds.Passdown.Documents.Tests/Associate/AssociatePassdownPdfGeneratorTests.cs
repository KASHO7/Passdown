using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Documents.Tests.Associate;

public sealed class AssociatePassdownPdfGeneratorTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 21, 6, 8, 0, TimeSpan.Zero);

    // 26. No network/repository dependency: the generator is constructed with nothing but its own
    // default preflight service and generates purely from an in-memory model.
    private static readonly IAssociatePassdownPdfGenerator Generator = new AssociatePassdownPdfGenerator();

    private static AssociatePassdownDocumentModel BuildModel(AssociateWorkspace workspace) =>
        AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);

    // 1. Minimal valid Associate PDF generates.
    [Fact]
    public void MinimalWorkspace_Generates()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.MinimalValid());

        var result = Generator.Generate(model);

        Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
        Assert.NotNull(result.PdfBytes);
        Assert.NotEmpty(result.PdfBytes!);
    }

    // 2. Normal realistic draft generates one page. 23. Long but valid realistic content still fits one page (dense day).
    [Theory]
    [MemberData(nameof(OnePageFixtures))]
    public void RealisticWorkspaces_RenderExactlyOnePage(Func<AssociateWorkspace> fixture)
    {
        var model = BuildModel(fixture());

        var result = Generator.Generate(model);

        Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
        Assert.Equal(AssociatePassdownPreflightStatus.FitsOnePage, result.Preflight.Status);
        Assert.Equal(1, PdfInspector.CountPages(result.PdfBytes!));
    }

    public static IEnumerable<object[]> OnePageFixtures()
    {
        yield return [(Func<AssociateWorkspace>)AssociateWorkspaceFixtures.NormalDay];
        yield return [(Func<AssociateWorkspace>)AssociateWorkspaceFixtures.DenseDay];
        yield return [(Func<AssociateWorkspace>)AssociateWorkspaceFixtures.PrivateBuyoutDay];
        yield return [(Func<AssociateWorkspace>)AssociateWorkspaceFixtures.SparseDay];
        yield return [(Func<AssociateWorkspace>)AssociateWorkspaceFixtures.MinimalValid];
    }

    // Spec correction: the Associate Passdown never has a continuation page, however sections are
    // hidden — every hidden-section combination below must still render as exactly one real page.
    [Theory]
    [MemberData(nameof(HiddenSectionCombinationFixtures))]
    public void HiddenSectionCombinations_RenderExactlyOnePage(PassdownSectionSelection selection)
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        workspace = workspace with { Draft = workspace.Draft with { SectionSelection = selection } };

        var result = Generator.Generate(BuildModel(workspace));

        Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
        Assert.Equal(AssociatePassdownPreflightStatus.FitsOnePage, result.Preflight.Status);
        Assert.Equal(1, PdfInspector.CountPages(result.PdfBytes!));
    }

    public static IEnumerable<object[]> HiddenSectionCombinationFixtures()
    {
        yield return [new PassdownSectionSelection(false, false, false, false, false, false, false)]; // every optional section hidden.
        yield return [new PassdownSectionSelection(true, false, false, false, false, false, false)]; // only Daily Admission visible.
        yield return [new PassdownSectionSelection(false, true, false, false, false, false, false)]; // only Fast Lane visible.
        yield return [new PassdownSectionSelection(false, false, false, false, false, true, false)]; // only Operational Notes visible.
        yield return [PassdownSectionSelection.AllIncluded with { IncludePromotions = false }];
        yield return [PassdownSectionSelection.AllIncluded with { IncludeSeasonPasses = false, IncludeAddOns = false }];
        yield return [PassdownSectionSelection.AllIncluded with { IncludeHauntedAttractions = false }];
        yield return [PassdownSectionSelection.AllIncluded with { IncludeOperationalNotes = false }];
    }

    // Spec Phase 8B polish Part 1: the Key Selling tile grid adapts to how many products a category
    // has (1, 2, 3, and 4+ each use a different row arrangement — see AdaptiveGridLayout) rather than
    // a fixed tile width. Every arrangement must still generate successfully as one page, and every
    // product's name and price must still appear regardless of how many share the row.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    public void KeySellingSection_AnyProductCount_RendersSuccessfullyAsOnePage(int productCount)
    {
        var workspace = AssociateWorkspaceFixtures.MinimalValid() with
        {
            Products = KeySellingProducts(productCount),
        };

        var result = Generator.Generate(BuildModel(workspace));

        Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
        Assert.Equal(1, PdfInspector.CountPages(result.PdfBytes!));

        var text = PdfInspector.ExtractAllText(result.PdfBytes!);
        for (var i = 1; i <= productCount; i++)
        {
            Assert.Contains($"Ticket {i}", text);
            Assert.Contains($"${40 + i}.99", text);
        }
    }

    // Spec Phase 8B polish Part 2 (revised Phase 17D §8/§9/§14): every promotion renders as one
    // aligned row regardless of count — no comparison ("was") price and no raw ecommerce qualifier
    // ("Online only") print any more, only the current price.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PromotionalSection_AnyPromotionCount_RendersSuccessfullyAsOnePage(int promotionCount)
    {
        var workspace = AssociateWorkspaceFixtures.MinimalValid() with
        {
            Products = PromotionProducts(promotionCount),
        };

        var result = Generator.Generate(BuildModel(workspace));

        Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
        Assert.Equal(1, PdfInspector.CountPages(result.PdfBytes!));

        var text = PdfInspector.ExtractAllText(result.PdfBytes!);
        for (var i = 1; i <= promotionCount; i++)
        {
            Assert.Contains($"Promo {i}", text);
            Assert.Contains($"${20 + i}.00", text);
            Assert.DoesNotContain($"${60 + i}.00", text); // comparison ("was") price never prints.
        }

        Assert.DoesNotContain("Online only", text); // raw qualifier text never prints.
    }

    private static List<AssociateProduct> KeySellingProducts(int count) =>
        Enumerable.Range(1, count).Select(i => new AssociateProduct(
            $"dated|DatedAdmissionTickets|t{i}",
            $"Ticket {i}",
            SourceCategory.DatedAdmissionTickets,
            IsCustom: false,
            OfficialPriceAmount: 40m + i + 0.99m,
            OfficialDisplayPrice: null,
            ComparisonPriceAmount: null,
            PriceQualifier: null,
            DateSpecific: true,
            ApplicableDate: new DateOnly(2026, 8, 21),
            Verification: AssociateVerificationInfo.NotCheckedYet,
            IncludeOnPdf: true,
            DisplayPriceOverride: null)).ToList();

    private static List<AssociateProduct> PromotionProducts(int count) =>
        Enumerable.Range(1, count).Select(i => new AssociateProduct(
            $"catalog|promo|p{i}",
            $"Promo {i}",
            SourceCategory.Promotions,
            IsCustom: false,
            OfficialPriceAmount: 20m + i,
            OfficialDisplayPrice: null,
            ComparisonPriceAmount: 60m + i,
            PriceQualifier: "Online only",
            DateSpecific: false,
            ApplicableDate: null,
            Verification: AssociateVerificationInfo.NotCheckedYet,
            IncludeOnPdf: true,
            DisplayPriceOverride: null)).ToList();

    // Long but realistic Operational Notes (spec: a supervisor writing several full sentences, not a
    // pathological stress test) must still fit on one page.
    [Fact]
    public void LongButReasonableOperationalNotes_RenderExactlyOnePage()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay() with
        {
            Draft = AssociateWorkspaceFixtures.NormalDay().Draft with
            {
                OperationalNotes =
                [
                    "Fury 325 closed for scheduled maintenance until approximately 11 AM; direct guests to Intimidator or Copperhead Strike in the meantime.",
                    "Extra staffing scheduled at North Gate ahead of a school group of roughly 200 students arriving at 9:15 AM.",
                    "Guest Services has been relocated temporarily to the Carolina Harbor breezeway while the main building undergoes repairs.",
                    "Reminder: associates should direct all season pass processing questions to the front plaza tent today only.",
                ],
            },
        };

        var result = Generator.Generate(BuildModel(workspace));

        Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
        Assert.Equal(AssociatePassdownPreflightStatus.FitsOnePage, result.Preflight.Status);
        Assert.Equal(1, PdfInspector.CountPages(result.PdfBytes!));

        // These notes are long enough to visually wrap across two lines in the rendered panel.
        // PdfPig's naive text extraction does not insert a space at a QuestPDF-internal line-wrap
        // boundary (the two lines are separate glyph runs with no space glyph between them), so an
        // exact or whitespace-normalized substring match would spuriously fail even though every
        // word rendered correctly and in order. Stripping whitespace entirely from both sides
        // sidesteps that extraction quirk while still proving no word/character was dropped.
        var text = RemoveWhitespace(PdfInspector.ExtractAllText(result.PdfBytes!));
        foreach (var note in workspace.Draft.OperationalNotes)
        {
            Assert.Contains(RemoveWhitespace(note), text);
        }
    }

    private static string RemoveWhitespace(string value) => new(value.Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static AssociateOperationalNoteSelection Selection(string title, string? body = null, OperationalNotePriority priority = OperationalNotePriority.Normal) =>
        new(Guid.NewGuid(), title, body, OperationalNoteCategory.Equipment, priority, new DateOnly(2026, 8, 20), null, DateTimeOffset.UtcNow);

    // Phase 11D Part AW/AC: selected persistent notes render alongside manual notes, still within the
    // existing one-page overflow policy — 0/1/3/many selections, long titles/bodies, and manual notes
    // + selected notes together.
    [Fact]
    public void ZeroSelectedOperationalNotes_BehavesLikePre11D()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        var withoutSelections = BuildModel(workspace);

        Assert.Equal(workspace.Draft.OperationalNotes.Count, withoutSelections.OperationalNotes.Count);
    }

    [Fact]
    public void OneSelectedOperationalNote_AppearsOnThePage()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        workspace = workspace with { Draft = workspace.Draft with { SelectedOperationalNotes = [Selection("South Gate scanner #3 out of service")] } };

        var result = Generator.Generate(BuildModel(workspace));

        Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
        Assert.Equal(1, PdfInspector.CountPages(result.PdfBytes!));
        Assert.Contains("South Gate scanner #3 out of service", PdfInspector.ExtractAllText(result.PdfBytes!));
    }

    [Fact]
    public void MultipleSelectedOperationalNotes_AllAppearOnThePage()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        var selections = new[]
        {
            Selection("South Gate scanner #3 out of service"),
            Selection("Group check-in support at North Gate"),
            Selection("Entrance scanner maintenance", priority: OperationalNotePriority.Important),
        };
        workspace = workspace with { Draft = workspace.Draft with { SelectedOperationalNotes = selections } };

        var result = Generator.Generate(BuildModel(workspace));

        Assert.Equal(1, PdfInspector.CountPages(result.PdfBytes!));
        var text = PdfInspector.ExtractAllText(result.PdfBytes!);
        foreach (var selection in selections)
        {
            Assert.Contains(selection.Title, text);
        }
    }

    [Fact]
    public void ManualNotesAndSelectedOperationalNotes_BothAppearTogether()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        workspace = workspace with { Draft = workspace.Draft with { SelectedOperationalNotes = [Selection("South Gate scanner #3 out of service")] } };
        var model = BuildModel(workspace);

        var result = Generator.Generate(model);
        var text = PdfInspector.ExtractAllText(result.PdfBytes!);

        foreach (var manualNote in workspace.Draft.OperationalNotes)
        {
            Assert.Contains(manualNote, text);
        }
        Assert.Contains("South Gate scanner #3 out of service", text);
    }

    [Fact]
    public void RemovedSelection_NoLongerAppearsInRegeneratedPdf()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        var selected = workspace with { Draft = workspace.Draft with { SelectedOperationalNotes = [Selection("South Gate scanner #3 out of service")] } };
        var deselected = workspace with { Draft = workspace.Draft with { SelectedOperationalNotes = [] } };

        var withSelection = Generator.Generate(BuildModel(selected));
        var withoutSelection = Generator.Generate(BuildModel(deselected));

        Assert.Contains("South Gate scanner #3 out of service", PdfInspector.ExtractAllText(withSelection.PdfBytes!));
        Assert.DoesNotContain("South Gate scanner #3 out of service", PdfInspector.ExtractAllText(withoutSelection.PdfBytes!));
    }

    [Fact]
    public void SelectedOperationalNote_NeverExposesSourceIdInPdf()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        var selection = Selection("South Gate scanner #3 out of service");
        workspace = workspace with { Draft = workspace.Draft with { SelectedOperationalNotes = [selection] } };

        var result = Generator.Generate(BuildModel(workspace));

        Assert.DoesNotContain(selection.SourceOperationalNoteId.ToString(), PdfInspector.ExtractAllText(result.PdfBytes!));
    }

    // Spec correction: a realistic heavy day still renders every included product and note in full
    // on the single page — nothing is silently dropped or truncated to force a one-page fit.
    [Fact]
    public void DenseDay_RendersEveryProductAndNote_OnTheSinglePage()
    {
        var workspace = AssociateWorkspaceFixtures.DenseDay();
        var model = BuildModel(workspace);

        var result = Generator.Generate(model);

        Assert.Equal(1, PdfInspector.CountPages(result.PdfBytes!));

        var text = PdfInspector.ExtractAllText(result.PdfBytes!);
        foreach (var category in model.ProductCategories)
        {
            foreach (var product in category.Products)
            {
                Assert.Contains(product.DisplayName, text);
            }
        }

        foreach (var note in model.OperationalNotes)
        {
            Assert.Contains(note, text);
        }
    }

    // 3. Operating date appears — the primary page heading (spec Phase 17D §7/§11), printed in
    // upper case, so this is matched case-insensitively rather than against the mixed-case model text.
    [Fact]
    public void OperatingDate_Appears()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.NormalDay());
        var text = GenerateText(model);

        Assert.Contains(model.OperatingDateText, text, StringComparison.OrdinalIgnoreCase);
    }

    // 4. Effective park hours appear.
    [Fact]
    public void EffectiveParkHours_Appear()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.NormalDay());
        var text = GenerateText(model);

        Assert.Contains(model.ParkHours.HeadlineText, text);
    }

    // 5. North Gate / Avenue appear.
    [Fact]
    public void NorthGateAndAvenue_Appear()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.NormalDay());
        var text = GenerateText(model);

        Assert.Contains("North Gate", text);
        Assert.Contains("Avenue", text);
        Assert.Contains(model.ParkHours.NorthGateText!, text);
        Assert.Contains(model.ParkHours.AvenueText!, text);
    }

    // 6. Projected attendance appears.
    [Fact]
    public void ProjectedAttendance_Appears()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.NormalDay());
        var text = GenerateText(model);

        Assert.Contains("18,500", text);
    }

    // 7. Pre-solds appear.
    [Fact]
    public void PreSolds_Appear()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.NormalDay());
        var text = GenerateText(model);

        Assert.Contains("4,200", text);
    }

    // 8. Early Ride Time content appears when configured — rides only (Phase 17D §10: no start time,
    // no note/reason line). NormalDay's own North Gate opening coincidentally formats identically to
    // its ERT start time ("8:30 AM"), so absence-of-time is proven separately, with a deliberately
    // non-colliding time, by EarlyRideTimeLine_ShowsRidesOnly_IndependentOfNorthGate_NeverAStartTime.
    [Fact]
    public void EarlyRideTimeDetails_AppearWhenConfigured()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.NormalDay());
        var text = GenerateText(model);

        Assert.Equal(EffectiveErtKind.Configured, model.EarlyRideTime.Kind);
        Assert.Contains("Fury 325", text);
        Assert.Contains("Intimidator", text);
        Assert.Contains("Copperhead Strike", text);
    }

    // Phase 17B spec Part 31, revised Phase 17D §10: ERT is a monthly/override configuration, never
    // derived from park hours, and never prints a start time at all — the rendered Early Ride line
    // shows only rides, independent of the Park Hours box's "North Gate" value.
    [Fact]
    public void EarlyRideTimeLine_ShowsRidesOnly_IndependentOfNorthGate_NeverAStartTime()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay() with
        {
            EarlyRideTime = new EffectiveErt(EffectiveErtKind.Configured, EffectiveErtSource.Monthly, new TimeOnly(7, 45), ["Fury 325"], null),
        };
        var model = BuildModel(workspace);
        var text = GenerateText(model);

        Assert.NotNull(model.ParkHours.NorthGateText);
        Assert.Contains("Fury 325", text);
        Assert.DoesNotContain("7:45 AM", text);
        Assert.DoesNotContain("Start", text);
    }

    // Phase 17B spec Part 31/32 (park-hours independence regression): a Closed park day never
    // changes or erases the resolved ERT — rides still print exactly as configured, never a time.
    [Fact]
    public void EarlyRideTimeBox_ClosedParkDay_ErtUnaffected()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay() with
        {
            ParkHours = AssociateWorkspaceFixtures.NormalDay().ParkHours with
            {
                Effective = ParkHoursCalculator.Calculate(true, null, null, false, null, null, false, null, null),
            },
        };
        var model = BuildModel(workspace);
        var text = GenerateText(model);

        Assert.Equal(EffectiveErtKind.Configured, model.EarlyRideTime.Kind);
        Assert.Equal(PdfParkHoursStatus.Closed, model.ParkHours.Status);
        Assert.Contains("Fury 325", text); // rides preserved, never silently erased.
        Assert.DoesNotContain("8:30 AM", text); // never a start time, independent of park status.
        Assert.Contains("park is closed", text, StringComparison.OrdinalIgnoreCase);
    }

    // 9. Participating attractions appear.
    [Fact]
    public void ParticipatingAttractions_Appear()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.NormalDay());
        var text = GenerateText(model);

        Assert.Contains("Fury 325", text);
        Assert.Contains("Intimidator", text);
        Assert.Contains("Copperhead Strike", text);
    }

    // 10. Daily Feature appears, headed by the selected type itself.
    [Fact]
    public void DailyFeature_Appears()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.NormalDay());
        var text = GenerateText(model);

        Assert.Contains("QUESTION OF THE DAY", text);
        Assert.Contains("What year did Carowinds open?", text);
    }

    // 11. Operational notes appear.
    [Fact]
    public void OperationalNotes_Appear()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.NormalDay());
        var text = GenerateText(model);

        Assert.Contains("Fury 325 closed for scheduled maintenance until 11 AM.", text);
        Assert.Contains("Guest Services relocated temporarily to the Carolina Harbor breezeway.", text);
    }

    // 12. Excluded official product does not appear. 13. Included official product appears.
    [Fact]
    public void IncludeOnPdf_ControlsWhetherAnOfficialProductPrints()
    {
        var workspace = AssociateWorkspaceFixtures.SparseDay();
        var included = workspace.Products[0] with { DisplayName = "Included Ticket", IncludeOnPdf = true };
        var excluded = workspace.Products[0] with { ProductIdentity = "dated|DatedAdmissionTickets|excluded", DisplayName = "Excluded Ticket", IncludeOnPdf = false };
        workspace = workspace with { Products = [included, excluded] };

        var text = GenerateText(BuildModel(workspace));

        Assert.Contains("Included Ticket", text);
        Assert.DoesNotContain("Excluded Ticket", text);
    }

    // 14. Official price appears when no override exists.
    [Fact]
    public void OfficialPrice_AppearsWithoutOverride()
    {
        var workspace = AssociateWorkspaceFixtures.SparseDay();
        var product = workspace.Products[0] with { OfficialPriceAmount = 54.99m, DisplayPriceOverride = null };
        workspace = workspace with { Products = [product] };

        var text = GenerateText(BuildModel(workspace));

        Assert.Contains("$54.99", text);
    }

    // 15. Display-price override replaces effective displayed price.
    [Fact]
    public void DisplayPriceOverride_ReplacesEffectivePrice()
    {
        var workspace = AssociateWorkspaceFixtures.SparseDay();
        var product = workspace.Products[0] with { OfficialPriceAmount = 54.99m, DisplayPriceOverride = 49.99m };
        workspace = workspace with { Products = [product] };

        var text = GenerateText(BuildModel(workspace));

        Assert.Contains("$49.99", text);
        Assert.DoesNotContain("$54.99", text);
    }

    // 16. Comparison price never prints, source-backed or not (Phase 17D §8).
    [Fact]
    public void ComparisonPrice_NeverAppears()
    {
        var workspace = AssociateWorkspaceFixtures.SparseDay();
        var withComparison = workspace.Products[0] with { OfficialPriceAmount = 54.99m, ComparisonPriceAmount = 69.99m };
        workspace = workspace with { Products = [withComparison] };

        var text = GenerateText(BuildModel(workspace));

        Assert.Contains("$54.99", text);
        Assert.DoesNotContain("$69.99", text);
    }

    // 17. Raw ecommerce qualifier text never prints (Phase 17D §9) — only the "From $x" price prefix
    // survives for a Starting-From-classified price.
    [Fact]
    public void RawQualifier_NeverAppears()
    {
        var workspace = AssociateWorkspaceFixtures.SparseDay();
        var withQualifier = workspace.Products[0] with { PriceQualifier = "Through Labor Day" };
        workspace = workspace with { Products = [withQualifier] };

        var text = GenerateText(BuildModel(workspace));

        Assert.DoesNotContain("Through Labor Day", text);
    }

    // Phase 17D §9: a "Starting From"-classified price prints as "From $x" in the price column
    // rather than a separate qualifier line.
    [Fact]
    public void StartingFromPrice_PrintsFromPricePrefix()
    {
        var workspace = AssociateWorkspaceFixtures.SparseDay();
        var startingFrom = workspace.Products[0] with
        {
            OfficialPriceAmount = 37.00m,
            PriceQualifier = "Starting From",
            PriceKind = PriceKind.StartingFrom,
        };
        workspace = workspace with { Products = [startingFrom] };

        var text = GenerateText(BuildModel(workspace));

        Assert.Contains("From $37.00", text);
        Assert.DoesNotContain("Starting From", text);
    }

    // 18. Custom included product appears. 20. Custom product Notes appear appropriately.
    [Fact]
    public void IncludedCustomProduct_AppearsWithNotes()
    {
        var workspace = AssociateWorkspaceFixtures.MinimalValid() with
        {
            Draft = AssociateDraftPayload.CreateDefault() with
            {
                CustomProducts = [new CustomProductEntry("c1", "Staff Meal Voucher", 8.00m, "Cafeteria only.", true)],
            },
        };

        var text = GenerateText(BuildModel(workspace));

        Assert.Contains("Staff Meal Voucher", text);
        Assert.Contains("Cafeteria only.", text);
        Assert.Contains("$8.00", text);
    }

    // 19. Excluded custom product does not appear.
    [Fact]
    public void ExcludedCustomProduct_DoesNotAppear()
    {
        var workspace = AssociateWorkspaceFixtures.MinimalValid() with
        {
            Draft = AssociateDraftPayload.CreateDefault() with
            {
                CustomProducts = [new CustomProductEntry("c1", "Hidden Internal Item", null, null, false)],
            },
        };

        var text = GenerateText(BuildModel(workspace));

        Assert.DoesNotContain("Hidden Internal Item", text);
    }

    // 21. Technical/internal ids do not appear.
    [Fact]
    public void TechnicalIdentifiers_NeverAppear()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.NormalDay());
        var text = GenerateText(model);

        Assert.DoesNotContain("dated|", text);
        Assert.DoesNotContain("catalog|", text);
        Assert.DoesNotContain("custom|", text);
        Assert.DoesNotContain("DatedAdmissionTickets", text);
        Assert.DoesNotContain("DatedFastLane", text);
        Assert.DoesNotContain("CatalogProductKey", text);
        Assert.DoesNotContain("SourceCategory", text);
        Assert.DoesNotContain("RetrievalStrategy", text);
    }

    // 22. Empty optional sections do not create ugly blank blocks.
    [Fact]
    public void EmptyOptionalSections_DoNotPrint()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.MinimalValid());
        var text = GenerateText(model);

        Assert.DoesNotContain("OPERATIONAL NOTES", text);
        Assert.DoesNotContain("PRODUCTS", text);
    }

    // Spec correction: the Associate Passdown never has a second page. Content that would not fit on
    // one page is refused by preflight — with a useful, actionable reason — rather than spilling to
    // a continuation page.
    [Fact]
    public void ExcessiveContent_StillGenerates_ButReportsMoreThanOnePage()
    {
        // Phase 13D Part AA/AB: excessive content is no longer refused — it renders onto real
        // additional pages, and the authoritative page count (never the preflight estimate) is what
        // a caller must use to warn the associate.
        var workspace = AssociateWorkspaceFixtures.ExcessiveDay();
        var model = BuildModel(workspace);

        var result = Generator.Generate(model);

        Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
        Assert.NotNull(result.PdfBytes);
        Assert.True(result.PageCount > 1);
        Assert.Equal(result.PageCount, PdfInspector.CountPages(result.PdfBytes!));
    }

    // Normal-sized content always renders as exactly one physical page (asserted throughout this file
    // via PdfInspector.CountPages); a too-large model still generates, but PageCount reports the real
    // overflow rather than silently staying at 1 (Phase 13D Part AA/AB).
    [Fact]
    public void SuccessfulGeneration_NormalContentIsExactlyOnePage_ExcessiveContentReportsRealOverflow()
    {
        foreach (var fixture in new Func<AssociateWorkspace>[]
        {
            AssociateWorkspaceFixtures.MinimalValid,
            AssociateWorkspaceFixtures.SparseDay,
            AssociateWorkspaceFixtures.NormalDay,
            AssociateWorkspaceFixtures.DenseDay,
            AssociateWorkspaceFixtures.PrivateBuyoutDay,
        })
        {
            var result = Generator.Generate(BuildModel(fixture()));

            Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
            Assert.Equal(1, result.PageCount);
            Assert.Equal(1, PdfInspector.CountPages(result.PdfBytes!));
        }

        var excessiveResult = Generator.Generate(BuildModel(AssociateWorkspaceFixtures.ExcessiveDay()));
        Assert.Equal(AssociatePassdownPdfStatus.Generated, excessiveResult.Status);
        Assert.NotNull(excessiveResult.PdfBytes);
        Assert.True(excessiveResult.PageCount > 1);
    }

    // Phase 13D Part Y: retrieval-verification metadata ("Last verified", "Not verified", staleness
    // wording) never appears on the printed Associate Passdown, even for a day whose data is stale —
    // that diagnostic signal stays in-app only (Dashboard Data Status, Associate's on-screen Park
    // Hours panel, the Operations Calendar).
    [Fact]
    public void StaleDataDay_NeverPrintsVerificationMetadata()
    {
        var result = Generator.Generate(BuildModel(AssociateWorkspaceFixtures.StaleDataDay()));

        Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
        var text = PdfInspector.ExtractAllText(result.PdfBytes!);

        Assert.DoesNotContain("Last verified", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Not verified", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("may be out of date", text, StringComparison.OrdinalIgnoreCase);
    }

    // 25. PDF begins with valid PDF bytes.
    [Fact]
    public void GeneratedBytes_BeginWithPdfMagicNumber()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.NormalDay());

        var result = Generator.Generate(model);

        Assert.NotNull(result.PdfBytes);
        Assert.Equal("%PDF"u8.ToArray(), result.PdfBytes!.Take(4).ToArray());
    }

    // Preflight: missing operating date fails generation clearly rather than producing misleading output.
    [Fact]
    public void MissingOperatingDate_FailsGenerationWithoutBytes()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.MinimalValid()) with { OperatingDate = null };

        var result = Generator.Generate(model);

        Assert.Equal(AssociatePassdownPdfStatus.ValidationFailed, result.Status);
        Assert.Null(result.PdfBytes);
        Assert.Equal(AssociatePassdownPreflightStatus.MissingOperatingDate, result.Preflight.Status);
    }

    // Phase 8A: an excluded optional section's category heading and products never appear, and
    // consume no space — nothing prints where the hidden category would have been.
    [Fact]
    public void ExcludedOptionalSection_DoesNotAppearInGeneratedPdf()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        workspace = workspace with
        {
            Draft = workspace.Draft with { SectionSelection = PassdownSectionSelection.AllIncluded with { IncludeFastLane = false } },
        };

        var text = GenerateText(BuildModel(workspace));

        Assert.DoesNotContain("Fast Lane", text);
        Assert.Contains("DAILY ADMISSION", text); // category heading; every other included optional section is unaffected.
    }

    // Phase 8B: correcting the Phase 8A omission — hiding SCarowinds/Haunt removes it from the
    // generated PDF and consumes no space, matching every other optional section.
    [Fact]
    public void ExcludedSCarowinds_DoesNotAppearInGeneratedPdf()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        var hauntedProduct = workspace.Products[0] with
        {
            ProductIdentity = "dated|HauntedAttractions|scaretest",
            DisplayName = "SCarowinds Haunted House",
            Category = SourceCategory.HauntedAttractions,
        };
        workspace = workspace with { Products = [.. workspace.Products, hauntedProduct] };

        var shownText = GenerateText(BuildModel(workspace with
        {
            Draft = workspace.Draft with { SectionSelection = PassdownSectionSelection.AllIncluded },
        }));
        Assert.Contains("SCarowinds Haunted House", shownText);

        var hiddenText = GenerateText(BuildModel(workspace with
        {
            Draft = workspace.Draft with { SectionSelection = PassdownSectionSelection.AllIncluded with { IncludeHauntedAttractions = false } },
        }));
        Assert.DoesNotContain("SCarowinds Haunted House", hiddenText);
    }

    // Phase 8A: excluding Operational Notes means the section prints nothing at all, not an empty box.
    [Fact]
    public void ExcludedOperationalNotes_DoesNotAppearInGeneratedPdf()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        workspace = workspace with
        {
            Draft = workspace.Draft with { SectionSelection = PassdownSectionSelection.AllIncluded with { IncludeOperationalNotes = false } },
        };

        var text = GenerateText(BuildModel(workspace));

        Assert.DoesNotContain("OPERATIONAL NOTES", text);
        Assert.DoesNotContain("Fury 325 closed for scheduled maintenance", text);
    }

    // Phase 8A: mandatory content always renders regardless of what SectionSelection says — there is
    // no flag that can hide it in the first place.
    [Fact]
    public void MandatoryContent_AlwaysAppears_EvenWithEveryOptionalSectionExcluded()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        workspace = workspace with
        {
            Draft = workspace.Draft with
            {
                SectionSelection = new PassdownSectionSelection(false, false, false, false, false, false, false),
            },
        };

        var model = BuildModel(workspace);
        var text = GenerateText(model);

        Assert.Contains(model.OperatingDateText, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(model.ParkHours.HeadlineText, text);
        Assert.Contains("18,500", text); // attendance
        Assert.Contains("Fury 325", text); // Early Ride Time rides
        Assert.Contains("QUESTION OF THE DAY", text); // Daily Feature
    }

    private static string GenerateText(AssociatePassdownDocumentModel model)
    {
        var result = Generator.Generate(model);
        Assert.NotNull(result.PdfBytes);
        return PdfInspector.ExtractAllText(result.PdfBytes!);
    }
}
