using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Carowinds.Passdown.Documents.Associate.Templates;

/// <summary>
/// Phase 17F's shared low-level building blocks — deliberately *not* composition. Each renderer still
/// owns its own page structure (header, summary, section order/pairing, spacing, typography); this
/// only holds the three mechanical rules every template must get identically right: the aligned
/// name/price row, section keep-together/orphan control, and the footer's timestamp + multi-page
/// indicator.
/// </summary>
internal static class PdfPrimitives
{
    /// <summary>
    /// A section with at most this many rows is kept whole: if it does not fit in the space left on the
    /// current page it moves to the next page instead of splitting (no heading stranded at the bottom,
    /// no one-row remainder on the next page). Larger sections are allowed to split naturally — moving
    /// a tall section whole would leave a half-empty page behind it.
    /// </summary>
    public const int KeepTogetherMaxRows = 10;

    /// <summary>
    /// Orphan control for one heading-led section. Small sections use <c>PreventPageBreak</c> (moves
    /// whole when it would split; unlike <c>ShowEntire</c> it never throws for content taller than a
    /// page). Large sections use <c>EnsureSpace</c> so the heading always lands with at least
    /// <paramref name="headingWithFirstRowsHeight"/> of its own rows beneath it, then flows naturally.
    /// </summary>
    public static IContainer KeepSection(this IContainer container, int rowCount, float headingWithFirstRowsHeight) =>
        rowCount <= KeepTogetherMaxRows
            ? container.PreventPageBreak()
            : container.EnsureSpace(headingWithFirstRowsHeight);

    /// <summary>
    /// The one aligned product-row shape (spec Part 9/10): a flexible, wrapping name column and a
    /// fixed-width, right-aligned price column, so every price in a section shares the same right
    /// edge — "From $37" included — and a wrapped name never moves its price. The price sits on the
    /// row's first line so it stays visually attached to the start of the name it belongs to.
    /// </summary>
    public static void PriceRow(
        IContainer container,
        string name,
        string? price,
        float priceColumnWidth,
        Action<TextSpanDescriptor> nameStyle,
        Action<TextSpanDescriptor> priceStyle) =>
        container.Row(row =>
        {
            row.RelativeItem().PaddingRight(8).Text(text => nameStyle(text.Span(name)));
            row.ConstantItem(priceColumnWidth).AlignRight().Text(text => priceStyle(text.Span(price ?? "-")));
        });

    /// <summary>
    /// The footer's right-hand cluster (spec Part 14): the generation timestamp ("Sep 15 • 3:25 PM")
    /// always, followed by "1 / 2" only when the rendered document actually has more than one page —
    /// resolved by QuestPDF at render time from the real total, never from an estimate. No app name,
    /// template name, filename, or machine name.
    /// </summary>
    public static void TimestampAndPageIndicator(RowDescriptor row, string generatedAtText, Action<TextSpanDescriptor> style)
    {
        row.AutoItem().AlignRight().Text(text => style(text.Span(generatedAtText)));

        row.AutoItem().ShowIf(context => context.TotalPages > 1).PaddingLeft(18).Text(text =>
        {
            style(text.CurrentPageNumber());
            style(text.Span(" / "));
            style(text.TotalPages());
        });
    }
}
