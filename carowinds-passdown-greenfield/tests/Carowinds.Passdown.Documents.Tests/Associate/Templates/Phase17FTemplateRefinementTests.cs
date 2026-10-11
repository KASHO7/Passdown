using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;
using Carowinds.Passdown.Documents.Tests.Associate.Templates.TestSupport;
using Carowinds.Passdown.Domain.Ert;

namespace Carowinds.Passdown.Documents.Tests.Associate.Templates;

/// <summary>
/// Phase 17F visual-refinement guarantees, proven from real rendered glyph positions (PdfPig) rather
/// than byte equality: Modern without section numbering yet still structurally distinct from Classic
/// (two-line date, four-column metadata grid), aligned price columns ("From $X" included), stable
/// long-name rows, collapsing optional sections, the multi-page-only "1 / 2" footer indicator, and
/// "compress before paginating". Models are built directly from the public document-model records so
/// each scenario controls exactly the rows it needs.
/// </summary>
public sealed class Phase17FTemplateRefinementTests
{
    private static readonly IAssociatePassdownPdfGenerator Generator = new AssociatePassdownPdfGenerator();

    public static IEnumerable<object[]> AllTemplates()
    {
        yield return [AssociatePassdownTemplate.Classic];
        yield return [AssociatePassdownTemplate.Compact];
        yield return [AssociatePassdownTemplate.Modern];
    }

    private static PdfProductRow Row(string name, string price) => new(name, price, null, null, null);

    private static PdfProductCategory Category(string name, ProductDisplaySection? section, params PdfProductRow[] rows) =>
        new(name, section, PdfProductSectionTier.Reference, rows);

    private static PdfProductCategory Admission() => Category("Daily Admission", ProductDisplaySection.DailyAdmission,
        Row("Park Ticket", "$55"), Row("Tricks and Treats Family Pack", "$45"), Row("Military & First Responder Tickets", "From $37"));

    private static PdfProductCategory FastLane() => Category("Fast Lane", ProductDisplaySection.FastLane, Row("Reserve", "$65"), Row("Ultimate", "$109"));

    private static PdfProductCategory Promotions() => Category("Promotions", ProductDisplaySection.Promotions, Row("Bring-A-Friend", "$50"), Row("Slash Sale", "$39"));

    private static PdfProductCategory SeasonPasses(int extra = 0) => Category("Season Passes", ProductDisplaySection.SeasonPasses,
        [Row("2027 Gold Pass", "$99"), Row("2027 Silver Pass", "$79"), Row("2027 Prestige Pass", "$249"), Row("2027 Military Gold Pass", "$89"),
         .. Enumerable.Range(1, extra).Select(i => Row($"2027 Pass Variant {i}", $"${100 + i}"))]);

    private static AssociatePassdownDocumentModel Model(IEnumerable<PdfProductCategory> categories, IReadOnlyList<string>? notes = null) => new(
        new DateOnly(2026, 9, 19),
        "Saturday, September 19, 2026",
        new PdfParkHours(PdfParkHoursStatus.Open, "11:00 AM – 12:00 AM", null, "10:30 AM", "10:15 AM", null, null),
        new PdfAttendance(20000, 1200),
        new PdfEarlyRideTime(EffectiveErtKind.Configured, EffectiveErtSource.Monthly, null, ["Thunderstriker", "Skytower"], null),
        new PdfDailyFeature("Useless Fact of the Day", "Did you know the park opened in 1973?"),
        notes ?? ["Scanner 3 at South Gate remains out of service.", "Verify stroller stock before evening rush."],
        categories.ToList(),
        HasUnverifiedProductPricing: false,
        "Sep 15 • 3:25 PM");

    private static AssociatePassdownDocumentModel NormalModel() => Model([Admission(), FastLane(), Promotions(), SeasonPasses()]);

    private static byte[] Render(AssociatePassdownDocumentModel model, AssociatePassdownTemplate template) =>
        Generator.Generate(model, template).PdfBytes!;

