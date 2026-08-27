using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Carowinds.Passdown.Documents.Associate;

/// <summary>
/// QuestPDF-backed <see cref="IAssociatePassdownPdfGenerator"/> (spec Part H; Phase 8B redesign to a
/// one-page-normal document; revised Phase 13D Part AA/AB so overflow content renders onto real
/// additional pages instead of being refused). No network, database, filesystem, or Desktop
/// dependency — every value it prints comes from the <see cref="AssociatePassdownDocumentModel"/>
/// passed in, so the same model always produces the same bytes. Layout follows spec §11.1/§11.2 and
/// Phase 8B Parts 2-11: US Letter portrait, black/white-safe (text, weight, and hairline borders
/// only — no color-coded meaning). Generation is normally one page but is no longer capped at one —
/// see <see cref="AssociatePassdownPdfResult.PageCount"/> for the real, authoritative count, which
/// the caller must use to show an unmistakable multi-page warning rather than silently accepting a
/// spilled-over document. Content is organized by visual hierarchy tier (spec Part 3) rather than as
/// a flat table: an operational summary strip, a distinct Daily Feature panel, Key Selling price
/// tiles, Promotion callout cards, compact adaptive Reference columns, and an Operational Notes
/// panel — each its own small reusable composer method below rather than one large method of
/// arbitrary coordinates (spec Part 11).
/// </summary>
public sealed class AssociatePassdownPdfGenerator : IAssociatePassdownPdfGenerator
{
    private const float PageMargin = 32f;
    private const float BoxSpacing = 8f;

    // Caps applied only when a single tile/card would otherwise stretch across the entire row width
    // (spec Phase 8B polish Parts 1/2) — centered instead, so a lone product/promotion reads as a
    // deliberately wider, balanced card rather than an awkwardly empty full-width box. Two or more
    // items per row instead divide the full row width evenly via QuestPDF <c>RelativeItem</c>, with
    // no cap, matching the spec's 50/50 and thirds mockups.
    private const float KeySellingSingleTileMaxWidth = 230f;
    private const float PromotionalSingleCardMaxWidth = 300f;
    private const float KeySellingTileGap = 8f;
    private const float PromotionalCardGap = 10f;

    // Carowinds Admissions Operations brand palette (blue/white/red retheme), matching the Desktop
    // shell's tokens (App.axaml) so the on-screen draft and the printed handoff read as the same
    // product. Applied only to decorative bands/rules/headings — never the sole carrier of meaning
    // (spec §11.1) — and dark enough to stay legible converted to grayscale.
    private static readonly string BrandDarkColor = "#0B2A4A";
    private static readonly string AccentColor = "#1E5AA8";
    private static readonly string HeaderMutedColor = "#B9CBDE";
    private static readonly string PanelFill = "#F2F6FB";
    private static readonly string BandFill = "#E4EDF7";
    private static readonly string FeatureFill = "#EAF0F8";
    private static readonly string HairlineColor = "#B7C1CC";
    private static readonly string ComparePriceColor = "#5C6673";
    private static readonly string MutedTextColor = "#57626D";
    private static readonly string DealPriceColor = "#9A2A2A";

    private readonly IAssociatePassdownPreflightService _preflightService;

