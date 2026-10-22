using Carowinds.Passdown.Documents.Teams;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Ert;

namespace Carowinds.Passdown.Documents.Associate;

/// <summary>
/// Phase 29: the Associate passdown's Teams sections as <see cref="TeamsMessage"/> content — the operations
/// summary and today's pricing. Built from the same <see cref="AssociatePassdownDocumentModel"/> the PDF
/// renders, so the clipboard can never disagree with the PDF; the existing Copy actions
/// (<see cref="AssociateTeamsTextFormatter"/>) and the morning message both use these sections, and the
/// plain-text rendering is exactly the Phase 18F text.
/// </summary>
public static class AssociateTeamsMessageBuilder
{
    public const string OperationsKey = "operations";

    public const string PricingKey = "pricing";

    /// <summary>
    /// Park Hours, effective gate times (override ?? default — never labeled as an override), projected
    /// attendance, pre-solds and Early Ride Time, one labeled line each. A value never entered is omitted,
    /// never printed as a placeholder; gate times only on an open day.
    /// </summary>
    public static TeamsMessageSection Operations(AssociatePassdownDocumentModel model, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        var lines = new List<TeamsLine>();
        var hours = model.ParkHours;

        switch (hours.Status)
        {
            case PdfParkHoursStatus.Open:
                lines.Add(TeamsLine.Labeled("Park Hours", hours.HeadlineText));
                AddIfPresent(lines, "Avenue", hours.AvenueText);
                AddIfPresent(lines, "North Gate", hours.NorthGateText);
                break;
            case PdfParkHoursStatus.Closed:
                lines.Add(TeamsLine.Labeled("Park Hours", "Closed"));
                break;
            default:
                lines.Add(TeamsLine.Labeled("Park Hours", "Not checked yet"));
                break;
        }

        if (model.Attendance.ProjectedAttendance is { } projected)
        {
            lines.Add(TeamsLine.Labeled("Projected Attendance", OperationalCount.Format(projected)));
        }

        if (model.Attendance.PreSolds is { } preSolds)
        {
            lines.Add(TeamsLine.Labeled("Pre-solds", OperationalCount.Format(preSolds)));
        }

        if (ErtText(model.EarlyRideTime) is { } ert)
        {
            lines.Add(TeamsLine.Labeled("ERT", ert));
        }

        return new TeamsMessageSection(OperationsKey, title, [new TeamsParagraph(lines)]);
    }

    /// <summary>
    /// "Ticket Prices:" then one block per included product section, in PDF order: the section's PDF
    /// heading (bold when formatted) followed by "Name: $59.00" / "Name: From $37.00" — the exact
    /// <see cref="PdfProductRow.EffectivePriceText"/> the PDF prints. A row with no price is left out, and
    /// a section left with no rows is left out with its heading. Empty (no blocks) when nothing remains.
    /// <paramref name="includeCustomItems"/> is false for the morning message, which carries official
    /// prices only.
    /// </summary>
    public static TeamsMessageSection Pricing(AssociatePassdownDocumentModel model, bool includeCustomItems = true)
    {
        ArgumentNullException.ThrowIfNull(model);

        var blocks = model.ProductCategories
            .Where(category => includeCustomItems || category.Section is not null)
            .Select(category => (category.CategoryName, Rows: category.Products
                .Where(product => !string.IsNullOrWhiteSpace(product.EffectivePriceText))
                .Select(product => TeamsLine.Of($"{product.DisplayName}: {product.EffectivePriceText}"))
                .ToList()))
            .Where(block => block.Rows.Count > 0)
            .Select(block => (TeamsBlock)new TeamsParagraph([new TeamsLine(TeamsRun.Strong(block.CategoryName)), .. block.Rows]))
            .ToList();

        return new TeamsMessageSection(PricingKey, "Ticket Prices:", blocks, TitleIsSeparateBlock: true);
    }

    private static void AddIfPresent(List<TeamsLine> lines, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && value != "-")
        {
            lines.Add(TeamsLine.Labeled(label, value));
        }
    }

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
