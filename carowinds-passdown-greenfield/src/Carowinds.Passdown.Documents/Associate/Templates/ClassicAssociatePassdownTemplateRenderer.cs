using Carowinds.Passdown.Application.Preferences;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Carowinds.Passdown.Documents.Associate.Templates;

/// <summary>
/// Phase 17D's "Classic" template — conservative typography, thin gray/blue horizontal rules, medium
/// spacing (spec §18); the closest of the three to the pre-17D passdown's feel, but rebuilt onto the
/// Phase 17D §11 approved master structure like every other template: no app-branding header, no
/// product cards/tiles, no comparison pricing, rides-only Early Ride Time. Previously (Phase 17C) this
/// class was a byte-for-byte extraction of the pre-17C composer; Phase 17D intentionally changes its
/// rendering (spec §18's "no product cards, no app branding header" applies to Classic too — the
/// "closest to prior style" qualifier is about typography/spacing restraint, not an exemption from the
/// corrective layout pass).
/// </summary>
public sealed class ClassicAssociatePassdownTemplateRenderer : IAssociatePassdownTemplateRenderer
{
    public AssociatePassdownTemplate Template => AssociatePassdownTemplate.Classic;

    private const float PageMargin = 32f;
    private const float PairColumnGap = 18f;
    private const float SectionSpacing = 9f;

    private static readonly string InkColor = "#0B2A4A";
    private static readonly string AccentColor = "#1E5AA8";
    private static readonly string MutedTextColor = "#57626D";
    private static readonly string HairlineColor = "#B7C1CC";

