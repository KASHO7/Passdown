using System.Globalization;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Documents.Tests.Associate;

/// <summary>
/// Phase 18F: <see cref="AssociateTeamsTextFormatter"/>. Every case builds the real
/// <see cref="AssociatePassdownDocumentModelBuilder"/> model from a workspace — the same path the PDF
/// takes — so these prove copied text follows the PDF's own inclusion rules rather than a parallel
/// reinterpretation of catalog state.
/// </summary>
public sealed class AssociateTeamsTextFormatterTests
{
    private static readonly DateOnly Date = new(2026, 9, 19);
    private static readonly DateTimeOffset GeneratedAt = new(2026, 9, 19, 6, 0, 0, TimeSpan.Zero);
    private static readonly AssociateVerificationInfo Verified = new(AssociateVerificationState.Verified, GeneratedAt, null, null);

    private static readonly PassdownSectionSelection NoneSelected = new(false, false, false, false, false, false, false);

    private static AssociateProduct Product(
        string name,
        SourceCategory category,
        decimal? price,
        PriceKind kind = PriceKind.ExactDated,
        bool include = true,
        decimal? comparison = null,
        string? qualifier = null,
        decimal? overridePrice = null) => new(
        $"test|{category}|{name}",
        name,
        category,
        IsCustom: false,
        price,
        OfficialDisplayPrice: null,
        comparison,
        qualifier,
        DateSpecific: kind == PriceKind.ExactDated,
        ApplicableDate: Date,
        Verified,
        include,
        overridePrice,
        kind);

    /// <summary>A product for every section, all included — a test narrows it by section selection or per-product deselection.</summary>
    private static List<AssociateProduct> EveryProduct() =>
    [
        Product("Park Ticket", SourceCategory.DatedAdmissionTickets, 59m),
        Product("Military/First Responder", SourceCategory.MilitaryFirstResponder, 37m, PriceKind.StartingFrom, comparison: 80m, qualifier: "Only"),
        Product("Fast Lane Reserve", SourceCategory.DatedFastLane, 65m),
        Product("Fast Lane Ultimate", SourceCategory.DatedFastLane, 109m),
        Product("Bring-A-Friend Ticket", SourceCategory.Promotions, 55m, comparison: 70m, qualifier: "Sale Price"),
        Product("Gold Season Pass", SourceCategory.SeasonPasses, 129m),
        Product("SCarowinds Ticket", SourceCategory.HauntedAttractions, 45m),
        Product("The Conjuring: Beyond Fear", SourceCategory.HauntedAttractions, 30m),
        Product("All Season Dining", SourceCategory.AddOns, 160m),
    ];

    private static AssociateWorkspace Workspace(
        PassdownSectionSelection selection,
        IReadOnlyList<AssociateProduct>? products = null,
        EffectiveHours? hours = null,
        int? projected = 18000,
        int? preSolds = 1000,
        EffectiveErt? ert = null,
        IReadOnlyList<CustomProductEntry>? custom = null)
    {
        var effective = hours ?? ParkHoursCalculator.Calculate(false, new TimeOnly(11, 0), new TimeOnly(0, 0), false, null, null, false, null, null);
        var draft = AssociateDraftPayload.CreateDefault() with
        {
            SectionSelection = selection,
            ProjectedAttendance = projected,
            PreSolds = preSolds,
            DailyFeatureType = DailyFeatureType.QuoteOfTheDay,
            DailyFeatureText = "Not for Teams.",
            OperationalNotes = ["Not for Teams either."],
            CustomProducts = custom ?? [],
        };

        return new AssociateWorkspace(
            Date, draft,
            new AssociateParkHoursView(effective, Verified, effective.Status == EffectiveHoursStatus.Closed, new TimeOnly(11, 0), new TimeOnly(0, 0)),
            products ?? EveryProduct(), [], AssociateSaveStatus.NeverSaved,
            EarlyRideTime: ert ?? new EffectiveErt(EffectiveErtKind.Configured, EffectiveErtSource.Monthly, new TimeOnly(10, 30), ["Thunder Striker", "Skytower"], null));
    }

