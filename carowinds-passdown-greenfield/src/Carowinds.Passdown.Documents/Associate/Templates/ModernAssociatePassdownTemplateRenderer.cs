using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Documents.DailyFeatureMath;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Carowinds.Passdown.Documents.Associate.Templates;

/// <summary>
/// The "Modern" template — a clean, editorially typeset internal operations sheet (Phase 17E
/// composition, Phase 17F visual refinement). Distinguished by typography and whitespace, not
/// ornament: a large two-line date (weekday over month/day/year) above a heavy rule; the hours
/// headline uncaptioned; North Gate/Avenue/Attendance/Pre-solds as a precise four-column metadata
/// grid (small uppercase label above each value); a dedicated Early Ride block; strong uppercase
/// section headings marked by a short accent rule rather than a full-width line; single-column
/// product rows with a right-aligned price column; and Operational Notes as clean spaced paragraphs.
/// Phase 17F removed the Phase 17E "01/02/..." section numbering entirely — no numbers, letters,
/// icons, or badges replace it. No sidebar rail, no dark header band, no cards.
/// </summary>
public sealed class ModernAssociatePassdownTemplateRenderer : IAssociatePassdownTemplateRenderer
{
    public AssociatePassdownTemplate Template => AssociatePassdownTemplate.Modern;

    private const float PageMargin = 36f;
    private const float PriceColumnWidth = 76f;

    /// <summary>
    /// Product sections and notes are set on a narrower editorial measure than the full page width, so
    /// each price sits a readable distance from its name instead of at the far page edge — and the
    /// open right margin is part of what separates Modern from Classic's full-width rows.
    /// </summary>
    private const float ReferenceMeasure = 400f;
    private const float BodySize = 9.5f;

    private static readonly string InkColor = "#14171C";
    private static readonly string AccentColor = "#1E4F8F";
    private static readonly string MutedTextColor = "#5E6670";
    private static readonly string HairlineColor = "#B9BFC7";

    /// <summary>Vertical rhythm only — Modern keeps visibly more air than Compact at <see cref="PdfDensity.Standard"/>; <see cref="PdfDensity.Tight"/> trims it so whitespace never costs a page.</summary>
    private sealed record Metrics(float ContentTop, float SectionSpacing, float CommercialGap, float GridSpacing, float HeadingGap, float RowPadding, float NoteSpacingFew, float NoteSpacingMany)
    {
        public static readonly Metrics Standard = new(11f, 11f, 3f, 8f, 3.5f, 1.6f, 6f, 3.5f);
        public static readonly Metrics Tight = new(7f, 7f, 0f, 5f, 2f, 0.9f, 3.5f, 2f);

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

            // Operations information...
            ComposeSummaryGrid(column, model, m);
            ComposeDailyFeature(column, model);
            ComposeShows(column, model.Shows, m, density);

            cancellationToken.ThrowIfCancellationRequested();

            // ...then the commercial reference after a deliberate extra pause.
            if (model.ProductCategories.Count > 0 && m.CommercialGap > 0)
            {
                column.Item().Height(m.CommercialGap);
            }