    static AssociatePassdownPdfGenerator()
    {
        // QuestPDF requires this to be set once before any document is generated (library
        // requirement, not application state we vary) — see spec Part L's licensing note.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public AssociatePassdownPdfGenerator(IAssociatePassdownPreflightService? preflightService = null)
    {
        _preflightService = preflightService ?? new AssociatePassdownPreflightService();
    }

    public AssociatePassdownPdfResult Generate(AssociatePassdownDocumentModel model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        cancellationToken.ThrowIfCancellationRequested();

        var preflight = _preflightService.Evaluate(model);
        if (!preflight.CanGenerate)
        {
            // Phase 13D Part AA: the only remaining generation-refusal case is "nothing to print"
            // (no operating date) — an estimated one-page overflow no longer refuses generation.
            return new AssociatePassdownPdfResult(AssociatePassdownPdfStatus.ValidationFailed, null, preflight, PageCount: 0);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Phase 13D Part AA/AB: the Associate Passdown is normally one page, but content that
        // overflows is now allowed to render onto real additional pages rather than being refused
        // outright — QuestPDF does not throw when a single Page()'s content overflows it, it simply
        // continues drawing onto additional physical pages. CountGeneratedPdfPages below reads the
        // actual, authoritative page count of what was produced (never the pre-render estimate) so
        // the caller can show an unmistakable warning without losing the associate's work.
        var document = Document.Create(container => container.Page(page => ComposePage(page, model, cancellationToken)));
        var bytes = document.GeneratePdf();
        var pageCount = CountGeneratedPdfPages(bytes);

        return new AssociatePassdownPdfResult(AssociatePassdownPdfStatus.Generated, bytes, preflight, pageCount);
    }

    /// <summary>
    /// Reads the page count directly out of QuestPDF's own generated bytes rather than pulling in a
    /// PDF-parsing library as a production dependency — safe to do only because this is trusted
    /// output from our own well-formed writer (never an externally supplied PDF), whose page tree
    /// root object always looks like <c>&lt;&lt;/Type /Pages ... /Count N&gt;&gt;</c> in
    /// uncompressed object bytes. Falls back to trusting the estimate (1) if that shape is ever not
    /// found, so a parsing quirk in this narrow safety net can never itself cause a spurious
    /// rejection of an otherwise-valid one-page document.
    /// </summary>
    private static int CountGeneratedPdfPages(byte[] pdfBytes)
    {
        var text = System.Text.Encoding.Latin1.GetString(pdfBytes);
        var match = System.Text.RegularExpressions.Regex.Match(text, @"/Type\s*/Pages\b.*?/Count\s+(\d+)", System.Text.RegularExpressions.RegexOptions.Singleline);
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 1;
    }

    private static void ComposePage(PageDescriptor page, AssociatePassdownDocumentModel model, CancellationToken cancellationToken)
    {
        ConfigurePage(page);
        page.Header().Element(header => ComposeHeader(header, model));

        page.Content().PaddingTop(6).Column(column =>
        {
            column.Spacing(BoxSpacing);

            ComposeOperationalSummary(column, model);
            ComposeDailyFeaturePanel(column, model);

            cancellationToken.ThrowIfCancellationRequested();

            ComposeProductTiers(column, model.ProductCategories);
            ComposeNotes(column, model.OperationalNotes, "Operational Notes");
        });

        page.Footer().Element(footer => ComposeFooter(footer, model));
    }

    private static void ConfigurePage(PageDescriptor page)
    {
        page.Size(PageSizes.Letter);
        page.Margin(PageMargin);
        page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Black));
    }