    // ---- Modern: no numbering, yet distinct from Classic -------------------------------------------

    [Fact]
    public void Modern_PrintsNoSectionNumbersLettersOrNumerals_BesideItsHeadings()
    {
        var words = PdfInspector.Words(Render(NormalModel(), AssociatePassdownTemplate.Modern), 1);

        // No "01".."09", Roman numerals, or lone letters immediately before a section heading word.
        string[] headingFirstWords = ["DAILY", "FAST", "PROMOTIONS", "SEASON", "OPERATIONAL"];
        for (var i = 1; i < words.Count; i++)
        {
            if (headingFirstWords.Contains(words[i]))
            {
                Assert.DoesNotMatch(@"^(0?\d{1,2}|[IVX]+|[A-Z])\.?$", words[i - 1]);
            }
        }

        Assert.DoesNotContain(words, word => System.Text.RegularExpressions.Regex.IsMatch(word, @"^0\d$"));
    }

    // Modern's date is two lines (weekday above month/day/year); Classic and Compact print it on one.
    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void DateHeading_IsTwoLinesOnlyInModern(AssociatePassdownTemplate template)
    {
        var bytes = Render(NormalModel(), template);

        var weekday = PdfInspector.FindWordBox(bytes, 1, template == AssociatePassdownTemplate.Modern ? "SATURDAY" : "SATURDAY,");
        var month = PdfInspector.FindWordBox(bytes, 1, "SEPTEMBER");
        Assert.NotNull(weekday);
        Assert.NotNull(month);

        var verticalGap = weekday!.Value.Top - month!.Value.Top;
        if (template == AssociatePassdownTemplate.Modern)
        {
            Assert.True(verticalGap > 8, $"Modern's weekday should sit on its own line above the month (gap {verticalGap}pt).");
        }
        else
        {
            Assert.True(Math.Abs(verticalGap) < 2, $"{template}'s date should be one line (gap {verticalGap}pt).");
        }
    }

    // Modern's metadata grid puts NORTH GATE / AVENUE / ATTENDANCE / PRE-SOLDS on one row as four
    // columns; Classic's traditional list stacks North Gate, Avenue, Attendance, Pre-solds vertically.
    [Fact]
    public void Modern_MetadataGrid_FourLabelsShareOneRow_WhileClassicStacksThem()
    {
        var modern = Render(NormalModel(), AssociatePassdownTemplate.Modern);
        var modernTops = new[] { "NORTH", "AVENUE", "ATTENDANCE", "PRE-SOLDS" }
            .Select(label => PdfInspector.FindWordBox(modern, 1, label)!.Value.Top).ToList();
        Assert.True(modernTops.Max() - modernTops.Min() < 1.5, "Modern's four metadata labels should share one row.");

        var modernLefts = new[] { "NORTH", "AVENUE", "ATTENDANCE", "PRE-SOLDS" }
            .Select(label => PdfInspector.FindWordBox(modern, 1, label)!.Value.Left).ToList();
        Assert.Equal(modernLefts.OrderBy(x => x), modernLefts);

        var classic = Render(NormalModel(), AssociatePassdownTemplate.Classic);
        var classicTops = new[] { "North", "Avenue", "Attendance", "Pre-solds" }
            .Select(label => PdfInspector.FindWordBox(classic, 1, label)!.Value.Top).ToList();
        Assert.Equal(classicTops.OrderByDescending(top => top), classicTops);
        Assert.True(classicTops[0] - classicTops[^1] > 20, "Classic's summary should be a vertical list.");

        // And the two documents differ in their top-level markers, not just spacing.
        var classicText = PdfInspector.ExtractAllText(classic);
        var modernText = PdfInspector.ExtractAllText(modern);
        Assert.Contains("PARK HOURS", classicText);
        Assert.DoesNotContain("PARK HOURS", modernText);
        Assert.Contains("EARLY RIDE", modernText);
    }

