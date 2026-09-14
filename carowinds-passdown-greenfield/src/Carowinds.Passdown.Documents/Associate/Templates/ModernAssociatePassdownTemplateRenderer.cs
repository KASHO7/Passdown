using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Domain.Ert;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Carowinds.Passdown.Documents.Associate.Templates;

/// <summary>
/// Phase 17C §13's "Modern" template — a distinct professional section composition from Classic
/// (spec §13: "different section composition"), not merely a recolor: a two-column body with a
/// narrow left rail (Park Hours/Attendance/ERT/Daily Feature — the "what's happening today" facts)
/// and a wide right column for products, rather than Classic's stacked full-width sections. Section
/// labels use a left accent rule instead of a filled band, and the header is a slim rule rather than
/// a full-bleed colored band — still operational, not decorative (spec §13's "never a web dashboard"),
/// and grayscale/photocopy-safe (spec §14).
/// </summary>
public sealed class ModernAssociatePassdownTemplateRenderer : IAssociatePassdownTemplateRenderer
{
    public AssociatePassdownTemplate Template => AssociatePassdownTemplate.Modern;

    private const float PageMargin = 30f;
    private const float RailWidth = 165f;

    private static readonly string InkColor = "#111318";
    private static readonly string AccentColor = "#1E5AA8";
    private static readonly string MutedTextColor = "#5B6470";
    private static readonly string HairlineColor = "#C7CDD6";
    private static readonly string ComparePriceColor = "#5C6673";

    public void Compose(PageDescriptor page, AssociatePassdownDocumentModel model, CancellationToken cancellationToken)
    {
        page.Size(PageSizes.Letter);
        page.Margin(PageMargin);
        page.DefaultTextStyle(x => x.FontSize(9.5f).FontColor(Colors.Black));

        page.Header().Element(header => ComposeHeader(header, model));

        page.Content().PaddingTop(10).Row(row =>
        {
            row.ConstantItem(RailWidth).Element(rail => ComposeRail(rail, model, cancellationToken));
            row.ConstantItem(16);
            row.RelativeItem().Element(main => ComposeMain(main, model, cancellationToken));
        });

        page.Footer().Element(footer => ComposeFooter(footer, model));
    }

