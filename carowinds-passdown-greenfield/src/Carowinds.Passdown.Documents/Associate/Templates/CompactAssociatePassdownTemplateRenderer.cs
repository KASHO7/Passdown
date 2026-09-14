using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Domain.Ert;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Carowinds.Passdown.Documents.Associate.Templates;

/// <summary>
/// Phase 17C §13's "Compact" template — optimized for information density: minimal borders, reduced
/// whitespace, and every product rendered as one dense text row (name/price/qualifier) regardless of
/// <see cref="PdfProductSectionTier"/>, rather than Classic's price-tile/promo-card treatment, since
/// tiles/cards are the single biggest space cost in the Classic layout. Categories are paired
/// two-per-row throughout (not just the Reference tier) for the same reason. No color-only meaning,
/// no emojis, grayscale/photocopy-safe (spec §14) — thin hairlines and text weight carry all the
/// hierarchy that Classic's tinted fills otherwise carry.
/// </summary>
public sealed class CompactAssociatePassdownTemplateRenderer : IAssociatePassdownTemplateRenderer
{
    public AssociatePassdownTemplate Template => AssociatePassdownTemplate.Compact;

    private const float PageMargin = 22f;

    private static readonly string InkColor = "#000000";
    private static readonly string MutedTextColor = "#4B4B4B";
    private static readonly string HairlineColor = "#8A8A8A";
    private static readonly string ComparePriceColor = "#5C5C5C";

    public void Compose(PageDescriptor page, AssociatePassdownDocumentModel model, CancellationToken cancellationToken)
    {
        page.Size(PageSizes.Letter);
        page.Margin(PageMargin);
        page.DefaultTextStyle(x => x.FontSize(8.75f).FontColor(Colors.Black));

        page.Header().Element(header => ComposeHeader(header, model));

        page.Content().PaddingTop(4).Column(column =>
        {
            column.Spacing(4);

            ComposeSummaryStrip(column, model);
            ComposeDailyFeatureLine(column, model);

            cancellationToken.ThrowIfCancellationRequested();

            ComposeProducts(column, model.ProductCategories);
            ComposeNotes(column, model.OperationalNotes);
        });

        page.Footer().Element(footer => ComposeFooter(footer, model));
    }