    // Classic is single column: every section heading starts at the same left edge, one under another.
    [Fact]
    public void Classic_SectionsStackInOneColumn()
    {
        var bytes = Render(NormalModel(), AssociatePassdownTemplate.Classic);
        var headings = new[] { "DAILY", "FAST", "PROMOTIONS", "SEASON" }.Select(w => PdfInspector.FindWordBox(bytes, 1, w)!.Value).ToList();

        Assert.True(headings.Max(h => h.Left) - headings.Min(h => h.Left) < 1, "Classic headings should share one left edge.");
        Assert.Equal(headings.Select(h => h.Top).OrderByDescending(t => t), headings.Select(h => h.Top));
    }

    // Compact pairs Promotions with Season Passes on one row (its defining trait).
    [Fact]
    public void Compact_PromotionsAndSeasonPasses_SharePairedRow()
    {
        var bytes = Render(NormalModel(), AssociatePassdownTemplate.Compact);
        var promotions = PdfInspector.FindWordBox(bytes, 1, "PROMOTIONS")!.Value;
        var passes = PdfInspector.FindWordBox(bytes, 1, "SEASON")!.Value;

        Assert.True(Math.Abs(promotions.Top - passes.Top) < 1.5);
        Assert.True(passes.Left > promotions.Left + 150);
    }

    // ---- Price alignment ---------------------------------------------------------------------------

    // Every price in a section shares one right edge, "From $37" included.
    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void Prices_ShareOneRightEdge_IncludingFromPrices(AssociatePassdownTemplate template)
    {
        var bytes = Render(Model([Admission()]), template);

        var rights = new[] { "$55", "$45", "$37" }.Select(price => PdfInspector.FindWordBox(bytes, 1, price)!.Value.Right).ToList();
        Assert.True(rights.Max() - rights.Min() < 0.75, $"{template}: price right edges differ ({string.Join(", ", rights)}).");

        // "From" sits immediately left of its amount, inside the same price column.
        var from = PdfInspector.FindWordBox(bytes, 1, "From")!.Value;
        var amount = PdfInspector.FindWordBox(bytes, 1, "$37")!.Value;
        Assert.True(Math.Abs(from.Top - amount.Top) < 1);
        Assert.True(from.Right < amount.Left && amount.Left - from.Right < 6);
    }

    // Classic/Modern use one price column for every single-column section on the page.
    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void SingleColumnTemplates_PriceColumnIsSharedAcrossSections(AssociatePassdownTemplate template)
    {
        var bytes = Render(NormalModel(), template);
        var rights = new[] { "$55", "$109", "$39", "$249" }.Select(price => PdfInspector.FindWordBox(bytes, 1, price)!.Value.Right).ToList();

        Assert.True(rights.Max() - rights.Min() < 0.75, $"{template}: price columns differ across sections ({string.Join(", ", rights)}).");
    }

    // Compact: a pair member whose partner is absent stays at column width, so its prices line up
    // with the paired sections above it instead of jumping to the page edge.
    [Fact]
    public void Compact_LonePairMember_KeepsColumnWidthPriceAlignment()
    {
        var bytes = Render(Model([Admission(), FastLane(), Promotions()]), AssociatePassdownTemplate.Compact);

        var admissionPrice = PdfInspector.FindWordBox(bytes, 1, "$55")!.Value.Right;
        var promotionsPrice = PdfInspector.FindWordBox(bytes, 1, "$50")!.Value.Right;

        Assert.True(Math.Abs(admissionPrice - promotionsPrice) < 0.75);
    }

    // Compact: when each pairing has only one side present, the two lone sections share one row
    // (Daily Admission left, Season Passes right) rather than stacking beside a blank right half.
    [Fact]
    public void Compact_TwoLonePairMembers_ShareOneRow()
    {
        var bytes = Render(Model([Admission(), SeasonPasses()]), AssociatePassdownTemplate.Compact);
        var admission = PdfInspector.FindWordBox(bytes, 1, "DAILY")!.Value;
        var passes = PdfInspector.FindWordBox(bytes, 1, "SEASON")!.Value;

        Assert.True(Math.Abs(admission.Top - passes.Top) < 1.5);
        Assert.True(passes.Left > admission.Left + 150);
    }

