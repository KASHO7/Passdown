using Carowinds.Passdown.Application.Preferences;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Carowinds.Passdown.Documents.Associate.Templates;

/// <summary>
/// Phase 17D's "Modern" template — the strongest typographic hierarchy of the three (spec §18): a
/// large date, restrained navy accents, small uppercase section headings, crisp rules, and intentional
/// whitespace, but on the exact same single-column master structure as Classic/Compact (spec §18's
/// explicit "NO sidebar/rail layout, NO giant dark header, NO decorative cards" — this template no
/// longer uses the Phase 17C two-column rail body, which is precisely the layout that made Modern less
/// pagination-efficient than Classic/Compact with no compensating benefit). Tuned to stay nearly as
/// space-efficient as Compact despite its larger type scale, by keeping spacing tight everywhere except
/// the date/section-label treatment itself.
/// </summary>
public sealed class ModernAssociatePassdownTemplateRenderer : IAssociatePassdownTemplateRenderer
{
    public AssociatePassdownTemplate Template => AssociatePassdownTemplate.Modern;

    private const float PageMargin = 28f;
    private const float PairColumnGap = 16f;

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

        page.Content().PaddingTop(8).Column(column =>
        {
            column.Spacing(8);

            ComposeSummaryGrid(column, model);
            ComposeDailyFeature(column, model);

            cancellationToken.ThrowIfCancellationRequested();

            MasterProductLayout.Compose(column, model.ProductCategories, PairColumnGap, 7f, ComposeCategory);
            ComposeNotes(column, model.OperationalNotes);
        });

        page.Footer().Element(footer => ComposeFooter(footer, model));
    }

    /// <summary>A large date is Modern's strongest single element (spec §18/§7) — a crisp accent rule underneath, no filled band, no app-branding line above it.</summary>
    private static void ComposeHeader(IContainer container, AssociatePassdownDocumentModel model) =>
        container.Column(column =>
        {
            column.Item().Text(model.OperatingDateText.ToUpperInvariant()).FontSize(22).Bold().FontColor(InkColor);
            column.Item().PaddingTop(5).Height(1.5f).Background(AccentColor);
        });

    private static void SectionLabel(ColumnDescriptor column, string text) =>
        column.Item().Column(heading =>
        {
            heading.Item().Text(text.ToUpperInvariant()).Bold().FontSize(8.25f).LetterSpacing(0.09f).FontColor(AccentColor);
            heading.Item().PaddingTop(1.5f).Height(0.75f).Background(HairlineColor);
        });

    private static void ComposeSummaryGrid(ColumnDescriptor column, AssociatePassdownDocumentModel model)
    {
        column.Item().Column(grid =>
        {
            grid.Spacing(3.5f);

            grid.Item().Row(row =>
            {
                row.ConstantItem(90).Text("PARK HOURS").FontSize(8).Bold().FontColor(MutedTextColor);
                row.RelativeItem().Text(text =>
                {
                    text.Span(model.ParkHours.HeadlineText).FontSize(13.5f).Bold().FontColor(InkColor);

                    if (model.ParkHours.BadgeText is { } badge)
                    {
                        text.Span($"   ({badge})").FontSize(8).Italic().FontColor(MutedTextColor);
                    }
                });
            });

            grid.Item().Row(row =>
            {
                row.RelativeItem().Row(pair =>
                {
                    pair.ConstantItem(90).Text("North Gate").FontSize(8.5f).FontColor(MutedTextColor);
                    pair.RelativeItem().Text(model.ParkHours.NorthGateText ?? "-").FontSize(8.5f).Bold();
                });
                row.ConstantItem(14);
                row.RelativeItem().Row(pair =>
                {
                    pair.ConstantItem(55).Text("Avenue").FontSize(8.5f).FontColor(MutedTextColor);
                    pair.RelativeItem().Text(model.ParkHours.AvenueText ?? "-").FontSize(8.5f).Bold();
                });
            });

            if (model.ParkHours.PrivateBuyoutDetails is { } details)
            {
                grid.Item().PaddingLeft(90).Text($"Buyout details: {details}").FontSize(8).FontColor(MutedTextColor);
            }

            grid.Item().Row(row =>
            {
                row.RelativeItem().Row(pair =>
                {
                    pair.ConstantItem(90).Text("Attendance").FontSize(8.5f).FontColor(MutedTextColor);
                    pair.RelativeItem().Text(PdfFormatting.FormatCount(model.Attendance.ProjectedAttendance)).FontSize(8.5f).Bold();
                });
                row.ConstantItem(14);
                row.RelativeItem().Row(pair =>
                {
                    pair.ConstantItem(55).Text("Pre-solds").FontSize(8.5f).FontColor(MutedTextColor);
                    pair.RelativeItem().Text(PdfFormatting.FormatCount(model.Attendance.PreSolds)).FontSize(8.5f).Bold();
                });
            });

            grid.Item().Row(row =>
            {
                row.ConstantItem(90).Text("Early Ride").FontSize(8.5f).FontColor(MutedTextColor);
                row.RelativeItem().Text(PdfFormatting.ErtLineText(model.EarlyRideTime.Kind, model.EarlyRideTime.Rides, model.OperatingDate)).FontSize(8.5f).Bold();
            });
        });
    }

    /// <summary>Mandatory content (spec §15), always present — a left-accent uppercase label, no shaded card.</summary>
    private static void ComposeDailyFeature(ColumnDescriptor column, AssociatePassdownDocumentModel model)
    {
        column.Item().ShowEntire().Column(section =>
        {
            section.Spacing(2);
            SectionLabel(section, model.DailyFeature.TypeLabel ?? "Daily Feature");

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

    private static void ComposeCategory(IContainer container, PdfProductCategory category) =>
        container.Column(section =>
        {
            section.Spacing(2);
            SectionLabel(section, category.CategoryName);

            foreach (var product in category.Products)
            {
                section.Item().PaddingTop(2).BorderBottom(0.6f).BorderColor(HairlineColor).PaddingBottom(2).Row(row =>
                {
                    row.RelativeItem().Text(product.DisplayName).FontSize(9.25f).Bold();
                    row.ConstantItem(68).AlignRight().Text(product.EffectivePriceText ?? "-").FontSize(9.25f).Bold().FontColor(AccentColor);
                });

                if (product.Notes is { } notes)
                {
                    section.Item().Text(notes).FontSize(7.75f).Italic().FontColor(MutedTextColor);
                }
            }
        });

    private static void ComposeNotes(ColumnDescriptor column, IReadOnlyList<string> notes)
    {
        if (notes.Count == 0)
        {
            return;
        }

        column.Item().ShowEntire().Column(section =>
        {
            section.Spacing(2.5f);
            SectionLabel(section, "Operational Notes");

            foreach (var note in notes)
            {
                section.Item().Row(row =>
                {
                    row.ConstantItem(11).Text("•").FontSize(9).Bold().FontColor(AccentColor);
                    row.RelativeItem().Text(note).FontSize(9);
                });
            }
        });
    }

    private static void ComposeFooter(IContainer container, AssociatePassdownDocumentModel model) =>
        container.PaddingTop(4).BorderTop(0.75f).BorderColor(HairlineColor).PaddingTop(4).Row(row =>
        {
            row.RelativeItem().Text(model.HasUnverifiedProductPricing ? "Some product pricing has not been recently verified." : string.Empty)
                .FontSize(7.25f).FontColor(MutedTextColor);

            row.ConstantItem(115).AlignRight().Text(model.GeneratedAtText).FontSize(7.25f).FontColor(MutedTextColor);
        });
}
