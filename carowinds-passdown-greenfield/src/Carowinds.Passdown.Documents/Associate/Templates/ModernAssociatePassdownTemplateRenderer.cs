using Carowinds.Passdown.Application.Preferences;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Carowinds.Passdown.Documents.Associate.Templates;

/// <summary>
/// Phase 17E's "Modern" template — an editorial/Swiss-style hybrid (spec Part 4), the most
/// deliberately designed of the three: a bold single-line date, an editorial metadata grid (North
/// Gate/Avenue/Attendance/Pre-solds as true columns, not label/value rows), a dedicated Early Ride
/// block, and numbered/accented section treatment ("01 DAILY ADMISSION", ...) running through both the
/// product categories and Operational Notes. Single-column body like Classic (no product pairing — that
/// is Compact's distinguishing trait) via <see cref="SingleColumnProductLayout"/>, restrained navy
/// accents, no sidebar/rail, no shaded cards. Tuned to stay reasonably space-efficient despite the
/// larger type scale and the metadata grid's extra vertical rhythm.
/// </summary>
public sealed class ModernAssociatePassdownTemplateRenderer : IAssociatePassdownTemplateRenderer
{
    public AssociatePassdownTemplate Template => AssociatePassdownTemplate.Modern;

    private const float PageMargin = 28f;
    private const float NumberGutterWidth = 22f;

    private static readonly string InkColor = "#111318";
    private static readonly string AccentColor = "#1E5AA8";
    private static readonly string MutedTextColor = "#5B6470";
    private static readonly string HairlineColor = "#C7CDD6";

    public void Compose(PageDescriptor page, AssociatePassdownDocumentModel model, CancellationToken cancellationToken)
    {
        page.Size(PageSizes.Letter);
        page.Margin(PageMargin);
        page.DefaultTextStyle(x => x.FontSize(9.25f).FontColor(Colors.Black));

        page.Header().Element(header => ComposeHeader(header, model));

        page.Content().PaddingTop(7).Column(column =>
        {
            column.Spacing(7);

            ComposeSummaryGrid(column, model);
            ComposeDailyFeature(column, model);

            cancellationToken.ThrowIfCancellationRequested();

            var categoryCount = SingleColumnProductLayout.Compose(column, model.ProductCategories, 6f, ComposeCategory);
            ComposeNotes(column, model.OperationalNotes, categoryCount + 1);
        });

        page.Footer().Element(footer => ComposeFooter(footer, model));
    }

    /// <summary>A large, bold, letter-spaced date under a crisp accent rule is Modern's strongest single element (spec Part 4/19) — no filled band, no app-branding line above it.</summary>
    private static void ComposeHeader(IContainer container, AssociatePassdownDocumentModel model) =>
        container.Column(column =>
        {
            column.Item().Text(model.OperatingDateText.ToUpperInvariant()).FontSize(23).Bold().LetterSpacing(0.015f).FontColor(InkColor);
            column.Item().PaddingTop(6).Height(2.25f).Background(AccentColor);
        });

    /// <summary>The numbered/accented section marker (spec Part 4/9/10) — a small gutter number beside an uppercase accent label, shared by product categories and Operational Notes so the same visual system runs through both.</summary>
    private static void NumberedSectionLabel(ColumnDescriptor column, int number, string text) =>
        column.Item().Row(row =>
        {
            row.ConstantItem(NumberGutterWidth).Text(number.ToString("00")).Bold().FontSize(9).FontColor(AccentColor);
            row.RelativeItem().Column(heading =>
            {
                heading.Item().Text(text.ToUpperInvariant()).Bold().FontSize(8.75f).LetterSpacing(0.09f).FontColor(InkColor);
                heading.Item().PaddingTop(1.5f).Height(0.75f).Background(HairlineColor);
            });
        });