    // ---- Long product names ------------------------------------------------------------------------

    // A long name wraps inside its own column at the normal size; the price stays on the row's first
    // line, in the shared price column — never pushed to a line of its own.
    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void LongProductName_WrapsCleanly_PriceStaysOnFirstLineInPriceColumn(AssociatePassdownTemplate template)
    {
        const string longName = "Ticket + Haunted Attraction Express Pass Bundle with Preferred Parking, All Season Dining, Two Haunted Maze Upgrades, Front-Row Show Seating and a Commemorative Souvenir Bottle";
        var model = Model([Category("Daily Admission", ProductDisplaySection.DailyAdmission, Row("Park Ticket", "$55"), Row(longName, "From $189"))]);
        var bytes = Render(model, template);

        var firstWord = PdfInspector.FindWordBox(bytes, 1, "Ticket", occurrence: 1)!.Value; // occurrence 0 is "Park Ticket".
        var lastWord = PdfInspector.FindWordBox(bytes, 1, "Bottle")!.Value;
        var price = PdfInspector.FindWordBox(bytes, 1, "$189")!.Value;
        var shortPrice = PdfInspector.FindWordBox(bytes, 1, "$55")!.Value;

        Assert.True(firstWord.Top - lastWord.Top > 5, $"{template}: the long name should wrap onto a second line.");
        Assert.True(Math.Abs(price.Top - firstWord.Top) < 1.5, $"{template}: price should sit on the name's first line.");
        Assert.True(Math.Abs(price.Right - shortPrice.Right) < 0.75, $"{template}: wrapped row's price left the price column.");
        Assert.True(lastWord.Right < price.Left, $"{template}: wrapped name text must not run under the price column.");
    }

    // ---- Optional sections -------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void AbsentSections_PrintNoHeadingRuleOrPlaceholder(AssociatePassdownTemplate template)
    {
        var bytes = Render(Model([Admission()], notes: []), template);
        var text = PdfInspector.ExtractAllText(bytes);

        Assert.DoesNotContain("FAST LANE", text);
        Assert.DoesNotContain("PROMOTIONS", text);
        Assert.DoesNotContain("SEASON PASSES", text);
        Assert.DoesNotContain("SCAROWINDS", text);
        Assert.DoesNotContain("ADD-ONS", text);
        Assert.DoesNotContain("NOTES", text);
    }