    private static void ComposeHeader(IContainer container, AssociatePassdownDocumentModel model)
    {
        // A full-width brand band (spec Part L visual-polish pass) rather than a thin accent bar on
        // white — this is the strongest single identity element on the page, mirroring the Desktop
        // shell's own brand band (MainWindow.axaml). Day/date carries the strongest single piece of
        // Level 1 information (spec Phase 8B Part 3), so it sits large and right-aligned here rather
        // than buried in the body.
        container.Background(BrandDarkColor).PaddingHorizontal(14).PaddingVertical(10).Column(column =>
        {
            column.Item().Row(row =>
            {
                // A small ascending-track mark (spec §10.1) — the same plain geometric bars used in
                // the Desktop shell's header, not an image asset.
                row.AutoItem().AlignBottom().Row(mark =>
                {
                    mark.AutoItem().Width(3).Height(8).Background(Colors.White);
                    mark.ConstantItem(2);
                    mark.AutoItem().Width(3).Height(13).Background(Colors.White);
                    mark.ConstantItem(2);
                    mark.AutoItem().Width(3).Height(18).Background(Colors.White);
                });

                row.ConstantItem(10);

                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("CAROWINDS ADMISSIONS OPERATIONS").FontSize(8).Bold()
                        .LetterSpacing(0.12f).FontColor(HeaderMutedColor);

                    left.Item().PaddingTop(2).Text("Associate Passdown").FontSize(21).Bold().FontColor(Colors.White);
                });

                row.ConstantItem(170).AlignRight().AlignMiddle()
                    .Text(model.OperatingDateText).FontSize(14).Bold().FontColor(Colors.White);
            });

            column.Item().PaddingTop(8).Height(2.5f).Background(AccentColor);
        });
    }

    private static void ComposePanel(IContainer container, string heading, Action<ColumnDescriptor> content, bool isHero = false)
    {
        container.Background(PanelFill).Border(1).BorderColor(HairlineColor)
            .BorderTop(isHero ? 2.5f : 1).BorderColor(isHero ? AccentColor : HairlineColor)
            .Padding(7).Column(column =>
        {
            column.Spacing(2);
            column.Item().Text(heading).Bold().FontSize(8.5f).LetterSpacing(0.06f).FontColor(AccentColor);
            content(column);
        });
    }

    /// <summary>
    /// Level 1 (spec Phase 8B Part 3): Park Hours, Attendance, and Early Ride Time as three visually
    /// distinct blocks in a single row — the highest-priority "what's happening today" content,
    /// immediately recognizable when an associate picks up the sheet. Park Hours gets the widest
    /// share since it carries the most content (headline, badge, gate times, verification/buyout
    /// text). The Daily Feature (Level 2) deliberately does not share this row — it gets its own
    /// full-width panel below (<see cref="ComposeDailyFeaturePanel"/>) so operational facts and the
    /// "personality" content stay visually distinct, matching Part 3's tier separation.
    /// </summary>
    private static void ComposeOperationalSummary(ColumnDescriptor column, AssociatePassdownDocumentModel model)
    {
        column.Item().Row(row =>
        {
            row.RelativeItem(3).Element(box => ComposeHoursBox(box, model));
            row.ConstantItem(8);
            row.RelativeItem(2).Element(box => ComposeAttendanceBox(box, model));
            row.ConstantItem(8);
            row.RelativeItem(2).Element(box => ComposeEarlyRideTimeBox(box, model));
        });
    }

    private static void ComposeHoursBox(IContainer container, AssociatePassdownDocumentModel model) =>
        ComposePanel(container, "PARK HOURS", column =>
        {
            column.Item().PaddingTop(1).Text(model.ParkHours.HeadlineText).FontSize(16).Bold();

            if (model.ParkHours.BadgeText is { } badge)
            {
                column.Item().Text(badge).FontSize(8.5f).Italic().FontColor(MutedTextColor);
            }

            column.Item().PaddingTop(1).Text(text =>
            {
                text.Span("North Gate  ").FontSize(9).FontColor(MutedTextColor);
                text.Span(model.ParkHours.NorthGateText ?? "-").FontSize(9).Bold();
                text.Span("     Avenue  ").FontSize(9).FontColor(MutedTextColor);
                text.Span(model.ParkHours.AvenueText ?? "-").FontSize(9).Bold();
            });

            if (model.ParkHours.VerificationText is { } verification)
            {
                column.Item().Text(verification).FontSize(8).FontColor(MutedTextColor);
            }

            if (model.ParkHours.PrivateBuyoutDetails is { } details)
            {
                column.Item().Text($"Buyout details: {details}").FontSize(8.5f);
            }
        }, isHero: true);

    private static void ComposeAttendanceBox(IContainer container, AssociatePassdownDocumentModel model) =>
        ComposePanel(container, "ATTENDANCE", column =>
        {
            column.Item().PaddingTop(1).Text(text =>
            {
                text.Span("Projected  ").FontSize(9).FontColor(MutedTextColor);
                text.Span(FormatCount(model.Attendance.ProjectedAttendance)).FontSize(13).Bold();
            });
            column.Item().Text(text =>
            {
                text.Span("Pre-solds  ").FontSize(9).FontColor(MutedTextColor);
                text.Span(FormatCount(model.Attendance.PreSolds)).FontSize(13).Bold();
            });
        });

    /// <summary>
    /// Phase 13F: the start time is no longer free-typed into <see cref="PdfEarlyRideTime.Details"/> —
    /// it is <see cref="PdfParkHours.NorthGateText"/>, the exact same already-derived value the Park
    /// Hours box prints as "North Gate" (spec Part L: one shared derived value, never two independent
    /// calculations). Printed again here so a reader glancing only at this box still sees the time
    /// (spec Part R), never fabricated when no effective opening exists (spec Parts H/I/J).
    /// </summary>
    private static void ComposeEarlyRideTimeBox(IContainer container, AssociatePassdownDocumentModel model) =>
        ComposePanel(container, "EARLY RIDE TIME", column =>
        {
            if (!model.EarlyRideTime.Enabled)
            {
                column.Item().Text("Not applicable for this date.").FontSize(9.5f).Italic().FontColor(MutedTextColor);
                return;
            }

            // PdfParkHours.NorthGateText is never null (it formats to "-" when there is no time), so
            // Status — not the formatted string — is what actually distinguishes "has a derived
            // start" from "nothing to derive from" (Status is only Open when Opening is non-null).
            if (model.ParkHours.Status == PdfParkHoursStatus.Open)
            {
                column.Item().Text(text =>
                {
                    text.Span("Start  ").FontSize(9).FontColor(MutedTextColor);
                    text.Span(model.ParkHours.NorthGateText).FontSize(11).Bold();
                });
            }
            else
            {
                column.Item().Text(EarlyRideTimeUnavailableText(model.ParkHours.Status)).FontSize(9).Italic().FontColor(MutedTextColor);
            }

            if (model.EarlyRideTime.Details is { } details)
            {
                column.Item().Text(details).FontSize(9.5f);
            }

            if (model.EarlyRideTime.ParticipatingAttractions.Count > 0)
            {
                column.Item().Text($"Attractions: {string.Join(", ", model.EarlyRideTime.ParticipatingAttractions)}").FontSize(9);
            }
        });

    private static string EarlyRideTimeUnavailableText(PdfParkHoursStatus status) => status switch
    {
        PdfParkHoursStatus.Closed => "Start time not applicable — park is closed.",
        _ => "Start time unavailable — park hours not yet confirmed.",
    };

    /// <summary>
    /// Level 2 (spec Phase 8B Part 3): the Daily Feature gets its own full-width panel, visually
    /// distinct from the operational-fact strip above and the product/reference content below — a
    /// thick accent rule and a heading that IS the selected type (e.g. "USELESS FACT OF THE DAY")
    /// rather than a generic "Daily Feature" label give the page a moment of personality without any
    /// emoji or icon (spec's explicit "do not use" list).
    /// </summary>
    private static void ComposeDailyFeaturePanel(ColumnDescriptor column, AssociatePassdownDocumentModel model)
    {
        column.Item().Background(FeatureFill).BorderLeft(4).BorderColor(AccentColor)
            .Padding(9).Column(inner =>
        {
            inner.Spacing(2);
            inner.Item().Text(model.DailyFeature.TypeLabel?.ToUpperInvariant() ?? "DAILY FEATURE")
                .Bold().FontSize(10).LetterSpacing(0.06f).FontColor(BrandDarkColor);

            if (!model.DailyFeature.HasContent)
            {
                inner.Item().Text("Not entered.").FontSize(10).Italic().FontColor(MutedTextColor);
                return;
            }

            if (model.DailyFeature.Text is { } text)
            {
                inner.Item().PaddingTop(2).Text(text).FontSize(11.5f).Italic();
            }
        });
    }

    /// <summary>
    /// Groups the already-ordered categories by <see cref="PdfProductSectionTier"/> and renders each
    /// tier with its own reusable composer (spec Part 11) — Key Selling tiles, Promotion callout
    /// cards, then compact Reference columns. A tier with no categories renders nothing at all
    /// (spec Part 7's "zero layout space when hidden"), so hiding every category in a tier collapses
    /// that entire block rather than leaving a labeled gap.
    /// </summary>
    private static void ComposeProductTiers(ColumnDescriptor column, IReadOnlyList<PdfProductCategory> categories)
    {
        if (categories.Count == 0)
        {
            return;
        }

        column.Item().Column(section =>
        {
            section.Spacing(6);

            var keySelling = categories.Where(c => c.Tier == PdfProductSectionTier.KeySelling).ToList();
            var promotional = categories.Where(c => c.Tier == PdfProductSectionTier.Promotional).ToList();
            var reference = categories.Where(c => c.Tier == PdfProductSectionTier.Reference).ToList();

            ComposeKeySellingSection(section, keySelling);
            ComposePromotionalSection(section, promotional);
            ComposeReferenceSection(section, reference);
        });
    }

    /// <summary>
    /// Level 3 (spec Part 3): Daily Admission / Fast Lane as a labeled band per category followed by
    /// an adaptive grid of price tiles (spec Phase 8B polish Part 1) — a product's name and price are
    /// each their own visual unit instead of one column in a spreadsheet-style row, and the price
    /// itself carries far more weight (spec's "$59" example) than the product name above it. The
    /// grid responds to how many products the category actually has (<see cref="AdaptiveGridLayout"/>)
    /// rather than using a fixed tile width, so a category with only one or two products fills the
    /// row width appropriately instead of leaving most of it empty.
    /// </summary>
    private static void ComposeKeySellingSection(ColumnDescriptor column, IReadOnlyList<PdfProductCategory> categories)
    {
        if (categories.Count == 0)
        {
            return;
        }

        foreach (var category in categories)
        {
            column.Item().Column(section =>
            {
                section.Spacing(3);
                CategoryBand(section, category.CategoryName);

                section.Item().Column(grid =>
                {
                    grid.Spacing(6);
                    ComposeAdaptiveGrid(grid, category.Products, KeySellingTileGap, KeySellingSingleTileMaxWidth, ComposePriceTile);
                });
            });
        }
    }

    /// <summary>
    /// Renders <paramref name="items"/> as rows of evenly-divided cards, row size chosen by
    /// <see cref="AdaptiveGridLayout"/> for the collection's total count (spec Phase 8B polish Parts
    /// 1/2). A row with two or more items divides the full row width between them via QuestPDF
    /// <c>RelativeItem</c> — no fixed card width, so the cards always use the space actually
    /// available. A row left with exactly one item (whether the whole collection is a singleton, or
    /// it is the trailing remainder of a larger one) is centered and width-capped instead of
    /// stretched edge-to-edge, per the spec's explicit "do not stretch a tiny amount of content into
    /// an awkward giant card" guidance.
    /// </summary>
    private static void ComposeAdaptiveGrid(
        ColumnDescriptor column,
        IReadOnlyList<PdfProductRow> items,
        float gap,
        float singleItemMaxWidth,
        Action<IContainer, PdfProductRow> composeItem)
    {
        var itemsPerRow = AdaptiveGridLayout.ItemsPerRow(items.Count);

        foreach (var row in items.Chunk(itemsPerRow))
        {
            if (row.Length == 1)
            {
                column.Item().AlignCenter().MaxWidth(singleItemMaxWidth).Element(el => composeItem(el, row[0]));
                continue;
            }

            column.Item().Row(rowDescriptor =>
            {
                for (var i = 0; i < row.Length; i++)
                {
                    if (i > 0)
                    {
                        rowDescriptor.ConstantItem(gap);
                    }

                    rowDescriptor.RelativeItem().Element(el => composeItem(el, row[i]));
                }
            });
        }
    }

    private static void ComposePriceTile(IContainer container, PdfProductRow product) =>
        container.Border(1).BorderColor(HairlineColor).Background(Colors.White).Padding(7).Column(tile =>
        {
            tile.Spacing(1);
            tile.Item().Text(product.DisplayName).FontSize(8.5f).Bold().FontColor(MutedTextColor);
            tile.Item().Text(product.EffectivePriceText ?? "-").FontSize(19).Bold().FontColor(AccentColor);

            if (product.ComparisonPriceText is { } comparison)
            {
                tile.Item().Text(text =>
                {
                    text.Span("was ").FontSize(7.5f).FontColor(ComparePriceColor);
                    text.Span(comparison).FontSize(7.5f).FontColor(ComparePriceColor).Strikethrough();
                });
            }
            else if (product.Qualifier is { } qualifier)
            {
                tile.Item().Text(qualifier).FontSize(7.5f).FontColor(ComparePriceColor);
            }
        });

    /// <summary>
    /// Level 3/4 (spec Part 4): Promotions render as compact callout cards, distinct from ordinary
    /// reference products. The current price is the strongest element on the card; any comparison
    /// price is visibly de-emphasized (muted, struck through), and a source-backed qualifier is
    /// preserved alongside — never fabricated, never dropped just because a comparison price also
    /// exists (spec's "preserve qualifiers when the model contains legitimate qualifier information").
    /// </summary>
    private static void ComposePromotionalSection(ColumnDescriptor column, IReadOnlyList<PdfProductCategory> categories)
    {
        if (categories.Count == 0)
        {
            return;
        }

        foreach (var category in categories)
        {
            column.Item().Column(section =>
            {
                section.Spacing(3);
                CategoryBand(section, category.CategoryName);

                section.Item().Column(grid =>
                {
                    grid.Spacing(8);
                    ComposeAdaptiveGrid(grid, category.Products, PromotionalCardGap, PromotionalSingleCardMaxWidth, ComposePromotionCard);
                });
            });
        }
    }

    private static void ComposePromotionCard(IContainer container, PdfProductRow product) =>
        container.Border(1.25f).BorderColor(AccentColor).Background(PanelFill).Padding(8).Column(card =>
        {
            card.Spacing(1);
            card.Item().Text(product.DisplayName).FontSize(9.5f).Bold().FontColor(BrandDarkColor);
            card.Item().Text(product.EffectivePriceText ?? "-").FontSize(18).Bold().FontColor(DealPriceColor);

            if (product.ComparisonPriceText is { } comparison)
            {
                card.Item().Text(text =>
                {
                    text.Span("was ").FontSize(8).FontColor(ComparePriceColor);
                    text.Span(comparison).FontSize(8).FontColor(ComparePriceColor).Strikethrough();
                });
            }

            if (product.Qualifier is { } qualifier)
            {
                card.Item().Text(qualifier).FontSize(7.5f).Italic().FontColor(MutedTextColor);
            }
        });

    /// <summary>
    /// Level 5 (spec Part 5/8): Season Passes, Add-Ons, SCarowinds/Haunt, and custom/internal entries
    /// are reference-oriented rather than "remember this now" — rendered as compact single-line rows,
    /// two categories sharing a row where the content allows (spec Part 8's adaptive layout). Pairing
    /// is formed only from whichever reference categories are actually present, so hiding one never
    /// leaves the other's half of the row empty — the remaining category simply takes the full row
    /// width via <c>RelativeItem</c>, and an odd category out does the same on its own row.
    /// </summary>
    private static void ComposeReferenceSection(ColumnDescriptor column, IReadOnlyList<PdfProductCategory> categories)
    {
        if (categories.Count == 0)
        {
            return;
        }

        // A dedicated sub-column with its own (slightly more generous) spacing, distinct from the
        // gap between tiers above — spec Phase 8B polish Part 5's "modest additional separation
        // between rows of reference categories" so consecutive pairs read as clearly separate rows
        // rather than running together.
        column.Item().Column(section =>
        {
            section.Spacing(8);

            foreach (var pair in categories.Chunk(2))
            {
                section.Item().Row(row =>
                {
                    for (var i = 0; i < pair.Length; i++)
                    {
                        if (i > 0)
                        {
                            row.ConstantItem(14);
                        }

                        row.RelativeItem().Element(el => ComposeReferenceColumn(el, pair[i]));
                    }
                });
            }
        });
    }

    private static void ComposeReferenceColumn(IContainer container, PdfProductCategory category) =>
        container.Column(section =>
        {
            section.Spacing(1);
            CategoryBand(section, category.CategoryName);

            foreach (var product in category.Products)
            {
                section.Item().PaddingTop(1).BorderBottom(0.6f).BorderColor(HairlineColor).Row(row =>
                {
                    row.RelativeItem().Text(product.DisplayName).FontSize(8.5f);
                    row.ConstantItem(55).AlignRight().Text(product.EffectivePriceText ?? "-").FontSize(8.5f).Bold();
                });

                if (product.Notes is { } notes)
                {
                    section.Item().Text(notes).FontSize(7.5f).Italic().FontColor(MutedTextColor);
                }

                if (product.Qualifier is { } qualifier)
                {
                    section.Item().Text(qualifier).FontSize(7.5f).FontColor(ComparePriceColor);
                }
            }
        });

    private static void CategoryBand(ColumnDescriptor column, string categoryName) =>
        // A tinted rate-sheet-style band rather than a bare bold label, matching the Desktop shell's
        // category band treatment (AssociateView.axaml "categoryBand").
        column.Item().Background(BandFill).PaddingHorizontal(4).PaddingVertical(2)
            .Text(categoryName.ToUpperInvariant()).Bold().FontSize(8.5f).LetterSpacing(0.05f).FontColor(AccentColor);

    /// <summary>
    /// Level 6 (spec Part 6): Operational Notes get their own clearly bordered panel with a stronger
    /// left accent than an ordinary hairline, so supervisor-provided notes read as distinctly
    /// separated from product/reference content. Disappears entirely when hidden or empty
    /// (spec Part 7) — no empty box.
    /// </summary>
    private static void ComposeNotes(ColumnDescriptor column, IReadOnlyList<string> notes, string heading)
    {
        if (notes.Count == 0)
        {
            return;
        }

        column.Item().Background(PanelFill).BorderLeft(4).BorderColor(BrandDarkColor).Padding(8).Column(section =>
        {
            section.Spacing(3);
            section.Item().Text(heading.ToUpperInvariant()).Bold().FontSize(10).LetterSpacing(0.04f).FontColor(BrandDarkColor);

            foreach (var note in notes)
            {
                // A standard typographic bullet (spec Phase 8B polish Part 4) rather than a raw
                // hyphen — not an emoji, no icon meaning attached to it.
                section.Item().Row(row =>
                {
                    row.ConstantItem(12).Text("•").FontSize(9.5f).Bold().FontColor(AccentColor);
                    row.RelativeItem().Text(note).FontSize(9.5f);
                });
            }
        });
    }

    private static void ComposeFooter(IContainer container, AssociatePassdownDocumentModel model)
    {
        container.PaddingTop(4).BorderTop(1).BorderColor(AccentColor).PaddingTop(4).Row(row =>
        {
            row.AutoItem().AlignMiddle().Row(mark =>
            {
                mark.AutoItem().Width(2).Height(5).Background(AccentColor);
                mark.ConstantItem(1.5f);
                mark.AutoItem().Width(2).Height(8).Background(AccentColor);
                mark.ConstantItem(1.5f);
                mark.AutoItem().Width(2).Height(11).Background(AccentColor);
            });

            row.ConstantItem(6);

            // Spec Phase 8B polish Part 6: no page-number indicator — the Associate Passdown is
            // strictly one page, so "1 of 1" communicates nothing. The now-unused right side of the
            // footer is deliberately left clean rather than filled with anything decorative.
            row.RelativeItem().Text(text =>
            {
                text.Span($"Generated {model.GeneratedAtText}").FontSize(7.5f).FontColor(MutedTextColor);

                if (model.HasUnverifiedProductPricing)
                {
                    text.Span("  -  Some product pricing has not been recently verified.").FontSize(7.5f).FontColor(MutedTextColor);
                }
            });
        });
    }

    private static string FormatCount(int? value) => value?.ToString("N0", CultureInfo.InvariantCulture) ?? "Not entered";
}
