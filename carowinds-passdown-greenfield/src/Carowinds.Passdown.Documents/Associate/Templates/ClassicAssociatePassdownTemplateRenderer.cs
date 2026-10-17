using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Documents.DailyFeatureMath;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Carowinds.Passdown.Documents.Associate.Templates;

/// <summary>
/// The "Classic" template — a traditional, professional operations handout (Phase 17E composition,
/// Phase 17F visual refinement). One-line date over a full-width rule; a fully vertical Park Hours/
/// gates/attendance/Early Ride list; every section introduced by a plain uppercase heading over a
/// full-width thin rule; single-column product rows (no pairing — that is Compact's trait) with a
/// strict right-aligned price column. Conservative near-black typography with moderate whitespace so
/// the sheet stays easy to annotate by hand and reads the same printed in grayscale. No cards, no
/// shaded boxes, no app branding, no comparison pricing, rides-only Early Ride.
/// </summary>
public sealed class ClassicAssociatePassdownTemplateRenderer : IAssociatePassdownTemplateRenderer
{
    public AssociatePassdownTemplate Template => AssociatePassdownTemplate.Classic;

    private const float PageMargin = 34f;
    private const float PriceColumnWidth = 72f;
    private const float BodySize = 9.5f;

    private static readonly string InkColor = "#111111";
    private static readonly string MutedTextColor = "#555555";
    private static readonly string RuleColor = "#8C8C8C";

    /// <summary>Vertical rhythm only — <see cref="PdfDensity.Tight"/> trims spacing so a just-overflowing day can recover a page; Classic still favors readability, so type sizes never change.</summary>
    private sealed record Metrics(float ContentTop, float SectionSpacing, float CommercialGap, float SummarySpacing, float HeadingGap, float RowPadding, float NoteSpacingFew, float NoteSpacingMany)
    {
        public static readonly Metrics Standard = new(9f, 11f, 2f, 2.5f, 2.5f, 1.75f, 4f, 2f);
        public static readonly Metrics Tight = new(6f, 7f, 0f, 1.75f, 1.5f, 1.1f, 2.5f, 1.25f);

        public static Metrics For(PdfDensity density) => density == PdfDensity.Tight ? Tight : Standard;
    }

    public void Compose(PageDescriptor page, AssociatePassdownDocumentModel model, PdfDensity density, CancellationToken cancellationToken)
    {
        var m = Metrics.For(density);

        page.Size(PageSizes.Letter);
        page.Margin(PageMargin);
        page.DefaultTextStyle(x => x.FontSize(BodySize).FontColor(InkColor));

        page.Header().Element(header => ComposeHeader(header, model));

        page.Content().PaddingTop(m.ContentTop).Column(column =>
        {
            column.Spacing(m.SectionSpacing);

            // Operations information (hours, gates, attendance, Early Ride, Daily Feature)...
            ComposeSummaryList(column, model, m);
            ComposeDailyFeature(column, model, m);
            ComposeShows(column, model.Shows, m, density);

            cancellationToken.ThrowIfCancellationRequested();

            // ...then the commercial reference, separated by a little extra space rather than a container.
            if (model.ProductCategories.Count > 0 && m.CommercialGap > 0)
            {
                column.Item().Height(m.CommercialGap);
            }

            SingleColumnProductLayout.Compose(column, model.ProductCategories, m.SectionSpacing, (el, category) => ComposeCategory(el, category, m));
            ComposeNotes(column, model.OperationalNotes, m);
        });

        page.Footer().Element(footer => ComposeFooter(footer, model));
    }

    /// <summary>The date on one line is the page's primary heading — no "CAROWINDS OPERATIONS"/"Associate Passdown" branding above it.</summary>
    private static void ComposeHeader(IContainer container, AssociatePassdownDocumentModel model) =>
        container.Column(column =>
        {
            column.Item().Text(model.OperatingDateText.ToUpperInvariant()).FontSize(17).Bold().LetterSpacing(0.01f);
            column.Item().PaddingTop(5).Height(1.25f).Background(InkColor);
        });

    private static void SectionHeading(ColumnDescriptor column, string text, Metrics m) =>
        column.Item().PaddingBottom(m.HeadingGap).Column(heading =>
        {
            heading.Item().Text(text.ToUpperInvariant()).Bold().FontSize(9).LetterSpacing(0.06f);
            heading.Item().PaddingTop(2.5f).Height(0.75f).Background(RuleColor);
        });

    /// <summary>
    /// Classic's top summary — a "PARK HOURS" caption, the hours headline, then North Gate, Avenue,
    /// Attendance, Pre-solds, and Early Ride each on their own aligned label/value row. The fully
    /// vertical list is what reads as a traditional handout (Compact packs the same facts onto an
    /// inline strip; Modern lays them out as a four-column metadata grid).
    /// </summary>
    private static void ComposeSummaryList(ColumnDescriptor column, AssociatePassdownDocumentModel model, Metrics m)
    {
        const float labelWidth = 92f;

        column.Item().Column(list =>
        {
            list.Spacing(m.SummarySpacing);

            list.Item().Text("PARK HOURS").FontSize(9).Bold().LetterSpacing(0.06f);
            list.Item().PaddingTop(1).PaddingBottom(3).Text(text =>
            {
                text.Span(model.ParkHours.HeadlineText).FontSize(14).Bold();

                if (model.ParkHours.BadgeText is { } badge)
                {
                    text.Span($"   ({badge})").FontSize(8.5f).Italic().FontColor(MutedTextColor);
                }
            });

            SummaryRow(list, labelWidth, "North Gate", model.ParkHours.NorthGateText ?? "-");
            SummaryRow(list, labelWidth, "Avenue", model.ParkHours.AvenueText ?? "-");

            if (model.ParkHours.PrivateBuyoutDetails is { } details)
            {
                list.Item().PaddingLeft(labelWidth).Text($"Buyout details: {details}").FontSize(8.5f).FontColor(MutedTextColor);
            }

            SummaryRow(list, labelWidth, "Attendance", PdfFormatting.FormatCount(model.Attendance.ProjectedAttendance));
            SummaryRow(list, labelWidth, "Pre-solds", PdfFormatting.FormatCount(model.Attendance.PreSolds));
            SummaryRow(list, labelWidth, "Early Ride", PdfFormatting.ErtLineText(model.EarlyRideTime.Kind, model.EarlyRideTime.Rides, model.OperatingDate));
        });
    }

