using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Documents.DailyFeatureMath;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Carowinds.Passdown.Documents.Associate.Templates;

/// <summary>
/// The "Compact" template — a purpose-built dense quick reference and the one-page density benchmark
/// (Phase 17E composition, Phase 17F visual refinement). A small date over a heavy ink rule; Park
/// Hours/gates/attendance/Early Ride compressed into a three-line inline strip; Daily Admission/Fast
/// Lane and Promotions/Season Passes paired side by side via <see cref="MasterProductLayout"/> (its
/// defining trait); the smallest safe type and spacing; minimal ornament (one thin rule under each
/// heading, no per-row rules, no cards, no decorative whitespace).
/// </summary>
public sealed class CompactAssociatePassdownTemplateRenderer : IAssociatePassdownTemplateRenderer
{
    public AssociatePassdownTemplate Template => AssociatePassdownTemplate.Compact;

    private const float PageMargin = 24f;
    private const float PairColumnGap = 18f;
    private const float PriceColumnWidth = 52f;
    private const float BodySize = 8.25f;
    private const float LabelSize = 6.75f;

    private static readonly string InkColor = "#000000";
    private static readonly string MutedTextColor = "#4B4B4B";
    private static readonly string RuleColor = "#8A8A8A";

    /// <summary>Vertical rhythm only. Compact's Standard is already the tightest of the three; <see cref="PdfDensity.Tight"/> squeezes the remaining slack before a second page is accepted.</summary>
    private sealed record Metrics(float ContentTop, float SectionSpacing, float SummarySpacing, float HeadingGap, float RowPadding, float NoteSpacingFew, float NoteSpacingMany)
    {
        public static readonly Metrics Standard = new(5f, 7f, 1.5f, 1.5f, 0.9f, 1.75f, 0.75f);
        public static readonly Metrics Tight = new(3f, 4.5f, 0.75f, 1f, 0.4f, 1f, 0.25f);

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

            ComposeSummaryStrip(column, model, m);
            ComposeDailyFeature(column, model);

            cancellationToken.ThrowIfCancellationRequested();

