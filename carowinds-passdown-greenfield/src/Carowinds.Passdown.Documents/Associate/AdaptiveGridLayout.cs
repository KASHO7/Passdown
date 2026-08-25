namespace Carowinds.Passdown.Documents.Associate;

/// <summary>
/// Shared row-chunking rule for the adaptive Key Selling/Promotional grids (spec Phase 8B polish
/// Parts 1/2) — how many cards/tiles share one row for a given collection size. Used identically by
/// <see cref="AssociatePassdownPdfGenerator"/> (to actually lay the grid out with QuestPDF
/// <c>RelativeItem</c> rows, so each row's items evenly divide the full row width regardless of
/// count) and <see cref="AssociatePassdownPreflightService"/> (to estimate the grid's rendered
/// height), so the two can never disagree about how many rows a given item count produces.
/// </summary>
internal static class AdaptiveGridLayout
{
    /// <summary>
    /// Items per row for a grid of this total size: a single item spans its own row (rendered
    /// centered and width-capped by the caller, never stretched full width), two/three items split
    /// their row exactly in half/thirds, four uses a balanced 2x2 rather than one cramped row of
    /// four, and larger collections wrap at three per row.
    /// </summary>
    public static int ItemsPerRow(int totalCount) => totalCount switch
    {
        <= 1 => 1,
        2 => 2,
        3 => 3,
        4 => 2,
        _ => 3,
    };

    /// <summary>Total rows a grid of this size renders as, given <see cref="ItemsPerRow"/> — a trailing partial row (e.g. 7 items at 3-per-row leaves a lone 7th) still counts as its own row.</summary>
    public static int RowCount(int totalCount) =>
        totalCount <= 0 ? 0 : (int)Math.Ceiling(totalCount / (double)ItemsPerRow(totalCount));
}
