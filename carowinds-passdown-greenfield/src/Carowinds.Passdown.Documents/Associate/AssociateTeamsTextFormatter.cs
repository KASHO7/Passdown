using System.Text;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Ert;

namespace Carowinds.Passdown.Documents.Associate;

/// <summary>The three Phase 18F "Copy" menu actions on the Associate screen.</summary>
public enum AssociateTeamsCopyKind
{
    OperationsSummary,
    TodaysPricing,
    OperationsAndPricing,
}

/// <summary>
/// Plain-text, Microsoft Teams-friendly output for the Associate passdown (Phase 18F). Formatting
/// only: every input is the same <see cref="AssociatePassdownDocumentModel"/> the PDF renders from
/// (<see cref="AssociatePassdownDocumentModelBuilder.Build"/>), so section/product inclusion,
/// suppression, display names, effective price, "From $x" semantics, ordering, and effective gate
/// times are decided once, upstream, and can never drift between the PDF and the clipboard. No
/// retrieval, no persistence, no catalog interpretation happens here.
/// <para>
/// Output is deliberately plain lines (no Markdown tables, no columns) so it stays legible when
/// Teams strips rich formatting. Daily Feature and Operational Notes are intentionally not included.
/// </para>
/// </summary>
public static class AssociateTeamsTextFormatter
{
    private const string LineBreak = "\r\n";

    /// <summary>
    /// The selected copy action's text, or <see langword="null"/> when there is nothing meaningful to
    /// copy — only <see cref="AssociateTeamsCopyKind.TodaysPricing"/> can be empty (no priced product
    /// selected); <see cref="AssociateTeamsCopyKind.OperationsAndPricing"/> then falls back to the
    /// operations summary alone rather than an empty "Ticket Prices:" heading.
    /// </summary>
    public static string? Format(AssociatePassdownDocumentModel model, AssociateTeamsCopyKind kind) => kind switch
    {
        AssociateTeamsCopyKind.OperationsSummary => FormatOperationsSummary(model),
        AssociateTeamsCopyKind.TodaysPricing => FormatPricing(model),
        AssociateTeamsCopyKind.OperationsAndPricing => FormatPricing(model) is { } pricing
            ? FormatOperationsSummary(model) + LineBreak + LineBreak + pricing
            : FormatOperationsSummary(model),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// Park Hours, effective gate times (override ?? default — never labeled as an override),
    /// projected attendance, pre-solds, and Early Ride Time. A value that was never entered is
    /// omitted rather than printed as a placeholder; gate times only print on an open day.
    /// </summary>
    public static string FormatOperationsSummary(AssociatePassdownDocumentModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var lines = new List<string>();
        var hours = model.ParkHours;

        switch (hours.Status)
        {
            case PdfParkHoursStatus.Open:
                lines.Add($"Park Hours: {hours.HeadlineText}");
                AddIfPresent(lines, "Avenue", hours.AvenueText);
                AddIfPresent(lines, "North Gate", hours.NorthGateText);
                break;
            case PdfParkHoursStatus.Closed:
                lines.Add("Park Hours: Closed");
                break;
            default:
                lines.Add("Park Hours: Not checked yet");
                break;
        }

        if (model.Attendance.ProjectedAttendance is { } projected)
        {
            lines.Add($"Projected Attendance: {FormatCount(projected)}");
        }

        if (model.Attendance.PreSolds is { } preSolds)
        {
            lines.Add($"Pre-solds: {FormatCount(preSolds)}");
        }

        if (ErtText(model.EarlyRideTime) is { } ert)
        {
            lines.Add($"ERT: {ert}");
        }

        return string.Join(LineBreak, lines);
    }

    /// <summary>
    /// "Ticket Prices:" then one block per included product section, in PDF order: the section's PDF
    /// heading followed by "Name: $59.00" / "Name: From $37.00" lines — the exact
    /// <see cref="PdfProductRow.EffectivePriceText"/> the PDF prints. A row with no price is left out
    /// (the PDF's "-" placeholder means nothing in a Teams post), and a section left with no rows is
    /// left out with its heading. <see langword="null"/> when nothing remains.
    /// </summary>
    public static string? FormatPricing(AssociatePassdownDocumentModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var blocks = model.ProductCategories
            .Select(category => (category.CategoryName, Rows: category.Products
                .Where(product => !string.IsNullOrWhiteSpace(product.EffectivePriceText))
                .Select(product => $"{product.DisplayName}: {product.EffectivePriceText}")
                .ToList()))
            .Where(block => block.Rows.Count > 0)
            .Select(block => block.CategoryName + LineBreak + string.Join(LineBreak, block.Rows))
            .ToList();

        if (blocks.Count == 0)
        {
            return null;
        }

        var text = new StringBuilder("Ticket Prices:");
        foreach (var block in blocks)
        {
            text.Append(LineBreak).Append(LineBreak).Append(block);
        }

        return text.ToString();
    }

    private static void AddIfPresent(List<string> lines, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && value != "-")
        {
            lines.Add($"{label}: {value}");
        }
    }

    private static string FormatCount(int value) => value.ToString("N0", PresentationCulture.UnitedStates);

    /// <summary>
    /// The same three states every PDF template prints (<c>PdfFormatting.ErtLineText</c>), worded for a
    /// sentence: "None", "Not configured", or the rides joined naturally ("A and B", "A, B, and C").
    /// </summary>
    private static string? ErtText(PdfEarlyRideTime earlyRideTime) => earlyRideTime.Kind switch
    {
        EffectiveErtKind.NotConfigured => "Not configured",
        EffectiveErtKind.NoErt => "None",
        _ => earlyRideTime.Rides.Count > 0 ? JoinNaturally(earlyRideTime.Rides) : null,
    };

    private static string JoinNaturally(IReadOnlyList<string> items) => items.Count switch
    {
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        _ => string.Join(", ", items.Take(items.Count - 1)) + ", and " + items[^1],
    };
}