            MasterProductLayout.Compose(column, model.ProductCategories, PairColumnGap, m.SectionSpacing, headingWithFirstRowsHeight: 48f, (el, category) => ComposeCategory(el, category, m));
            ComposeNotes(column, model.OperationalNotes, m);
        });

        page.Footer().Element(footer => ComposeFooter(footer, model));
    }

    /// <summary>The date alone over a heavy rule — no app branding, no title line.</summary>
    private static void ComposeHeader(IContainer container, AssociatePassdownDocumentModel model) =>
        container.Column(column =>
        {
            column.Item().Text(model.OperatingDateText.ToUpperInvariant()).FontSize(13.5f).Bold();
            column.Item().PaddingTop(3).Height(1.75f).Background(InkColor);
        });

    private static void SectionHeading(ColumnDescriptor column, string text, Metrics m) =>
        column.Item().PaddingBottom(m.HeadingGap).Column(heading =>
        {
            heading.Item().Text(text.ToUpperInvariant()).Bold().FontSize(7.5f).LetterSpacing(0.03f);
            heading.Item().PaddingTop(1.5f).Height(0.6f).Background(RuleColor);
        });

    private static void Label(TextDescriptor text, string label) =>
        text.Span(label + "  ").FontSize(LabelSize).Bold().FontColor(MutedTextColor);

    /// <summary>
    /// Park Hours/gates/attendance/Early Ride as a three-line inline strip — the same facts the other
    /// templates spread over a list or grid, compressed to the fewest lines that stay legible. North
    /// Gate/Avenue use the short "NG"/"AVE" labels; every value is printed exactly as the model gives it.
    /// </summary>
    private static void ComposeSummaryStrip(ColumnDescriptor column, AssociatePassdownDocumentModel model, Metrics m) =>
        column.Item().Column(strip =>
        {
            strip.Spacing(m.SummarySpacing);

            strip.Item().Text(text =>
            {
                Label(text, "HOURS");
                text.Span(model.ParkHours.HeadlineText).FontSize(10).Bold();

                if (model.ParkHours.BadgeText is { } badge)
                {
                    text.Span($" ({badge})").FontSize(7f).Italic().FontColor(MutedTextColor);
                }

                text.Span("     ");
                Label(text, "NG");
                text.Span(model.ParkHours.NorthGateText ?? "-").FontSize(BodySize).Bold();
                text.Span("     ");
                Label(text, "AVE");
                text.Span(model.ParkHours.AvenueText ?? "-").FontSize(BodySize).Bold();
            });

            if (model.ParkHours.PrivateBuyoutDetails is { } details)
            {
                strip.Item().Text($"Buyout: {details}").FontSize(7f).FontColor(MutedTextColor);
            }

            strip.Item().Text(text =>
            {
                Label(text, "ATTENDANCE");
                text.Span(PdfFormatting.FormatCount(model.Attendance.ProjectedAttendance)).FontSize(BodySize).Bold();
                text.Span("     ");
                Label(text, "PRE-SOLDS");
                text.Span(PdfFormatting.FormatCount(model.Attendance.PreSolds)).FontSize(BodySize).Bold();
            });

            strip.Item().Text(text =>
            {
                Label(text, "EARLY RIDE");
                text.Span(PdfFormatting.ErtLineText(model.EarlyRideTime.Kind, model.EarlyRideTime.Rides, model.OperatingDate)).FontSize(BodySize).Bold();
            });

            // Phase 21C: selected shows as one more strip line — "Show A (1:00, 3:00 PM)  •  Show B (5:00 PM)" —
            // so Shows costs a line or two, never a section. Absent entirely when nothing is selected.
            if (model.Shows.Count > 0)
            {
                strip.Item().Text(text =>
                {
                    Label(text, "SHOWS");
                    for (var i = 0; i < model.Shows.Count; i++)
                    {
                        if (i > 0)
                        {
                            text.Span("  •  ").FontSize(BodySize).FontColor(MutedTextColor);
                        }

                        text.Span(model.Shows[i].Name).FontSize(BodySize).Bold();
                        text.Span($" ({model.Shows[i].CompactTimesText})").FontSize(BodySize);
                    }
                });
            }
        });

    /// <summary>A tight heading line directly over its body, no rule, no panel. Phase 18F: omitted entirely when no text was entered.</summary>
    private static void ComposeDailyFeature(ColumnDescriptor column, AssociatePassdownDocumentModel model)
    {
        if (!model.DailyFeature.HasContent)
        {
            return;
        }

        column.Item().PreventPageBreak().Column(section =>
        {
            section.Spacing(1f);
            section.Item().Text((model.DailyFeature.TypeLabel ?? "Daily Feature").ToUpperInvariant()).FontSize(7.5f).Bold().LetterSpacing(0.03f);
            section.Item().Element(body => DailyFeaturePdfText.Compose(body, model.DailyFeature.Text!, new DailyFeaturePdfTextStyle(BodySize, null, InkColor)));
        });
    }

    private static void ComposeCategory(IContainer container, PdfProductCategory category, Metrics m) =>
        container.Column(section =>
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
                    section.Item().PaddingRight(PriceColumnWidth).Text(notes).FontSize(6.75f).Italic().FontColor(MutedTextColor);
                }
            }
        });

    /// <summary>Tight bullets under a "NOTES" heading — spacing tightens further past three notes so the list compresses before it can force a second page.</summary>
    private static void ComposeNotes(ColumnDescriptor column, IReadOnlyList<string> notes, Metrics m)
    {
        if (notes.Count == 0)
        {
            return;
        }

        column.Item().KeepSection(notes.Count, headingWithFirstRowsHeight: 48f).Column(section =>
        {
            SectionHeading(section, "Notes", m);

            var noteSpacing = notes.Count <= 3 ? m.NoteSpacingFew : m.NoteSpacingMany;
            foreach (var note in notes)
            {
                section.Item().PaddingBottom(noteSpacing).Row(row =>
                {
                    row.ConstantItem(9).Text("•").FontSize(BodySize).Bold();
                    row.RelativeItem().Text(note).FontSize(BodySize);
                });
            }
        });
    }

    private static void ComposeFooter(IContainer container, AssociatePassdownDocumentModel model) =>
        container.PaddingTop(4).Row(row =>
        {
            row.RelativeItem().Text(model.HasUnverifiedProductPricing ? "Some product pricing has not been recently verified." : string.Empty)
                .FontSize(6.5f).FontColor(MutedTextColor);

            PdfPrimitives.TimestampAndPageIndicator(row, model.GeneratedAtText, span => span.FontSize(6.75f).FontColor(MutedTextColor));
        });
}