    public void Compose(PageDescriptor page, AssociatePassdownDocumentModel model, CancellationToken cancellationToken)
    {
        page.Size(PageSizes.Letter);
        page.Margin(PageMargin);
        page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Black));

        page.Header().Element(header => ComposeHeader(header, model));

        page.Content().PaddingTop(8).Column(column =>
        {
            column.Spacing(SectionSpacing);

            ComposeSummaryGrid(column, model);
            ComposeDailyFeature(column, model);

            cancellationToken.ThrowIfCancellationRequested();

            MasterProductLayout.Compose(column, model.ProductCategories, PairColumnGap, 8f, ComposeCategory);
            ComposeNotes(column, model.OperationalNotes);
        });

        page.Footer().Element(footer => ComposeFooter(footer, model));
    }

    /// <summary>The date is the page's primary heading (spec §7/§11) — no "CAROWINDS OPERATIONS"/"Associate Passdown" branding above it.</summary>
    private static void ComposeHeader(IContainer container, AssociatePassdownDocumentModel model) =>
        container.Column(column =>
        {
            column.Item().Text(model.OperatingDateText.ToUpperInvariant()).FontSize(19).Bold().FontColor(InkColor);
            column.Item().PaddingTop(5).Height(2f).Background(AccentColor);
        });

    private static void SectionHeading(ColumnDescriptor column, string text) =>
        column.Item().Column(heading =>
        {
            heading.Item().Text(text.ToUpperInvariant()).Bold().FontSize(9).LetterSpacing(0.05f).FontColor(AccentColor);
            heading.Item().PaddingTop(2).Height(0.75f).Background(HairlineColor);
        });

    /// <summary>Spec §11's label/value grid — Park Hours, gates, attendance, and Early Ride Time as aligned rows in one panel, never separate cards/columns.</summary>
    private static void ComposeSummaryGrid(ColumnDescriptor column, AssociatePassdownDocumentModel model)
    {
        column.Item().Column(grid =>
        {
            grid.Spacing(4);

            grid.Item().Row(row =>
            {
                row.ConstantItem(95).Text("PARK HOURS").FontSize(8.5f).Bold().FontColor(MutedTextColor);
                row.RelativeItem().Text(text =>
                {
                    text.Span(model.ParkHours.HeadlineText).FontSize(13).Bold();

                    if (model.ParkHours.BadgeText is { } badge)
                    {
                        text.Span($"   ({badge})").FontSize(8.5f).Italic().FontColor(MutedTextColor);
                    }
                });
            });

            grid.Item().Row(row =>
            {
                row.RelativeItem().Row(pair =>
                {
                    pair.ConstantItem(95).Text("North Gate").FontSize(9).FontColor(MutedTextColor);
                    pair.RelativeItem().Text(model.ParkHours.NorthGateText ?? "-").FontSize(9).Bold();
                });
                row.ConstantItem(16);
                row.RelativeItem().Row(pair =>
                {
                    pair.ConstantItem(60).Text("Avenue").FontSize(9).FontColor(MutedTextColor);
                    pair.RelativeItem().Text(model.ParkHours.AvenueText ?? "-").FontSize(9).Bold();
                });
            });

            if (model.ParkHours.PrivateBuyoutDetails is { } details)
            {
                grid.Item().PaddingLeft(95).Text($"Buyout details: {details}").FontSize(8.5f).FontColor(MutedTextColor);
            }

            grid.Item().Row(row =>
            {
                row.RelativeItem().Row(pair =>
                {
                    pair.ConstantItem(95).Text("Attendance").FontSize(9).FontColor(MutedTextColor);
                    pair.RelativeItem().Text(PdfFormatting.FormatCount(model.Attendance.ProjectedAttendance)).FontSize(9).Bold();
                });
                row.ConstantItem(16);
                row.RelativeItem().Row(pair =>
                {
                    pair.ConstantItem(60).Text("Pre-solds").FontSize(9).FontColor(MutedTextColor);
                    pair.RelativeItem().Text(PdfFormatting.FormatCount(model.Attendance.PreSolds)).FontSize(9).Bold();
                });
            });

            grid.Item().Row(row =>
            {
                row.ConstantItem(95).Text("Early Ride").FontSize(9).FontColor(MutedTextColor);
                row.RelativeItem().Text(PdfFormatting.ErtLineText(model.EarlyRideTime.Kind, model.EarlyRideTime.Rides, model.OperatingDate)).FontSize(9).Bold();
            });
        });
    }

    /// <summary>
    /// Mandatory content (spec §15) — always rendered, even with no text entered, so the Daily
    /// Feature type an associate committed to still shows on the page. No shaded card/accent bar; a
    /// plain heading + rule, matching every other section.
    /// </summary>
    private static void ComposeDailyFeature(ColumnDescriptor column, AssociatePassdownDocumentModel model)
    {
        column.Item().ShowEntire().Column(section =>
        {
            section.Spacing(2);
            SectionHeading(section, model.DailyFeature.TypeLabel ?? "Daily Feature");

            if (model.DailyFeature.Text is { } featureText)
            {
                section.Item().PaddingTop(1).Text(featureText).FontSize(10.5f).Italic();
            }
            else
            {
                section.Item().PaddingTop(1).Text("Not entered.").FontSize(10.5f).Italic().FontColor(MutedTextColor);
            }
        });
    }

    /// <summary>One category: heading + rule, then every product as a single aligned row (spec §14 — no cards).</summary>
    private static void ComposeCategory(IContainer container, PdfProductCategory category) =>
        container.Column(section =>
        {
            section.Spacing(1);
            SectionHeading(section, category.CategoryName);

            foreach (var product in category.Products)
            {
                section.Item().PaddingTop(2).BorderBottom(0.5f).BorderColor(HairlineColor).PaddingBottom(2).Row(row =>
                {
                    row.RelativeItem().Text(product.DisplayName).FontSize(9.5f);
                    row.ConstantItem(64).AlignRight().Text(product.EffectivePriceText ?? "-").FontSize(9.5f).Bold();
                });

                if (product.Notes is { } notes)
                {
                    section.Item().Text(notes).FontSize(8).Italic().FontColor(MutedTextColor);
                }
            }
        });

    /// <summary>Bulleted lines under a plain heading — no card, no excessive padding (spec §16), so a few short notes never get pushed to a near-empty page 2.</summary>
    private static void ComposeNotes(ColumnDescriptor column, IReadOnlyList<string> notes)
    {
        if (notes.Count == 0)
        {
            return;
        }

        column.Item().ShowEntire().Column(section =>
        {
            section.Spacing(3);
            SectionHeading(section, "Operational Notes");

            foreach (var note in notes)
            {
                section.Item().Row(row =>
                {
                    row.ConstantItem(12).Text("•").FontSize(9.5f).Bold().FontColor(AccentColor);
                    row.RelativeItem().Text(note).FontSize(9.5f);
                });
            }
        });
    }

    private static void ComposeFooter(IContainer container, AssociatePassdownDocumentModel model) =>
        container.PaddingTop(4).BorderTop(0.75f).BorderColor(HairlineColor).PaddingTop(4).Row(row =>
        {
            row.RelativeItem().Text(model.HasUnverifiedProductPricing ? "Some product pricing has not been recently verified." : string.Empty)
                .FontSize(7.5f).FontColor(MutedTextColor);

            row.ConstantItem(120).AlignRight().Text(model.GeneratedAtText).FontSize(7.5f).FontColor(MutedTextColor);
        });
}
