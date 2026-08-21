using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Carowinds.Passdown.Documents.Associate;

/// <summary>
/// QuestPDF-backed <see cref="IAssociatePassdownPdfGenerator"/> (spec Part H). No network, database,
/// filesystem, or Desktop dependency — every value it prints comes from the
/// <see cref="AssociatePassdownDocumentModel"/> passed in, so the same model always produces the
/// same bytes. Layout follows spec §11.1/§11.2: US Letter portrait, black/white-safe (text, weight,
/// and hairline borders only — no color-coded meaning), one page for normal content, a labeled
/// "Supporting Details" continuation page for the spec §11.4 overflow case.
/// </summary>
public sealed class AssociatePassdownPdfGenerator : IAssociatePassdownPdfGenerator
{
    private const float PageMargin = 32f;
    private const float BoxSpacing = 6f;

    private readonly IAssociatePassdownPreflightService _preflightService;

    static AssociatePassdownPdfGenerator()
    {
        // QuestPDF requires this to be set once before any document is generated (library
        // requirement, not application state we vary) — see spec Part L's licensing note.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public AssociatePassdownPdfGenerator(IAssociatePassdownPreflightService? preflightService = null)
    {
        _preflightService = preflightService ?? new AssociatePassdownPreflightService();
    }

    public AssociatePassdownPdfResult Generate(AssociatePassdownDocumentModel model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        cancellationToken.ThrowIfCancellationRequested();

        var preflight = _preflightService.Evaluate(model);
        if (!preflight.CanGenerate)
        {
            return new AssociatePassdownPdfResult(AssociatePassdownPdfStatus.ValidationFailed, null, preflight);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var document = Document.Create(container =>
        {
            container.Page(page => ComposePrimaryPage(page, model, preflight, cancellationToken));

            if (preflight.NeedsContinuationPage)
            {
                container.Page(page => ComposeContinuationPage(page, model, preflight, cancellationToken));
            }
        });

        var bytes = document.GeneratePdf();

        var status = preflight.NeedsContinuationPage
            ? AssociatePassdownPdfStatus.GeneratedWithContinuationPage
            : AssociatePassdownPdfStatus.Generated;

        return new AssociatePassdownPdfResult(status, bytes, preflight);
    }

    private static void ComposePrimaryPage(PageDescriptor page, AssociatePassdownDocumentModel model, AssociatePassdownPreflightResult preflight, CancellationToken cancellationToken)
    {
        ConfigurePage(page);
        page.Header().Element(header => ComposeHeader(header, model, isContinuation: false));

        page.Content().PaddingTop(6).Column(column =>
        {
            column.Spacing(BoxSpacing);

            column.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Spacing(BoxSpacing);
                    left.Item().Element(box => ComposeHoursBox(box, model));
                    left.Item().Element(box => ComposeEarlyRideTimeBox(box, model));
                });

                row.ConstantItem(10);

                row.RelativeItem().Column(right =>
                {
                    right.Spacing(BoxSpacing);
                    right.Item().Element(box => ComposeAttendanceBox(box, model));
                    right.Item().Element(box => ComposeDailyFunBox(box, model));
                });
            });

            cancellationToken.ThrowIfCancellationRequested();

            ComposeProducts(column, preflight.PageOneProductCategories, "Products");
            ComposeNotes(column, preflight.PageOneNotes, "Operational Notes");
        });

        page.Footer().Element(footer => ComposeFooter(footer, model));
    }

    private static void ComposeContinuationPage(PageDescriptor page, AssociatePassdownDocumentModel model, AssociatePassdownPreflightResult preflight, CancellationToken cancellationToken)
    {
        ConfigurePage(page);
        page.Header().Element(header => ComposeHeader(header, model, isContinuation: true));

        page.Content().PaddingTop(6).Column(column =>
        {
            column.Spacing(BoxSpacing);
            cancellationToken.ThrowIfCancellationRequested();

            ComposeProducts(column, preflight.ContinuationProductCategories, "Additional Products");
            ComposeNotes(column, preflight.ContinuationNotes, "Additional Operational Notes");
        });

        page.Footer().Element(footer => ComposeFooter(footer, model));
    }

    private static void ConfigurePage(PageDescriptor page)
    {
        page.Size(PageSizes.Letter);
        page.Margin(PageMargin);
        page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Black));
    }

    private static void ComposeHeader(IContainer container, AssociatePassdownDocumentModel model, bool isContinuation)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Text(isContinuation
                        ? "Carowinds Passdown - Associate - Supporting Details"
                        : "Carowinds Passdown - Associate")
                    .Bold().FontSize(15);

                row.ConstantItem(180).AlignRight().Text(model.OperatingDateText).FontSize(11);
            });

            column.Item().PaddingTop(3).BorderBottom(1.5f).BorderColor(Colors.Black);
        });
    }

    private static void ComposeHoursBox(IContainer container, AssociatePassdownDocumentModel model)
    {
        container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Column(column =>
        {
            column.Spacing(2);
            column.Item().Text("PARK HOURS").Bold().FontSize(9.5f);
            column.Item().Text(model.ParkHours.HeadlineText).FontSize(13).Bold();

            if (model.ParkHours.BadgeText is { } badge)
            {
                column.Item().Text(badge).FontSize(8.5f).Italic();
            }

            column.Item().Text($"North Gate {model.ParkHours.NorthGateText ?? "-"}    |    Avenue {model.ParkHours.AvenueText ?? "-"}").FontSize(9);

            if (model.ParkHours.VerificationText is { } verification)
            {
                column.Item().Text(verification).FontSize(8).FontColor(Colors.Grey.Darken2);
            }

            if (model.ParkHours.PrivateBuyoutDetails is { } details)
            {
                column.Item().Text($"Buyout details: {details}").FontSize(8.5f);
            }
        });
    }

    private static void ComposeAttendanceBox(IContainer container, AssociatePassdownDocumentModel model)
    {
        container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Column(column =>
        {
            column.Spacing(2);
            column.Item().Text("ATTENDANCE").Bold().FontSize(9.5f);
            column.Item().Text($"Projected: {FormatCount(model.Attendance.ProjectedAttendance)}").FontSize(10);
            column.Item().Text($"Pre-solds: {FormatCount(model.Attendance.PreSolds)}").FontSize(10);
        });
    }

    private static void ComposeEarlyRideTimeBox(IContainer container, AssociatePassdownDocumentModel model)
    {
        container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Column(column =>
        {
            column.Spacing(2);
            column.Item().Text("EARLY RIDE TIME").Bold().FontSize(9.5f);

            if (!model.EarlyRideTime.Enabled)
            {
                column.Item().Text("Not applicable for this date.").FontSize(9.5f).Italic();
                return;
            }

            if (model.EarlyRideTime.Details is { } details)
            {
                column.Item().Text(details).FontSize(9.5f);
            }

            if (model.EarlyRideTime.ParticipatingAttractions.Count > 0)
            {
                column.Item().Text($"Attractions: {string.Join(", ", model.EarlyRideTime.ParticipatingAttractions)}").FontSize(9);
            }
        });
    }

    private static void ComposeDailyFunBox(IContainer container, AssociatePassdownDocumentModel model)
    {
        container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Column(column =>
        {
            column.Spacing(2);
            column.Item().Text("DAILY FUN").Bold().FontSize(9.5f);

            if (!model.DailyFun.HasContent)
            {
                column.Item().Text("Not entered.").FontSize(9.5f).Italic();
                return;
            }

            if (model.DailyFun.TypeLabel is { } label)
            {
                column.Item().Text(label).FontSize(9).Italic();
            }

            if (model.DailyFun.Text is { } text)
            {
                column.Item().Text(text).FontSize(9.5f);
            }
        });
    }

    // Rows/notes are laid out in their own tightly-spaced inner column, added to the outer content
    // column as a single item — the outer column's spacing is meant to separate major sections
    // (chrome / products / notes), not add a gap before every individual product row or note line.
    private static void ComposeProducts(ColumnDescriptor column, IReadOnlyList<PdfProductCategory> categories, string heading)
    {
        if (categories.Count == 0)
        {
            return;
        }

        column.Item().Column(section =>
        {
            section.Spacing(1);
            SectionHeading(section, heading);

            foreach (var category in categories)
            {
                section.Item().PaddingTop(3).Text(category.CategoryName).Bold().FontSize(9).FontColor(Colors.Grey.Darken3);

                foreach (var product in category.Products)
                {
                    section.Item().Row(row =>
                    {
                        row.RelativeItem(3).Column(nameColumn =>
                        {
                            nameColumn.Item().Text(product.DisplayName).FontSize(9.5f);
                            if (product.Notes is { } notes)
                            {
                                nameColumn.Item().Text(notes).FontSize(8).Italic().FontColor(Colors.Grey.Darken2);
                            }
                        });

                        row.ConstantItem(60).AlignRight().Text(product.EffectivePriceText ?? "-").FontSize(9.5f).Bold();
                        row.RelativeItem(2).AlignRight().Text(BuildSecondaryPriceText(product)).FontSize(8).FontColor(Colors.Grey.Darken2);
                    });
                }
            }
        });
    }

    private static void ComposeNotes(ColumnDescriptor column, IReadOnlyList<string> notes, string heading)
    {
        if (notes.Count == 0)
        {
            return;
        }

        column.Item().Column(section =>
        {
            section.Spacing(1);
            SectionHeading(section, heading);

            foreach (var note in notes)
            {
                section.Item().Text($"- {note}").FontSize(9.5f);
            }
        });
    }

    private static void SectionHeading(ColumnDescriptor column, string title) =>
        column.Item().PaddingTop(4).PaddingBottom(2).BorderBottom(1).BorderColor(Colors.Black).Text(title.ToUpperInvariant()).Bold().FontSize(10);

    private static void ComposeFooter(IContainer container, AssociatePassdownDocumentModel model)
    {
        container.PaddingTop(4).BorderTop(0.75f).BorderColor(Colors.Grey.Lighten2).PaddingTop(3).Row(row =>
        {
            row.RelativeItem().Text(text =>
            {
                text.Span($"Generated {model.GeneratedAtText}").FontSize(7.5f).FontColor(Colors.Grey.Darken1);

                if (model.HasUnverifiedProductPricing)
                {
                    text.Span("  -  Some product pricing has not been recently verified.").FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                }
            });

            row.ConstantItem(70).AlignRight().Text(text =>
            {
                text.CurrentPageNumber().FontSize(7.5f);
                text.Span(" of ").FontSize(7.5f);
                text.TotalPages().FontSize(7.5f);
            });
        });
    }

    private static string BuildSecondaryPriceText(PdfProductRow product)
    {
        if (product.ComparisonPriceText is { } comparison && product.Qualifier is { } qualifier)
        {
            return $"was {comparison} - {qualifier}";
        }

        if (product.ComparisonPriceText is { } comparisonOnly)
        {
            return $"was {comparisonOnly}";
        }

        return product.Qualifier ?? string.Empty;
    }

    private static string FormatCount(int? value) => value?.ToString("N0", CultureInfo.InvariantCulture) ?? "Not entered";
}