            SingleColumnProductLayout.Compose(column, model.ProductCategories, m.SectionSpacing, (el, category) => ComposeCategory(el, category, m));
            ComposeNotes(column, model.OperationalNotes, m);
        });

        page.Footer().Element(footer => ComposeFooter(footer, model));
    }

    /// <summary>
    /// The two-line date is Modern's strongest single element: the weekday as a small, widely spaced
    /// line above a large month/day/year, then a heavy ink rule. Split from the model's own
    /// already-formatted date text (first ", "), so no date formatting is re-decided here; a text with
    /// no comma simply prints on one line.
    /// </summary>
    private static void ComposeHeader(IContainer container, AssociatePassdownDocumentModel model)
    {
        var dateText = model.OperatingDateText.ToUpperInvariant();
        var split = dateText.IndexOf(", ", StringComparison.Ordinal);
        var weekday = split > 0 ? dateText[..split] : null;
        var remainder = split > 0 ? dateText[(split + 2)..] : dateText;

        container.Column(column =>
        {
            if (weekday is not null)
            {
                column.Item().Text(weekday).FontSize(11).Bold().LetterSpacing(0.18f).FontColor(AccentColor);
            }

            column.Item().Text(remainder).FontSize(25).Bold().LetterSpacing(0.01f);
            column.Item().PaddingTop(6).Height(2.5f).Background(InkColor);
        });
    }

    /// <summary>
    /// Modern's section heading: a strong, letter-spaced uppercase label over a short accent rule —
    /// not a full-width line (fewer full-width rules than Classic) and never a number, letter, icon,
    /// or badge.
    /// </summary>
    private static void SectionHeading(ColumnDescriptor column, string text, Metrics m) =>
        column.Item().PaddingBottom(m.HeadingGap).Column(heading =>
        {
            heading.Item().Text(text.ToUpperInvariant()).Bold().FontSize(10).LetterSpacing(0.12f);
            heading.Item().PaddingTop(3).Width(30).Height(2f).Background(AccentColor);
        });

    /// <summary>
    /// Modern's editorial metadata grid: the hours headline on its own (no caption), then North Gate/
    /// Avenue/Attendance/Pre-solds as four equal columns (small uppercase label above a bold value),
    /// then a dedicated Early Ride block.
    /// </summary>
    private static void ComposeSummaryGrid(ColumnDescriptor column, AssociatePassdownDocumentModel model, Metrics m) =>
        column.Item().Column(grid =>
        {
            grid.Spacing(m.GridSpacing);

            grid.Item().Text(text =>
            {
                text.Span(model.ParkHours.HeadlineText).FontSize(17).Bold();

                if (model.ParkHours.BadgeText is { } badge)
                {
                    text.Span($"   {badge}").FontSize(8.5f).Italic().FontColor(MutedTextColor);
                }
            });

            if (model.ParkHours.PrivateBuyoutDetails is { } details)
            {
                grid.Item().Text($"Buyout details: {details}").FontSize(8.5f).FontColor(MutedTextColor);
            }

            grid.Item().Row(row =>
            {
                MetadataColumn(row, "NORTH GATE", model.ParkHours.NorthGateText ?? "-");
                MetadataColumn(row, "AVENUE", model.ParkHours.AvenueText ?? "-");
                MetadataColumn(row, "ATTENDANCE", PdfFormatting.FormatCount(model.Attendance.ProjectedAttendance));
                MetadataColumn(row, "PRE-SOLDS", PdfFormatting.FormatCount(model.Attendance.PreSolds));
            });

            grid.Item().Column(early =>
            {
                early.Spacing(2f);
                MetadataLabel(early.Item(), "EARLY RIDE");
                early.Item().Text(PdfFormatting.ErtLineText(model.EarlyRideTime.Kind, model.EarlyRideTime.Rides, model.OperatingDate)).FontSize(11).Bold();
            });
        });

    private static void MetadataLabel(IContainer container, string label) =>
        container.Text(label).FontSize(7.25f).Bold().LetterSpacing(0.12f).FontColor(MutedTextColor);

    private static void MetadataColumn(RowDescriptor row, string label, string value) =>
        row.RelativeItem().Column(col =>
        {
            col.Spacing(2f);
            MetadataLabel(col.Item(), label);
            col.Item().Text(value).FontSize(11).Bold();
        });

    /// <summary>
    /// The one Modern section that keeps a full-width hairline — it closes the operations block before
    /// the commercial reference begins. Restrained editorial spacing, no shaded card. Phase 18F:
    /// omitted entirely (hairline included) when no text was entered.
    /// </summary>
    private static void ComposeDailyFeature(ColumnDescriptor column, AssociatePassdownDocumentModel model)
    {
        if (!model.DailyFeature.HasContent)
        {
            return;
        }

        column.Item().PreventPageBreak().Column(section =>
        {
            section.Item().Text((model.DailyFeature.TypeLabel ?? "Daily Feature").ToUpperInvariant()).Bold().FontSize(10).LetterSpacing(0.12f);
            section.Item().PaddingTop(3).PaddingBottom(5).Height(0.75f).Background(HairlineColor);
            section.Item().Element(body => DailyFeaturePdfText.Compose(body, model.DailyFeature.Text!, new DailyFeaturePdfTextStyle(10.5f, 1.25f, InkColor)));
        });
    }

    /// <summary>
    /// Phase 21C: the selected shows on the editorial measure under the standard accent heading — show
    /// name in the body weight with its venue as a small muted line beneath, start times right-aligned
    /// in a wrapping column. No numbering, no cards. Omitted entirely when nothing is selected.
    /// </summary>
    private static void ComposeShows(ColumnDescriptor column, IReadOnlyList<PdfShow> shows, Metrics m, PdfDensity density)
    {
        if (shows.Count == 0)
        {
            return;
        }

        column.Item().KeepSection(shows.Count, headingWithFirstRowsHeight: 80f).MaxWidth(ReferenceMeasure + 60f).Column(section =>
        {
            SectionHeading(section, "Shows", m);

            foreach (var show in shows)
            {
                section.Item().PaddingVertical(m.RowPadding).Row(row =>
                {
                    row.RelativeItem(5).PaddingRight(10).Column(name =>
                    {
                        name.Item().Text(show.Name).FontSize(BodySize).SemiBold();
                        if (show.Venue is { } venue)
                        {
                            name.Item().Text(venue).FontSize(7.75f).FontColor(MutedTextColor);
                        }
                    });
                    // Tight density (a just-overflowing day) writes AM/PM once per run to recover space.
                    row.RelativeItem(6).AlignRight().Text(density == PdfDensity.Tight ? show.CompactTimesText : show.TimesText).FontSize(BodySize);
                });
            }
        });
    }

    private static void ComposeCategory(IContainer container, PdfProductCategory category, Metrics m) =>
        container.KeepSection(category.Products.Count, headingWithFirstRowsHeight: 80f).MaxWidth(ReferenceMeasure).Column(section =>
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
    /// Operational Notes as clean, spaced paragraphs under the same heading treatment as products — no
    /// bullets, no numbers, no container. More breathing room than Compact; tightens past three notes
    /// so a long list compresses before it forces a page break.
    /// </summary>
    private static void ComposeNotes(ColumnDescriptor column, IReadOnlyList<string> notes, Metrics m)
    {
        if (notes.Count == 0)
        {
            return;
        }

        column.Item().KeepSection(notes.Count, headingWithFirstRowsHeight: 80f).MaxWidth(ReferenceMeasure + 60f).Column(section =>
        {
            SectionHeading(section, "Operational Notes", m);

            var noteSpacing = notes.Count <= 3 ? m.NoteSpacingFew : m.NoteSpacingMany;
            foreach (var note in notes)
            {
                section.Item().PaddingBottom(noteSpacing).Text(note).FontSize(BodySize).LineHeight(1.2f);
            }
        });
    }

    private static void ComposeFooter(IContainer container, AssociatePassdownDocumentModel model) =>
        container.PaddingTop(8).Row(row =>
        {
            row.RelativeItem().Text(model.HasUnverifiedProductPricing ? "Some product pricing has not been recently verified." : string.Empty)
                .FontSize(7.25f).FontColor(MutedTextColor);

            PdfPrimitives.TimestampAndPageIndicator(row, model.GeneratedAtText, span => span.FontSize(7.25f).FontColor(MutedTextColor));
        });
}