    private static void ComposeHeader(IContainer container, AssociatePassdownDocumentModel model)
    {
        container.BorderBottom(2f).BorderColor(AccentColor).PaddingBottom(8).Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text("CAROWINDS OPERATIONS").FontSize(8).Bold().LetterSpacing(0.16f).FontColor(AccentColor);
                left.Item().PaddingTop(1).Text("Associate Passdown").FontSize(19).Bold().FontColor(InkColor);
            });

            row.ConstantItem(180).AlignRight().AlignMiddle().Text(model.OperatingDateText).FontSize(13).Bold().FontColor(InkColor);
        });
    }

    private static void SectionLabel(ColumnDescriptor column, string text) =>
        column.Item().BorderLeft(2.5f).BorderColor(AccentColor).PaddingLeft(6)
            .Text(text.ToUpperInvariant()).Bold().FontSize(8.5f).LetterSpacing(0.08f).FontColor(AccentColor);

    /// <summary>The narrow left rail: every "what's happening today" fact, top to bottom, in reading-priority order — a genuinely different composition from Classic's horizontal summary row.</summary>
    private static void ComposeRail(IContainer container, AssociatePassdownDocumentModel model, CancellationToken cancellationToken) =>
        container.Column(rail =>
        {
            rail.Spacing(10);

            rail.Item().Column(section =>
            {
                section.Spacing(3);
                SectionLabel(section, "Park Hours");
                section.Item().Text(model.ParkHours.HeadlineText).FontSize(15).Bold();

                if (model.ParkHours.BadgeText is { } badge)
                {
                    section.Item().Text(badge).FontSize(8).Italic().FontColor(MutedTextColor);
                }

                section.Item().Text(text =>
                {
                    text.Span("North Gate ").FontSize(8.5f).FontColor(MutedTextColor);
                    text.Span(model.ParkHours.NorthGateText ?? "-").FontSize(8.5f).Bold();
                });
                section.Item().Text(text =>
                {
                    text.Span("Avenue ").FontSize(8.5f).FontColor(MutedTextColor);
                    text.Span(model.ParkHours.AvenueText ?? "-").FontSize(8.5f).Bold();
                });

                if (model.ParkHours.VerificationText is { } verification)
                {
                    section.Item().Text(verification).FontSize(7.5f).FontColor(MutedTextColor);
                }

                if (model.ParkHours.PrivateBuyoutDetails is { } details)
                {
                    section.Item().Text($"Buyout: {details}").FontSize(8);
                }
            });

            rail.Item().Column(section =>
            {
                section.Spacing(3);
                SectionLabel(section, "Attendance");
                section.Item().Text(text =>
                {
                    text.Span("Projected ").FontSize(8.5f).FontColor(MutedTextColor);
                    text.Span(PdfFormatting.FormatCount(model.Attendance.ProjectedAttendance)).FontSize(12).Bold();
                });
                section.Item().Text(text =>
                {
                    text.Span("Pre-solds ").FontSize(8.5f).FontColor(MutedTextColor);
                    text.Span(PdfFormatting.FormatCount(model.Attendance.PreSolds)).FontSize(12).Bold();
                });
            });

            rail.Item().Column(section =>
            {
                section.Spacing(3);
                SectionLabel(section, "Early Ride Time");
                ComposeErtBody(section, model);
            });

            rail.Item().Column(section =>
            {
                section.Spacing(3);
                SectionLabel(section, model.DailyFeature.TypeLabel ?? "Daily Feature");

                if (!model.DailyFeature.HasContent)
                {
                    section.Item().Text("Not entered.").FontSize(9).Italic().FontColor(MutedTextColor);
                }
                else if (model.DailyFeature.Text is { } text)
                {
                    section.Item().Text(text).FontSize(10).Italic();
                }
            });

            cancellationToken.ThrowIfCancellationRequested();
        });

    private static void ComposeErtBody(ColumnDescriptor column, AssociatePassdownDocumentModel model)
    {
        switch (model.EarlyRideTime.Kind)
        {
            case EffectiveErtKind.NotConfigured:
                column.Item().Text(PdfFormatting.NotConfiguredText(model.OperatingDate)).FontSize(8.5f).Italic().FontColor(MutedTextColor);
                return;

            case EffectiveErtKind.NoErt:
                column.Item().Text("No Early Ride Time today.").FontSize(8.5f).Italic().FontColor(MutedTextColor);
                if (model.EarlyRideTime.Note is { } noErtNote)
                {
                    column.Item().Text(noErtNote).FontSize(8);
                }
                return;

            default:
                column.Item().Text(model.EarlyRideTime.StartTimeText ?? "-").FontSize(12).Bold();

                if (model.EarlyRideTime.Rides.Count > 0)
                {
                    column.Item().Text(string.Join(", ", model.EarlyRideTime.Rides)).FontSize(8.5f);
                }

                if (model.EarlyRideTime.Source == EffectiveErtSource.Override)
                {
                    column.Item().Text("Date override").FontSize(7.5f).Italic().FontColor(MutedTextColor);
                }

                if (model.EarlyRideTime.Note is { } note)
                {
                    column.Item().Text(note).FontSize(8);
                }

                return;
        }
    }

    /// <summary>The wide right column: products, then Operational Notes — the content an associate scans repeatedly through the shift.</summary>
    private static void ComposeMain(IContainer container, AssociatePassdownDocumentModel model, CancellationToken cancellationToken) =>
        container.Column(main =>
        {
            main.Spacing(10);

            ComposeProducts(main, model.ProductCategories);
            cancellationToken.ThrowIfCancellationRequested();
            ComposeNotes(main, model.OperationalNotes);
        });

    private static void ComposeProducts(ColumnDescriptor column, IReadOnlyList<PdfProductCategory> categories)
    {
        if (categories.Count == 0)
        {
            return;
        }

        column.Item().Column(section =>
        {
            section.Spacing(9);

            foreach (var category in categories)
            {
                section.Item().Column(cat =>
                {
                    cat.Spacing(2);
                    SectionLabel(cat, category.CategoryName);

                    foreach (var product in category.Products)
                    {
                        cat.Item().BorderBottom(0.6f).BorderColor(HairlineColor).PaddingVertical(2).Row(row =>
                        {
                            row.RelativeItem().Column(left =>
                            {
                                left.Item().Text(product.DisplayName).FontSize(9.5f).Bold();

                                if (product.Qualifier is { } qualifier)
                                {
                                    left.Item().Text(qualifier).FontSize(7.5f).Italic().FontColor(ComparePriceColor);
                                }

                                if (product.Notes is { } notes)
                                {
                                    left.Item().Text(notes).FontSize(7.5f).Italic().FontColor(MutedTextColor);
                                }
                            });

                            row.ConstantItem(110).AlignRight().Column(right =>
                            {
                                right.Item().AlignRight().Text(product.EffectivePriceText ?? "-").FontSize(12).Bold().FontColor(AccentColor);

                                if (product.ComparisonPriceText is { } comparison)
                                {
                                    right.Item().AlignRight().Text(text =>
                                    {
                                        text.Span("was ").FontSize(7.5f).FontColor(ComparePriceColor);
                                        text.Span(comparison).FontSize(7.5f).FontColor(ComparePriceColor).Strikethrough();
                                    });
                                }
                            });
                        });
                    }
                });
            }
        });
    }

    private static void ComposeNotes(ColumnDescriptor column, IReadOnlyList<string> notes)
    {
        if (notes.Count == 0)
        {
            return;
        }

        column.Item().Column(section =>
        {
            section.Spacing(3);
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
        container.PaddingTop(4).BorderTop(1).BorderColor(HairlineColor).PaddingTop(4).Text(text =>
        {
            text.Span($"Generated {model.GeneratedAtText}").FontSize(7.5f).FontColor(MutedTextColor);

            if (model.HasUnverifiedProductPricing)
            {
                text.Span("  -  Some product pricing has not been recently verified.").FontSize(7.5f).FontColor(MutedTextColor);
            }
        });
}
