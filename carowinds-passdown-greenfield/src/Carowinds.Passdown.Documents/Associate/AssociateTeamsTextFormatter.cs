using Carowinds.Passdown.Documents.Teams;

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
/// Phase 29: the sections themselves come from <see cref="AssociateTeamsMessageBuilder"/> and are rendered
/// by <see cref="TeamsPlainTextRenderer"/> (text unchanged) or <see cref="TeamsHtmlRenderer"/> (formatted copy).
/// </para>
/// </summary>
public static class AssociateTeamsTextFormatter
{
    /// <summary>
    /// The selected copy action's text, or <see langword="null"/> when there is nothing meaningful to
    /// copy — only <see cref="AssociateTeamsCopyKind.TodaysPricing"/> can be empty (no priced product
    /// selected); <see cref="AssociateTeamsCopyKind.OperationsAndPricing"/> then falls back to the
    /// operations summary alone rather than an empty "Ticket Prices:" heading.
    /// </summary>
    public static string? Format(AssociatePassdownDocumentModel model, AssociateTeamsCopyKind kind) =>
        BuildMessage(model, kind) is { } message ? TeamsPlainTextRenderer.Render(message) : null;

    /// <summary>
    /// Phase 29: the selected copy action as a <see cref="TeamsMessage"/> — the one representation both the
    /// plain text (<see cref="Format"/>) and the formatted copy are rendered from. Null exactly when
    /// <see cref="Format"/> is null.
    /// </summary>
    public static TeamsMessage? BuildMessage(AssociatePassdownDocumentModel model, AssociateTeamsCopyKind kind)
    {
        ArgumentNullException.ThrowIfNull(model);

        var pricing = AssociateTeamsMessageBuilder.Pricing(model);
        return kind switch
        {
            AssociateTeamsCopyKind.OperationsSummary => new TeamsMessage(AssociateTeamsMessageBuilder.Operations(model)),
            AssociateTeamsCopyKind.TodaysPricing => pricing.IsEmpty ? null : new TeamsMessage(pricing),
            AssociateTeamsCopyKind.OperationsAndPricing => pricing.IsEmpty
                ? new TeamsMessage(AssociateTeamsMessageBuilder.Operations(model))
                : new TeamsMessage(AssociateTeamsMessageBuilder.Operations(model), pricing),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    /// <summary>
    /// Park Hours, effective gate times (override ?? default — never labeled as an override),
    /// projected attendance, pre-solds, and Early Ride Time. A value that was never entered is
    /// omitted rather than printed as a placeholder; gate times only print on an open day.
    /// </summary>
    public static string FormatOperationsSummary(AssociatePassdownDocumentModel model) =>
        TeamsPlainTextRenderer.Render(new TeamsMessage(AssociateTeamsMessageBuilder.Operations(model)));

    /// <summary>
    /// "Ticket Prices:" then one block per included product section, in PDF order: the section's PDF
    /// heading followed by "Name: $59.00" / "Name: From $37.00" lines — the exact
    /// <see cref="PdfProductRow.EffectivePriceText"/> the PDF prints. A row with no price is left out
    /// (the PDF's "-" placeholder means nothing in a Teams post), and a section left with no rows is
    /// left out with its heading. <see langword="null"/> when nothing remains.
    /// </summary>
    public static string? FormatPricing(AssociatePassdownDocumentModel model)
    {
        var pricing = AssociateTeamsMessageBuilder.Pricing(model);
        return pricing.IsEmpty ? null : TeamsPlainTextRenderer.Render(new TeamsMessage(pricing));
    }
}