    /// <summary>Modern's editorial metadata grid (spec Part 8: "editorial metadata row/grid") — Park Hours headline as its own line, then North Gate/Avenue/Attendance/Pre-solds as four true columns (label above value), then a dedicated Early Ride block. Genuinely different composition from Classic's vertical list and Compact's inline strip, not merely a restyled copy of either.</summary>
    private static void ComposeSummaryGrid(ColumnDescriptor column, AssociatePassdownDocumentModel model)
    {
        column.Item().Column(grid =>
        {
            grid.Spacing(6f);

            grid.Item().Text(text =>
            {
                text.Span(model.ParkHours.HeadlineText).FontSize(15).Bold().FontColor(InkColor);

                if (model.ParkHours.BadgeText is { } badge)
                {
                    text.Span($"   ({badge})").FontSize(8).Italic().FontColor(MutedTextColor);
                }
            });

            if (model.ParkHours.PrivateBuyoutDetails is { } details)
            {
                grid.Item().Text($"Buyout details: {details}").FontSize(8).FontColor(MutedTextColor);
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
                early.Spacing(1.5f);
                early.Item().Text("EARLY RIDE").FontSize(7.5f).Bold().LetterSpacing(0.08f).FontColor(MutedTextColor);
                early.Item().Text(PdfFormatting.ErtLineText(model.EarlyRideTime.Kind, model.EarlyRideTime.Rides, model.OperatingDate)).FontSize(11).Bold().FontColor(InkColor);
            });
        });
    }

    private static void MetadataColumn(RowDescriptor row, string label, string value) =>
        row.RelativeItem().Column(col =>
        {
            col.Spacing(1.5f);
            col.Item().Text(label).FontSize(7.25f).Bold().LetterSpacing(0.06f).FontColor(MutedTextColor);
            col.Item().Text(value).FontSize(10.5f).Bold().FontColor(InkColor);
        });

    /// <summary>Mandatory content (spec §15), always present — a stronger editorial heading than Classic/Compact but still unnumbered (the numbered system starts at the product categories), no shaded card.</summary>
    private static void ComposeDailyFeature(ColumnDescriptor column, AssociatePassdownDocumentModel model)
    {
        column.Item().ShowEntire().Column(section =>
        {
            section.Spacing(2.5f);
            section.Item().Text((model.DailyFeature.TypeLabel ?? "Daily Feature").ToUpperInvariant()).Bold().FontSize(9.5f).LetterSpacing(0.1f).FontColor(AccentColor);
            section.Item().Height(0.75f).Background(HairlineColor);

            if (model.DailyFeature.Text is { } featureText)
            {
                section.Item().PaddingTop(1).Text(featureText).FontSize(10).Italic();
            }
            else
            {
                section.Item().PaddingTop(1).Text("Not entered.").FontSize(10).Italic().FontColor(MutedTextColor);
            }
        });
    }

    private static void ComposeCategory(IContainer container, PdfProductCategory category, int number) =>
        container.Column(section =>
        {
            section.Spacing(2);
            NumberedSectionLabel(section, number, category.CategoryName);

            foreach (var product in category.Products)
            {
                section.Item().PaddingLeft(NumberGutterWidth).PaddingTop(2).BorderBottom(0.6f).BorderColor(HairlineColor).PaddingBottom(2).Row(row =>
                {
                    row.RelativeItem().Text(product.DisplayName).FontSize(9.25f).Bold();
                    row.ConstantItem(68).AlignRight().Text(product.EffectivePriceText ?? "-").FontSize(9.25f).Bold().FontColor(AccentColor);
                });

                if (product.Notes is { } notes)
                {
                    section.Item().PaddingLeft(NumberGutterWidth).Text(notes).FontSize(7.75f).Italic().FontColor(MutedTextColor);
                }
            }
        });

    /// <summary>Operational Notes closes the numbered system (spec Part 10) — the same gutter-number treatment as the product categories, with more deliberate spacing than Classic/Compact's plain bullets, never a giant note card.</summary>
    private static void ComposeNotes(ColumnDescriptor column, IReadOnlyList<string> notes, int number)
    {
        if (notes.Count == 0)
        {
            return;
        }

        column.Item().ShowEntire().Column(section =>
        {
            section.Spacing(3f);
            NumberedSectionLabel(section, number, "Operational Notes");

            foreach (var note in notes)
            {
                section.Item().PaddingLeft(NumberGutterWidth).Row(row =>
                {
                    row.ConstantItem(11).Text("•").FontSize(9).Bold().FontColor(AccentColor);
                    row.RelativeItem().Text(note).FontSize(9);
                });
            }
        });
    }

    private static void ComposeFooter(IContainer container, AssociatePassdownDocumentModel model) =>
        container.PaddingTop(4).BorderTop(0.75f).BorderColor(AccentColor).PaddingTop(4).Row(row =>
        {
            row.RelativeItem().Text(model.HasUnverifiedProductPricing ? "Some product pricing has not been recently verified." : string.Empty)
                .FontSize(7.25f).FontColor(MutedTextColor);

            row.ConstantItem(115).AlignRight().Text(model.GeneratedAtText).FontSize(7.25f).FontColor(MutedTextColor);
        });
}
