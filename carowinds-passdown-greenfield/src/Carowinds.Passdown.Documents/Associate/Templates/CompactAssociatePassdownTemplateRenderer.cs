using Carowinds.Passdown.Application.Preferences;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Carowinds.Passdown.Documents.Associate.Templates;

/// <summary>
/// Phase 17D's "Compact" template — maximum information density (spec §18): the smallest safe
/// vertical spacing and typography, minimal decoration, every product a tight one-line row. Follows
/// the same Phase 17D §11 master structure as Classic/Modern (spec §18 — "same master structure"), so
/// only spacing/type-scale differ here, not content order or the product pairing rule.
/// </summary>
public sealed class CompactAssociatePassdownTemplateRenderer : IAssociatePassdownTemplateRenderer
{
    public AssociatePassdownTemplate Template => AssociatePassdownTemplate.Compact;

    private const float PageMargin = 22f;
    private const float PairColumnGap = 12f;

    private static readonly string InkColor = "#000000";
    private static readonly string MutedTextColor = "#4B4B4B";
    private static readonly string HairlineColor = "#8A8A8A";

    public void Compose(PageDescriptor page, AssociatePassdownDocumentModel model, CancellationToken cancellationToken)
    {
        page.Size(PageSizes.Letter);
        page.Margin(PageMargin);
        page.DefaultTextStyle(x => x.FontSize(8.5f).FontColor(Colors.Black));

        page.Header().Element(header => ComposeHeader(header, model));

        page.Content().PaddingTop(4).Column(column =>
        {
            column.Spacing(4);

            ComposeSummaryStrip(column, model);
            ComposeDailyFeatureLine(column, model);

            cancellationToken.ThrowIfCancellationRequested();

            MasterProductLayout.Compose(column, model.ProductCategories, PairColumnGap, 3f, ComposeCategory);
            ComposeNotes(column, model.OperationalNotes);
        });

        page.Footer().Element(footer => ComposeFooter(footer, model));
    }

    /// <summary>The date alone, as densely as the page allows — no app branding, no title line.</summary>
    private static void ComposeHeader(IContainer container, AssociatePassdownDocumentModel model) =>
        container.BorderBottom(1.25f).BorderColor(InkColor).PaddingBottom(3)
            .Text(model.OperatingDateText.ToUpperInvariant()).FontSize(14).Bold();

    /// <summary>Park Hours/gates/attendance/Early Ride as one thin text strip — the same content the other templates show as a grid, compressed to the fewest lines that stay legible.</summary>
    private static void ComposeSummaryStrip(ColumnDescriptor column, AssociatePassdownDocumentModel model)
    {
        column.Item().BorderBottom(0.75f).BorderColor(HairlineColor).PaddingBottom(2.5f).Column(strip =>
        {
            strip.Spacing(1.25f);

            strip.Item().Row(row =>
            {
                row.AutoItem().Text("HOURS").FontSize(6.75f).Bold().FontColor(MutedTextColor);
                row.ConstantItem(4);
                row.RelativeItem().Text(text =>
                {
                    text.Span(model.ParkHours.HeadlineText).FontSize(9.5f).Bold();
                    text.Span("   North Gate ").FontSize(7.5f).FontColor(MutedTextColor);
                    text.Span(model.ParkHours.NorthGateText ?? "-").FontSize(7.5f).Bold();
                    text.Span("   Avenue ").FontSize(7.5f).FontColor(MutedTextColor);
                    text.Span(model.ParkHours.AvenueText ?? "-").FontSize(7.5f).Bold();

                    if (model.ParkHours.BadgeText is { } badge)
                    {
                        text.Span($"   ({badge})").FontSize(7f).Italic().FontColor(MutedTextColor);
                    }
                });
            });

            if (model.ParkHours.PrivateBuyoutDetails is { } details)
            {
                strip.Item().Text($"Buyout: {details}").FontSize(7f);
            }

            strip.Item().Row(row =>
            {
                row.RelativeItem(2).Text(text =>
                {
                    text.Span("ATTENDANCE ").FontSize(6.75f).Bold().FontColor(MutedTextColor);
                    text.Span(PdfFormatting.FormatCount(model.Attendance.ProjectedAttendance)).FontSize(8.5f).Bold();
                    text.Span("   PRE-SOLDS ").FontSize(6.75f).Bold().FontColor(MutedTextColor);
                    text.Span(PdfFormatting.FormatCount(model.Attendance.PreSolds)).FontSize(8.5f).Bold();
                });

                row.RelativeItem(3).Text(text =>
                {
                    text.Span("EARLY RIDE  ").FontSize(6.75f).Bold().FontColor(MutedTextColor);
                    text.Span(PdfFormatting.ErtLineText(model.EarlyRideTime.Kind, model.EarlyRideTime.Rides, model.OperatingDate)).FontSize(8.5f).Bold();
                });
            });
        });
    }

