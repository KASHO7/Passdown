using Carowinds.Passdown.Application.Preferences;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Carowinds.Passdown.Documents.Associate.Templates;

/// <summary>
/// Phase 17E's "Classic" template — a traditional, single-column daily operations handout (spec Part
/// 2): section-by-section flow, no product pairing/matrix (that is Compact's distinguishing trait, not
/// Classic's), conservative gray/blue hairlines, moderate spacing, a fully vertical Park Hours/gates/
/// attendance/Early Ride list rather than a two-per-row grid. No app-branding header, no product
/// cards/tiles, no comparison pricing, rides-only Early Ride Time — the Phase 17D content rules are
/// unchanged, only the composition (Phase 17E's actual scope) moved from the shared paired-row master
/// layout to <see cref="SingleColumnProductLayout"/>.
/// </summary>
public sealed class ClassicAssociatePassdownTemplateRenderer : IAssociatePassdownTemplateRenderer
{
    public AssociatePassdownTemplate Template => AssociatePassdownTemplate.Classic;

    private const float PageMargin = 32f;
    private const float SectionSpacing = 8f;

    private static readonly string InkColor = "#0B2A4A";
    private static readonly string AccentColor = "#1E5AA8";
    private static readonly string MutedTextColor = "#57626D";
    private static readonly string HairlineColor = "#B7C1CC";

    public void Compose(PageDescriptor page, AssociatePassdownDocumentModel model, CancellationToken cancellationToken)
    {
        page.Size(PageSizes.Letter);
        page.Margin(PageMargin);
        page.DefaultTextStyle(x => x.FontSize(9.75f).FontColor(Colors.Black));

        page.Header().Element(header => ComposeHeader(header, model));

        page.Content().PaddingTop(7).Column(column =>
        {
            column.Spacing(SectionSpacing);

            ComposeSummaryList(column, model);
            ComposeDailyFeature(column, model);

            cancellationToken.ThrowIfCancellationRequested();

            SingleColumnProductLayout.Compose(column, model.ProductCategories, 6f, (el, category, _) => ComposeCategory(el, category));
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

    /// <summary>
    /// Classic's top summary (spec Part 8: "vertical label/value arrangement") — Park Hours, gates,
    /// attendance, and Early Ride Time each as their own aligned row, stacked one under another rather
    /// than paired two-per-row. This is what makes Classic read as a traditional handout at a glance:
    /// Compact packs the same facts onto an inline strip, Modern lays them out as a metadata grid.
    /// </summary>
    private static void ComposeSummaryList(ColumnDescriptor column, AssociatePassdownDocumentModel model)
    {
        const float labelWidth = 100f;

        column.Item().Column(list =>
        {
            list.Spacing(3);

            list.Item().Text("PARK HOURS").FontSize(8.5f).Bold().FontColor(MutedTextColor);
            list.Item().PaddingBottom(1).Text(text =>
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
            row.ConstantItem(labelWidth).Text(label).FontSize(9).FontColor(MutedTextColor);
            row.RelativeItem().Text(value).FontSize(9.5f).Bold();
        });

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
                section.Item().PaddingTop(1.5f).BorderBottom(0.5f).BorderColor(HairlineColor).PaddingBottom(1.5f).Row(row =>
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