    private static AssociatePassdownDocumentModel Model(AssociateWorkspace workspace) => AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);

    private static string? Pricing(AssociateWorkspace workspace) => AssociateTeamsTextFormatter.FormatPricing(Model(workspace));

    private static string Operations(AssociateWorkspace workspace) => AssociateTeamsTextFormatter.FormatOperationsSummary(Model(workspace));

    private static PassdownSectionSelection Only(
        bool dailyAdmission = false, bool fastLane = false, bool promotions = false,
        bool seasonPasses = false, bool addOns = false, bool scarowinds = false) =>
        new(dailyAdmission, fastLane, promotions, seasonPasses, addOns, IncludeOperationalNotes: true, scarowinds);

    // ---------------------------------------------------------------- Today's Pricing

    [Fact]
    public void Pricing_OnlyDailyAdmission()
    {
        var text = Pricing(Workspace(Only(dailyAdmission: true)));

        Assert.Equal(
            "Ticket Prices:\r\n\r\nDaily Admission\r\nPark Ticket: $59.00\r\nMilitary/First Responder: From $37.00",
            text);
    }

    [Fact]
    public void Pricing_OnlyFastLane()
    {
        var text = Pricing(Workspace(Only(fastLane: true)));

        Assert.Equal("Ticket Prices:\r\n\r\nFast Lane\r\nFast Lane Reserve: $65.00\r\nFast Lane Ultimate: $109.00", text);
    }

    [Fact]
    public void Pricing_DailyAdmissionAndFastLane_InPdfOrder_BlankLineBetweenSections()
    {
        var text = Pricing(Workspace(Only(dailyAdmission: true, fastLane: true)))!;

        Assert.Equal(
            "Ticket Prices:\r\n\r\nDaily Admission\r\nPark Ticket: $59.00\r\nMilitary/First Responder: From $37.00\r\n\r\nFast Lane\r\nFast Lane Reserve: $65.00\r\nFast Lane Ultimate: $109.00",
            text);
    }

    [Fact]
    public void Pricing_PromotionsSelected_UsesCurrentPriceOnly()
    {
        var text = Pricing(Workspace(Only(promotions: true)))!;

        Assert.Equal("Ticket Prices:\r\n\r\nPromotions\r\nBring-A-Friend Ticket: $55.00", text);
    }

    [Fact]
    public void Pricing_NeverCarriesComparisonOrQualifierWording()
    {
        var text = Pricing(Workspace(PassdownSectionSelection.AllIncluded))!;

        Assert.DoesNotContain("$80", text);
        Assert.DoesNotContain("$70", text);
        foreach (var banned in new[] { "Was", "Only", "Online Price", "Sale Price", "ExactDated", "StartingFrom" })
        {
            Assert.DoesNotContain(banned, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Pricing_SeasonPassesDeselected_Omitted()
    {
        var text = Pricing(Workspace(PassdownSectionSelection.AllIncluded with { IncludeSeasonPasses = false }))!;

        Assert.DoesNotContain("Season Pass", text);
        Assert.Contains("Park Ticket", text);
    }

    [Fact]
    public void Pricing_ScarowindsDeselected_OmitsEveryHauntProductIncludingConjuring()
    {
        var text = Pricing(Workspace(PassdownSectionSelection.AllIncluded with { IncludeHauntedAttractions = false }))!;

        Assert.DoesNotContain("SCarowinds", text);
        Assert.DoesNotContain("Conjuring", text);
    }

    [Fact]
    public void Pricing_ScarowindsSelected_IncludesConjuringLikeAnyOtherHauntProduct()
    {
        var text = Pricing(Workspace(Only(scarowinds: true)));

        Assert.Equal(
            "Ticket Prices:\r\n\r\nSCarowinds\r\nSCarowinds Ticket: $45.00\r\nThe Conjuring: Beyond Fear: $30.00",
            text);
    }

    [Fact]
    public void Pricing_AddOnsSelected_Included()
    {
        Assert.Equal("Ticket Prices:\r\n\r\nAdd-Ons\r\nAll Season Dining: $160.00", Pricing(Workspace(Only(addOns: true))));
    }

    [Fact]
    public void Pricing_AddOnsDeselected_Omitted()
    {
        var text = Pricing(Workspace(PassdownSectionSelection.AllIncluded with { IncludeAddOns = false }))!;

        Assert.DoesNotContain("Add-Ons", text);
        Assert.DoesNotContain("All Season Dining", text);
    }

    [Fact]
    public void Pricing_IndividualProductDeselected_RemovedFromPdfModelAndClipboard_CatalogUntouched()
    {
        var products = EveryProduct()
            .Select(p => p.DisplayName == "Fast Lane Ultimate" ? p with { IncludeOnPdf = false } : p)
            .ToList();
        var workspace = Workspace(Only(fastLane: true), products);
        var model = Model(workspace);

        var pdfRows = model.ProductCategories.SelectMany(c => c.Products).Select(r => r.DisplayName).ToList();
        Assert.Equal(["Fast Lane Reserve"], pdfRows);
        Assert.Equal("Ticket Prices:\r\n\r\nFast Lane\r\nFast Lane Reserve: $65.00", AssociateTeamsTextFormatter.FormatPricing(model));

        // The workspace's own product list (the loaded catalog) still has the product.
        Assert.Contains(workspace.Products, p => p.DisplayName == "Fast Lane Ultimate");
    }

    [Fact]
    public void Pricing_FromPrice_KeepsFromPrefix()
    {
        Assert.Contains("Military/First Responder: From $37.00", Pricing(Workspace(Only(dailyAdmission: true))));
    }

    [Fact]
    public void Pricing_ExactPrice_HasNoFromPrefix()
    {
        Assert.Contains("Park Ticket: $59.00", Pricing(Workspace(Only(dailyAdmission: true))));
        Assert.DoesNotContain("Park Ticket: From", Pricing(Workspace(Only(dailyAdmission: true))));
    }

    [Fact]
    public void Pricing_DisplayPriceOverride_IsTheCopiedPrice()
    {
        var products = EveryProduct()
            .Select(p => p.DisplayName == "Park Ticket" ? p with { DisplayPriceOverride = 49m } : p)
            .ToList();

        Assert.Contains("Park Ticket: $49.00", Pricing(Workspace(Only(dailyAdmission: true), products)));
    }

    /// <summary>A product the shared section resolver suppresses (Bring-A-Friend Duo's dated category never gets its own section) never reaches the PDF model, so it is never copied.</summary>
    [Fact]
    public void Pricing_SuppressedProduct_NotCopied()
    {
        var products = EveryProduct().Append(Product("Bring-A-Friend Duo", SourceCategory.BringAFriendDuo, 50m)).ToList();

        Assert.DoesNotContain("Duo", Pricing(Workspace(PassdownSectionSelection.AllIncluded, products)));
    }

    /// <summary>A product with no available price prints "-" on the PDF; the copy leaves it out instead of inventing a value or pasting a placeholder.</summary>
    [Fact]
    public void Pricing_UnavailablePrice_LeftOut_AndEmptySectionHeadingDropped()
    {
        var products = new List<AssociateProduct>
        {
            Product("Fast Lane Reserve", SourceCategory.DatedFastLane, null),
            Product("Park Ticket", SourceCategory.DatedAdmissionTickets, 59m),
        };

        var text = Pricing(Workspace(Only(dailyAdmission: true, fastLane: true), products))!;

        Assert.DoesNotContain("Fast Lane", text);
        Assert.Equal("Ticket Prices:\r\n\r\nDaily Admission\r\nPark Ticket: $59.00", text);
    }

    [Fact]
    public void Pricing_NoSelectedSections_ReturnsNull_NoEmptyHeading()
    {
        Assert.Null(Pricing(Workspace(NoneSelected)));
    }

    [Fact]
    public void Pricing_AllProductsDeselected_ReturnsNull()
    {
        var products = EveryProduct().Select(p => p with { IncludeOnPdf = false }).ToList();

        Assert.Null(Pricing(Workspace(PassdownSectionSelection.AllIncluded, products)));
    }

    [Fact]
    public void Pricing_IncludedCustomItem_FollowsPdf()
    {
        var custom = new List<CustomProductEntry> { new("c1", "Locker Rental", 15m, "Near the front gate.", true), new("c2", "Hidden Item", 5m, null, false) };

        var text = Pricing(Workspace(NoneSelected, custom: custom))!;

        Assert.Contains("Locker Rental: $15.00", text);
        Assert.DoesNotContain("Hidden Item", text);
        Assert.DoesNotContain("Near the front gate", text);
    }

    // ---------------------------------------------------------------- PDF/clipboard consistency

    /// <summary>
    /// Drift guard: the shared model, the rendered PDF, and the clipboard text all carry exactly the
    /// same sections and products — Daily Admission + Fast Lane selected, Season Passes/Add-Ons/
    /// SCarowinds/Promotions not.
    /// </summary>
    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void PdfAndClipboard_UseTheSameProductInclusionResult(AssociatePassdownTemplate template)
    {
        var model = Model(Workspace(Only(dailyAdmission: true, fastLane: true)));

        string[] expected = ["Park Ticket", "Military/First Responder", "Fast Lane Reserve", "Fast Lane Ultimate"];
        string[] excluded = ["Gold Season Pass", "All Season Dining", "SCarowinds Ticket", "The Conjuring: Beyond Fear", "Bring-A-Friend Ticket"];

        // Shared presentation model.
        Assert.Equal([ProductDisplaySection.DailyAdmission, ProductDisplaySection.FastLane], model.ProductCategories.Select(c => c.Section));
        Assert.Equal(expected, model.ProductCategories.SelectMany(c => c.Products).Select(r => r.DisplayName));

        // PDF.
        var pdf = new AssociatePassdownPdfGenerator().Generate(model, template);
        var pdfText = PdfInspector.ExtractAllText(pdf.PdfBytes!);

        // Clipboard.
        var clip = AssociateTeamsTextFormatter.FormatPricing(model)!;
        var clipNames = clip.Split("\r\n").Where(line => line.Contains(": $") || line.Contains(": From $")).Select(line => line[..line.LastIndexOf(": ", StringComparison.Ordinal)]).ToList();
        Assert.Equal(expected, clipNames);

        foreach (var row in model.ProductCategories.SelectMany(c => c.Products))
        {
            Assert.Contains(row.EffectivePriceText!, pdfText);
            Assert.Contains($"{row.DisplayName}: {row.EffectivePriceText}", clip);
        }

        foreach (var name in excluded)
        {
            Assert.DoesNotContain(name, pdfText);
            Assert.DoesNotContain(name, clip);
        }
    }

    // ---------------------------------------------------------------- Operations Summary

    [Fact]
    public void Operations_NormalDay()
    {
        Assert.Equal(
            "Park Hours: 11:00 AM - 12:00 AM\r\nAvenue: 10:15 AM\r\nNorth Gate: 10:30 AM\r\nProjected Attendance: 18,000\r\nPre-solds: 1,000\r\nERT: Thunder Striker and Skytower",
            Operations(Workspace(PassdownSectionSelection.AllIncluded)));
    }

    [Fact]
    public void Operations_ClosedPark_ShowsClosed_NoGateTimes()
    {
        var closed = ParkHoursCalculator.Calculate(true, null, null, false, null, null, false, null, null);

        var text = Operations(Workspace(PassdownSectionSelection.AllIncluded, hours: closed, ert: EffectiveErt.NotConfigured));

        Assert.StartsWith("Park Hours: Closed", text);
        Assert.DoesNotContain("Avenue", text);
        Assert.DoesNotContain("North Gate", text);
    }

    [Fact]
    public void Operations_HoursNotChecked_DoesNotInventTimes()
    {
        var notChecked = ParkHoursCalculator.Calculate(false, null, null, false, null, null, false, null, null);

        var text = Operations(Workspace(PassdownSectionSelection.AllIncluded, hours: notChecked));

        Assert.StartsWith("Park Hours: Not checked yet", text);
        Assert.DoesNotContain("Avenue", text);
        Assert.DoesNotContain("North Gate", text);
    }

    private static EffectiveHours OpenWith(TimeOnly? northGate, TimeOnly? avenue) =>
        ParkHoursCalculator.Calculate(false, new TimeOnly(11, 0), new TimeOnly(0, 0), false, null, null, false, null, null, northGate, avenue);

    [Fact]
    public void Operations_NorthGateOverride_CopiedAsEffectiveTime_NotLabeled()
    {
        var text = Operations(Workspace(PassdownSectionSelection.AllIncluded, hours: OpenWith(new TimeOnly(10, 0), null)));

        Assert.Contains("North Gate: 10:00 AM", text);
        Assert.Contains("Avenue: 10:15 AM", text);
        Assert.DoesNotContain("Override", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Operations_AvenueOverride_CopiedAsEffectiveTime()
    {
        var text = Operations(Workspace(PassdownSectionSelection.AllIncluded, hours: OpenWith(null, new TimeOnly(9, 45))));

        Assert.Contains("Avenue: 9:45 AM", text);
        Assert.Contains("North Gate: 10:30 AM", text);
        Assert.DoesNotContain("Override", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Operations_BothOverrides_CopiedAsEffectiveTimes()
    {
        var text = Operations(Workspace(PassdownSectionSelection.AllIncluded, hours: OpenWith(new TimeOnly(10, 5), new TimeOnly(9, 50))));

        Assert.Contains("Avenue: 9:50 AM", text);
        Assert.Contains("North Gate: 10:05 AM", text);
    }

    [Theory]
    [InlineData(new[] { "Fury 325" }, "ERT: Fury 325")]
    [InlineData(new[] { "Thunder Striker", "Skytower" }, "ERT: Thunder Striker and Skytower")]
    [InlineData(new[] { "Fury 325", "Copperhead Strike", "Intimidator" }, "ERT: Fury 325, Copperhead Strike, and Intimidator")]
    public void Operations_RidesOnlyErt_ListsRidesWithoutTime(string[] rides, string expectedLine)
    {
        var text = Operations(Workspace(PassdownSectionSelection.AllIncluded, ert: new EffectiveErt(EffectiveErtKind.Configured, EffectiveErtSource.Monthly, new TimeOnly(10, 30), rides, "note")));

        Assert.EndsWith(expectedLine, text);
        Assert.DoesNotContain("10:30 AM", text.Split("\r\n").Last());
    }

    [Fact]
    public void Operations_NoErt_UsesEstablishedWording()
    {
        var text = Operations(Workspace(PassdownSectionSelection.AllIncluded, ert: new EffectiveErt(EffectiveErtKind.NoErt, EffectiveErtSource.Monthly, null, [], null)));

        Assert.EndsWith("ERT: None", text);
    }

    [Fact]
    public void Operations_ErtNotConfigured_SaysSo()
    {
        Assert.EndsWith("ERT: Not configured", Operations(Workspace(PassdownSectionSelection.AllIncluded, ert: EffectiveErt.NotConfigured)));
    }

    [Fact]
    public void Operations_AttendanceAndPreSolds_UseGroupedUsCounts()
    {
        var text = Operations(Workspace(PassdownSectionSelection.AllIncluded, projected: 26450, preSolds: 1234567));

        Assert.Contains("Projected Attendance: 26,450", text);
        Assert.Contains("Pre-solds: 1,234,567", text);
    }

    [Fact]
    public void Operations_MissingOptionalValues_LinesOmitted_NoPlaceholders()
    {
        var text = Operations(Workspace(PassdownSectionSelection.AllIncluded, projected: null, preSolds: null));

        Assert.DoesNotContain("Projected", text);
        Assert.DoesNotContain("Pre-solds", text);
        Assert.DoesNotContain("—", text);
        Assert.DoesNotContain("Not entered", text);
    }

    [Fact]
    public void Operations_NeverIncludesDailyFeatureOrNotes()
    {
        var workspace = Workspace(PassdownSectionSelection.AllIncluded);
        var model = Model(workspace);

        foreach (var kind in Enum.GetValues<AssociateTeamsCopyKind>())
        {
            var text = AssociateTeamsTextFormatter.Format(model, kind)!;
            Assert.DoesNotContain("Not for Teams", text);
            Assert.DoesNotContain("Quote", text);
        }
    }

    [Fact]
    public void Operations_ForeignHostCulture_StillUsFormatting()
    {
        var original = CultureInfo.CurrentCulture;
        var originalUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");

            var workspace = Workspace(Only(dailyAdmission: true));
            var text = AssociateTeamsTextFormatter.Format(Model(workspace), AssociateTeamsCopyKind.OperationsAndPricing)!;

            Assert.Contains("Park Hours: 11:00 AM - 12:00 AM", text);
            Assert.Contains("Projected Attendance: 18,000", text);
            Assert.Contains("Park Ticket: $59.00", text);
            Assert.Contains("From $37.00", text);
            Assert.DoesNotContain("€", text);
            Assert.DoesNotContain("18.000", text);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.CurrentUICulture = originalUi;
        }
    }

    // ---------------------------------------------------------------- Operations + Pricing

    [Fact]
    public void OperationsAndPricing_IsOperationsThenBlankLineThenPricing()
    {
        var model = Model(Workspace(Only(fastLane: true)));

        Assert.Equal(
            AssociateTeamsTextFormatter.FormatOperationsSummary(model) + "\r\n\r\n" + AssociateTeamsTextFormatter.FormatPricing(model),
            AssociateTeamsTextFormatter.Format(model, AssociateTeamsCopyKind.OperationsAndPricing));
    }

    [Fact]
    public void OperationsAndPricing_NoPricingSelected_OperationsOnly_NoEmptyTicketHeading()
    {
        var model = Model(Workspace(NoneSelected));
        var text = AssociateTeamsTextFormatter.Format(model, AssociateTeamsCopyKind.OperationsAndPricing)!;

        Assert.Equal(AssociateTeamsTextFormatter.FormatOperationsSummary(model), text);
        Assert.DoesNotContain("Ticket Prices", text);
    }

    [Fact]
    public void TodaysPricing_NoPricingSelected_FormatReturnsNull()
    {
        Assert.Null(AssociateTeamsTextFormatter.Format(Model(Workspace(NoneSelected)), AssociateTeamsCopyKind.TodaysPricing));
    }

    [Fact]
    public void Formatter_UsesPlainTextOnly_NoMarkdownTables()
    {
        var text = AssociateTeamsTextFormatter.Format(Model(Workspace(PassdownSectionSelection.AllIncluded)), AssociateTeamsCopyKind.OperationsAndPricing)!;

        Assert.DoesNotContain("|", text);
        Assert.DoesNotContain("**", text);
        Assert.DoesNotContain("\t", text);
    }

    // ---------------------------------------------------------------- Phase 19B product lifecycle

    private static readonly PassdownSectionSelection AllSections = new(true, true, true, true, true, true, true);

    private static List<AssociateProduct> WithLimitedPromotion() =>
        [.. EveryProduct(), Product("Limited Weekend Offer", SourceCategory.Promotions, 39m, PriceKind.Sale, comparison: 80m, qualifier: "Sale Price")];

    // §42: a retired product is simply absent from the composed workspace, so the PDF and the copy
    // drop it together; a reactivated one comes back in both. No clipboard-specific lifecycle logic.
    [Fact]
    public void RetiredThenReactivatedProduct_PdfAndPricingCopyStayConsistent()
    {
        var published = Workspace(AllSections, WithLimitedPromotion());
        var retired = Workspace(AllSections, EveryProduct());
        var reactivated = Workspace(AllSections, WithLimitedPromotion());

        static bool PdfHas(AssociateWorkspace w) => Model(w).ProductCategories.SelectMany(c => c.Products).Any(p => p.DisplayName == "Limited Weekend Offer");

        Assert.True(PdfHas(published));
        Assert.Contains("Limited Weekend Offer", Pricing(published));
        Assert.False(PdfHas(retired));
        Assert.DoesNotContain("Limited Weekend Offer", Pricing(retired));
        Assert.True(PdfHas(reactivated));
        Assert.Contains("Limited Weekend Offer", Pricing(reactivated));
    }

    // §43/§27: once every SCarowinds product has retired (and SCarowinds is not published for the
    // date), no empty SCarowinds section reaches the PDF or the copied pricing.
    [Fact]
    public void AllScarowindsProductsRetired_NoEmptyScarowindsSectionInPdfOrPricingCopy()
    {
        var regularOnly = EveryProduct().Where(p => p.Category != SourceCategory.HauntedAttractions).ToList();
        var workspace = Workspace(AllSections, regularOnly) with
        {
            CategoryStatuses = [new AssociateProductCategoryStatus(SourceCategory.HauntedAttractions, AssociateVerificationState.NotPublished, GeneratedAt, "Not published.")],
        };

        var model = Model(workspace);
        var text = Pricing(workspace)!;

        Assert.DoesNotContain(model.ProductCategories, c => c.Section == ProductDisplaySection.SCarowinds);
        Assert.All(model.ProductCategories, c => Assert.NotEmpty(c.Products));
        Assert.DoesNotContain("SCarowinds", text);
        Assert.Contains("Daily Admission", text);
    }
}