    /// <summary>A single compact title line instead of Classic's full-width colored brand band — saves the height a filled band costs while keeping the same identity/date information.</summary>
    private static void ComposeHeader(IContainer container, AssociatePassdownDocumentModel model)
    {
        container.BorderBottom(1.5f).BorderColor(InkColor).PaddingBottom(4).Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text("CAROWINDS OPERATIONS").FontSize(7).Bold().LetterSpacing(0.1f).FontColor(MutedTextColor);
                left.Item().Text("Associate Passdown — Compact").FontSize(14).Bold();
            });

            row.ConstantItem(160).AlignRight().AlignMiddle().Text(model.OperatingDateText).FontSize(11).Bold();
        });
    }

    /// <summary>
    /// Park Hours, Waterpark (when present), Attendance/Pre-Solds, and Early Ride Time as one thin
    /// text strip separated by vertical hairlines rather than four bordered boxes — the single
    /// biggest density win over Classic's three-panel row, since there is no panel padding/border
    /// repeated four times.
    /// </summary>
    private static void ComposeSummaryStrip(ColumnDescriptor column, AssociatePassdownDocumentModel model)
    {
        column.Item().BorderBottom(0.75f).BorderColor(HairlineColor).PaddingBottom(3).Column(strip =>
        {
            strip.Spacing(1.5f);

            strip.Item().Row(row =>
            {
                row.AutoItem().Text("HOURS").FontSize(7).Bold().FontColor(MutedTextColor);
                row.ConstantItem(4);
                row.RelativeItem().Text(text =>
                {
                    text.Span(model.ParkHours.HeadlineText).FontSize(10).Bold();
                    text.Span("   North Gate ").FontSize(8).FontColor(MutedTextColor);
                    text.Span(model.ParkHours.NorthGateText ?? "-").FontSize(8).Bold();
                    text.Span("   Avenue ").FontSize(8).FontColor(MutedTextColor);
                    text.Span(model.ParkHours.AvenueText ?? "-").FontSize(8).Bold();

                    if (model.ParkHours.BadgeText is { } badge)
                    {
                        text.Span($"   ({badge})").FontSize(7.5f).Italic().FontColor(MutedTextColor);
                    }
                });
            });

            if (model.ParkHours.VerificationText is { } verification)
            {
                strip.Item().Text(verification).FontSize(7.5f).FontColor(MutedTextColor);
            }

            if (model.ParkHours.PrivateBuyoutDetails is { } details)
            {
                strip.Item().Text($"Buyout: {details}").FontSize(7.5f);
            }

            strip.Item().Row(row =>
            {
                row.RelativeItem(2).Text(text =>
                {
                    text.Span("ATTENDANCE ").FontSize(7).Bold().FontColor(MutedTextColor);
                    text.Span(PdfFormatting.FormatCount(model.Attendance.ProjectedAttendance)).FontSize(9).Bold();
                    text.Span("   PRE-SOLDS ").FontSize(7).Bold().FontColor(MutedTextColor);
                    text.Span(PdfFormatting.FormatCount(model.Attendance.PreSolds)).FontSize(9).Bold();
                });

                row.RelativeItem(3).Element(el => ComposeErtLine(el, model));
            });
        });
    }

    private static void ComposeErtLine(IContainer container, AssociatePassdownDocumentModel model) =>
        container.Text(text =>
        {
            text.Span("ERT  ").FontSize(7).Bold().FontColor(MutedTextColor);

            switch (model.EarlyRideTime.Kind)
            {
                case EffectiveErtKind.NotConfigured:
                    text.Span(PdfFormatting.NotConfiguredText(model.OperatingDate)).FontSize(8.5f).Italic().FontColor(MutedTextColor);
                    return;

                case EffectiveErtKind.NoErt:
                    text.Span("None today.").FontSize(8.5f).Italic().FontColor(MutedTextColor);
                    return;

                default:
                    text.Span(model.EarlyRideTime.StartTimeText ?? "-").FontSize(9).Bold();

                    if (model.EarlyRideTime.Rides.Count > 0)
                    {
                        text.Span("  " + string.Join(", ", model.EarlyRideTime.Rides)).FontSize(8);
                    }

                    if (model.EarlyRideTime.Source == EffectiveErtSource.Override)
                    {
                        text.Span("  (override)").FontSize(7.5f).Italic().FontColor(MutedTextColor);
                    }

                    return;
            }
        });

    /// <summary>Daily Feature as one paragraph with a bold label prefix — no filled panel, no accent rule; the label alone carries the "this is a distinct feature, not a note" signal.</summary>
    private static void ComposeDailyFeatureLine(ColumnDescriptor column, AssociatePassdownDocumentModel model)
    {
        if (!model.DailyFeature.HasContent)
        {
            return;
        }

        column.Item().Text(text =>
        {
            text.Span((model.DailyFeature.TypeLabel ?? "Daily Feature").ToUpperInvariant() + ":  ")
                .FontSize(8).Bold();

            if (model.DailyFeature.Text is { } featureText)
            {
                text.Span(featureText).FontSize(9).Italic();
            }
        });
    }

    /// <summary>
    /// Every category renders as compact rows (spec §24's "avoid one large card per product"),
    /// regardless of tier — the tier distinction still orders categories (key selling first) but no
    /// longer changes their visual treatment, since tiles/cards are what Classic spends the most
    /// space on. Categories pair two-per-row when both fit, exactly like Classic's Reference tier.
    /// </summary>
    private static void ComposeProducts(ColumnDescriptor column, IReadOnlyList<PdfProductCategory> categories)
    {
        if (categories.Count == 0)
        {
            return;
        }

        var ordered = categories
            .OrderBy(c => c.Tier switch
            {
                PdfProductSectionTier.KeySelling => 0,
                PdfProductSectionTier.Promotional => 1,
                _ => 2,
            })
            .ToList();

        column.Item().Column(section =>
        {
            section.Spacing(3);

            foreach (var pair in ordered.Chunk(2))
            {
                section.Item().Row(row =>
                {
                    for (var i = 0; i < pair.Length; i++)
                    {
                        if (i > 0)
                        {
                            row.ConstantItem(12);
                        }

                        row.RelativeItem().Element(el => ComposeCategoryColumn(el, pair[i]));
                    }
                });
            }
        });
    }

    private static void ComposeCategoryColumn(IContainer container, PdfProductCategory category) =>
        container.Column(col =>
        {
            col.Spacing(0.5f);
            col.Item().Text(category.CategoryName.ToUpperInvariant()).Bold().FontSize(8).LetterSpacing(0.03f);

            foreach (var product in category.Products)
            {
                col.Item().BorderBottom(0.5f).BorderColor(HairlineColor).PaddingVertical(1.5f).Row(row =>
                {
                    row.RelativeItem().Text(text =>
                    {
                        text.Span(product.DisplayName).FontSize(8.25f);

                        if (product.Qualifier is { } qualifier)
                        {
                            text.Span($"  ({qualifier})").FontSize(7).Italic().FontColor(ComparePriceColor);
                        }
                    });

                    row.ConstantItem(70).AlignRight().Text(text =>
                    {
                        text.Span(product.EffectivePriceText ?? "-").FontSize(8.25f).Bold();

                        if (product.ComparisonPriceText is { } comparison)
                        {
                            text.Span($"  was {comparison}").FontSize(6.75f).FontColor(ComparePriceColor).Strikethrough();
                        }
                    });
                });

                if (product.Notes is { } notes)
                {
                    col.Item().Text(notes).FontSize(6.75f).Italic().FontColor(MutedTextColor);
                }
            }
        });

    /// <summary>Bulleted lines under a plain bold heading — no filled panel/accent bar.</summary>
    private static void ComposeNotes(ColumnDescriptor column, IReadOnlyList<string> notes)
    {
        if (notes.Count == 0)
        {
            return;
        }

        column.Item().BorderTop(0.75f).BorderColor(HairlineColor).PaddingTop(3).Column(section =>
        {
            section.Spacing(1.5f);
            section.Item().Text("OPERATIONAL NOTES").Bold().FontSize(8).LetterSpacing(0.03f);

            foreach (var note in notes)
            {
                section.Item().Row(row =>
                {
                    row.ConstantItem(9).Text("-").FontSize(8.5f).Bold();
                    row.RelativeItem().Text(note).FontSize(8.25f);
                });
            }
        });
    }

    private static void ComposeFooter(IContainer container, AssociatePassdownDocumentModel model) =>
        container.PaddingTop(3).BorderTop(0.75f).BorderColor(HairlineColor).PaddingTop(3).Text(text =>
        {
            text.Span($"Generated {model.GeneratedAtText}").FontSize(6.75f).FontColor(MutedTextColor);

            if (model.HasUnverifiedProductPricing)
            {
                text.Span("  -  Some product pricing has not been recently verified.").FontSize(6.75f).FontColor(MutedTextColor);
            }
        });
}