    /// <summary>Mandatory content (spec §15), always present — a bold label prefix, no filled panel.</summary>
    private static void ComposeDailyFeatureLine(ColumnDescriptor column, AssociatePassdownDocumentModel model) =>
        column.Item().Text(text =>
        {
            text.Span((model.DailyFeature.TypeLabel ?? "Daily Feature").ToUpperInvariant() + ":  ").FontSize(7.5f).Bold();
            text.Span(model.DailyFeature.Text ?? "Not entered.").FontSize(8.5f).Italic();
        });

    private static void ComposeCategory(IContainer container, PdfProductCategory category) =>
        container.Column(col =>
        {
            col.Spacing(0.5f);
            col.Item().Text(category.CategoryName.ToUpperInvariant()).Bold().FontSize(7.5f).LetterSpacing(0.02f);

            foreach (var product in category.Products)
            {
                col.Item().BorderBottom(0.5f).BorderColor(HairlineColor).PaddingVertical(1.25f).Row(row =>
                {
                    row.RelativeItem().Text(product.DisplayName).FontSize(8f);
                    row.ConstantItem(58).AlignRight().Text(product.EffectivePriceText ?? "-").FontSize(8f).Bold();
                });

                if (product.Notes is { } notes)
                {
                    col.Item().Text(notes).FontSize(6.5f).Italic().FontColor(MutedTextColor);
                }
            }
        });

    /// <summary>Bulleted lines under a plain bold heading — sized to never strand a short notes list on a near-empty page 2 (spec §16/§17).</summary>
    private static void ComposeNotes(ColumnDescriptor column, IReadOnlyList<string> notes)
    {
        if (notes.Count == 0)
        {
            return;
        }

        column.Item().ShowEntire().BorderTop(0.75f).BorderColor(HairlineColor).PaddingTop(2.5f).Column(section =>
        {
            section.Spacing(1.25f);
            section.Item().Text("OPERATIONAL NOTES").Bold().FontSize(7.5f).LetterSpacing(0.02f);

            foreach (var note in notes)
            {
                section.Item().Row(row =>
                {
                    row.ConstantItem(8).Text("-").FontSize(8f).Bold();
                    row.RelativeItem().Text(note).FontSize(8f);
                });
            }
        });
    }

    private static void ComposeFooter(IContainer container, AssociatePassdownDocumentModel model) =>
        container.PaddingTop(2.5f).BorderTop(0.75f).BorderColor(HairlineColor).PaddingTop(2.5f).Row(row =>
        {
            row.RelativeItem().Text(model.HasUnverifiedProductPricing ? "Some product pricing has not been recently verified." : string.Empty)
                .FontSize(6.5f).FontColor(MutedTextColor);

            row.ConstantItem(110).AlignRight().Text(model.GeneratedAtText).FontSize(6.5f).FontColor(MutedTextColor);
        });
}