    // ---- Footer / pagination -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void SinglePage_FooterShowsTimestampOnly_NoPageIndicator(AssociatePassdownTemplate template)
    {
        var result = Generator.Generate(NormalModel(), template);
        Assert.Equal(1, result.PageCount);

        var text = PdfInspector.ExtractAllText(result.PdfBytes!);
        Assert.Contains("Sep 15 • 3:25 PM", text);
        Assert.DoesNotContain("1 / 1", text);
        Assert.DoesNotMatch(@"\d+\s*/\s*\d+", text);
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void MultiPage_EveryPageShowsTimestampAndPageOfTotal(AssociatePassdownTemplate template)
    {
        var custom = Category("Custom / Internal", null, Enumerable.Range(1, 60).Select(i => Row($"Internal Item {i}", $"${i}")).ToArray());
        var result = Generator.Generate(Model([Admission(), FastLane(), Promotions(), SeasonPasses(), custom]), template);
        Assert.True(result.PageCount > 1, $"{template}: stress model should overflow.");

        for (var page = 1; page <= result.PageCount; page++)
        {
            var pageText = PdfInspector.ExtractPageText(result.PdfBytes!, page);
            Assert.Contains("Sep 15 • 3:25 PM", pageText);
            PdfTextAssert.ContainsIgnoringWhitespace($"{page} / {result.PageCount}", pageText);
        }
    }

    // "Compress before paginating": a day that only just overflows at standard spacing must come back
    // as one page (tighter spacing, same content and type sizes) instead of stranding a tiny remainder
    // — such as two or three notes — alone on page 2.
    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void JustOverflowingDay_IsRecoveredToOnePage_ByTighterSpacing(AssociatePassdownTemplate template)
    {
        var busyAdmission = Category("Daily Admission", ProductDisplaySection.DailyAdmission,
            Row("Park Ticket", "$55"), Row("Tricks and Treats Family Pack", "$45"), Row("Military & First Responder Tickets", "From $37"),
            Row("Two-Day Ticket", "$85"), Row("Senior Ticket", "$45"), Row("Junior Ticket", "$45"), Row("After 4 PM Ticket", "$40"));
        var busyFastLane = Category("Fast Lane", ProductDisplaySection.FastLane, Row("Reserve", "$65"), Row("Ultimate", "$109"), Row("Fast Lane Plus", "$129"), Row("Single Ride", "$25"));
        var busyPromotions = Category("Promotions", ProductDisplaySection.Promotions, Row("Bring-A-Friend", "$50"), Row("Slash Sale", "$39"), Row("Bring-A-Friend Duo", "$95"), Row("Flash Sale", "$42"));
        var scarowinds = Category("SCarowinds", ProductDisplaySection.SCarowinds, Row("Haunted Attractions Pass", "$15"), Row("Haunted Attractions Express", "$55"), Row("SCarowinds Bundle", "From $55"));
        var notes = new[] { "Scanner 3 at South Gate remains out of service.", "Verify stroller stock before evening rush.", "Confirm closing staffing before 8:00 PM." };

        var result = Generator.Generate(Model([busyAdmission, busyFastLane, busyPromotions, SeasonPasses(), scarowinds], notes), template);

        Assert.Equal(1, result.PageCount);
        var text = PdfInspector.ExtractAllText(result.PdfBytes!);
        foreach (var note in notes)
        {
            PdfTextAssert.ContainsIgnoringWhitespace(note, text);
        }
    }

    // Orphan control: a section heading never ends a page without at least one of its rows beneath
    // it — checked on every page of an overflowing document.
    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void Overflow_NeverLeavesASectionHeadingAloneAtPageBottom(AssociatePassdownTemplate template)
    {
        var custom = Category("Custom / Internal", null, Enumerable.Range(1, 45).Select(i => Row($"Internal Item {i}", $"${i}")).ToArray());
        var addOns = Category("Add-Ons", ProductDisplaySection.AddOns, Row("All Season Dining", "$129"), Row("Preferred Parking", "$35"));
        var model = Model([Admission(), FastLane(), Promotions(), SeasonPasses(extra: 6), custom, addOns],
            notes: Enumerable.Range(1, 6).Select(i => $"Operational note {i} for zone {i}.").ToList());
        var result = Generator.Generate(model, template);
        Assert.True(result.PageCount > 1);

        string[] headings = ["DAILY ADMISSION", "FAST LANE", "PROMOTIONS", "SEASON PASSES", "CUSTOM / INTERNAL", "ADD-ONS", "OPERATIONAL NOTES", "NOTES"];
        for (var page = 1; page <= result.PageCount; page++)
        {
            // Drop the footer ("Sep 15 • 3:25 PM   1 / 2") and look at what the page body ends with.
            var words = PdfInspector.Words(result.PdfBytes!, page).ToList();
            var footerStart = words.LastIndexOf("Sep");
            var body = string.Join(" ", footerStart >= 0 ? words.Take(footerStart) : words);

            Assert.False(headings.Any(heading => body.EndsWith(heading, StringComparison.Ordinal)),
                $"{template} page {page} ends with a stranded section heading: '...{body[^Math.Min(40, body.Length)..]}'.");
        }
    }
}