    private static void SummaryRow(ColumnDescriptor column, float labelWidth, string label, string value) =>
        column.Item().Row(row =>
        {
            row.ConstantItem(labelWidth).Text(label).FontSize(9.25f).FontColor(MutedTextColor);
            row.RelativeItem().Text(value).FontSize(9.5f).SemiBold();
        });

    /// <summary>The feature-type heading over a full-width rule, then the text. No shaded card. Phase 18F: omitted entirely (no heading, rule, placeholder, or spacing) when no text was entered.</summary>
    private static void ComposeDailyFeature(ColumnDescriptor column, AssociatePassdownDocumentModel model, Metrics m)
    {
        if (!model.DailyFeature.HasContent)
        {
            return;
        }

        column.Item().PreventPageBreak().Column(section =>
        {
            SectionHeading(section, model.DailyFeature.TypeLabel ?? "Daily Feature", m);
            section.Item().Element(body => DailyFeaturePdfText.Compose(body, model.DailyFeature.Text!, new DailyFeaturePdfTextStyle(10, 1.2f, InkColor)));
        });
    }

    /// <summary>
    /// Phase 21C: the selected shows under a plain "SHOWS" heading — one aligned row per show, name
    /// (with its venue, muted, when published) on the left and the start times on the right, wrapping
    /// within their own column. Omitted entirely when nothing is selected or the section is off.
    /// </summary>
    private static void ComposeShows(ColumnDescriptor column, IReadOnlyList<PdfShow> shows, Metrics m, PdfDensity density)
    {
        if (shows.Count == 0)
        {
            return;
        }

        column.Item().KeepSection(shows.Count, headingWithFirstRowsHeight: 70f).Column(section =>
        {
            SectionHeading(section, "Shows", m);

            foreach (var show in shows)
            {
                section.Item().PaddingVertical(m.RowPadding).Row(row =>
                {
                    row.RelativeItem(5).PaddingRight(8).Text(text =>
                    {
                        text.Span(show.Name).FontSize(BodySize).SemiBold();
                        if (show.Venue is { } venue)
                        {
                            text.Span($"  {venue}").FontSize(8.25f).FontColor(MutedTextColor);
                        }
                    });
                    // Tight density (a just-overflowing day) writes AM/PM once per run to recover space.
                    row.RelativeItem(6).AlignRight().Text(density == PdfDensity.Tight ? show.CompactTimesText : show.TimesText).FontSize(BodySize);
                });
            }
        });
    }

    /// <summary>One category: heading + full-width rule, then one aligned name/price row per product (no cards, no per-row rules).</summary>
    private static void ComposeCategory(IContainer container, PdfProductCategory category, Metrics m) =>
        container.KeepSection(category.Products.Count, headingWithFirstRowsHeight: 70f).Column(section =>
        {
            SectionHeading(section, category.CategoryName, m);

            foreach (var product in category.Products)
            {
                section.Item().PaddingVertical(m.RowPadding).Element(row => PdfPrimitives.PriceRow(
                    row,
                    product.DisplayName,
                    product.EffectivePriceText,
                    PriceColumnWidth,
                    name => name.FontSize(BodySize),
                    price => price.FontSize(BodySize).Bold()));

                if (product.Notes is { } notes)
                {
                    section.Item().PaddingRight(PriceColumnWidth).PaddingBottom(1).Text(notes).FontSize(8).Italic().FontColor(MutedTextColor);
                }
            }
        });

    /// <summary>
    /// Readable bullets under a plain heading — never a notes container. Spacing tightens once the
    /// list grows past three notes so a long list compresses before it forces a page break.
    /// </summary>
    private static void ComposeNotes(ColumnDescriptor column, IReadOnlyList<string> notes, Metrics m)
    {
        if (notes.Count == 0)
        {
            return;
        }

        column.Item().KeepSection(notes.Count, headingWithFirstRowsHeight: 70f).Column(section =>
        {
            SectionHeading(section, "Operational Notes", m);

            var noteSpacing = notes.Count <= 3 ? m.NoteSpacingFew : m.NoteSpacingMany;
            foreach (var note in notes)
            {
                section.Item().PaddingBottom(noteSpacing).Row(row =>
                {
                    row.ConstantItem(13).Text("•").FontSize(BodySize).Bold();
                    row.RelativeItem().Text(note).FontSize(BodySize).LineHeight(1.15f);
                });
            }
        });
    }

    private static void ComposeFooter(IContainer container, AssociatePassdownDocumentModel model) =>
        container.PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text(model.HasUnverifiedProductPricing ? "Some product pricing has not been recently verified." : string.Empty)
                .FontSize(7.5f).FontColor(MutedTextColor);

            PdfPrimitives.TimestampAndPageIndicator(row, model.GeneratedAtText, span => span.FontSize(7.5f).FontColor(MutedTextColor));
        });
}
