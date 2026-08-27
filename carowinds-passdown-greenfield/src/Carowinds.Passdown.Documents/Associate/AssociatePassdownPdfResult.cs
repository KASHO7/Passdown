namespace Carowinds.Passdown.Documents.Associate;

/// <summary>
/// What actually happened when <see cref="IAssociatePassdownPdfGenerator.Generate"/> ran. Phase 13D
/// Part AA revised this: the Associate Passdown is normally a one-page document, but content that
/// overflows is now rendered onto real additional pages (QuestPDF's own pagination) rather than
/// refused — see <see cref="AssociatePassdownPdfResult.PageCount"/> for the actual, authoritative
/// page count of what was generated.
/// </summary>
public enum AssociatePassdownPdfStatus
{
    /// <summary>Rendered successfully — see <see cref="AssociatePassdownPdfResult.PageCount"/> for whether it landed on one page or spilled onto more.</summary>
    Generated,

    /// <summary>Generation did not run at all — see <see cref="Preflight"/> for why (spec Part G). Reserved for a document with nothing to print (no operating date); no longer used merely because content would exceed one page.</summary>
    ValidationFailed,
}

/// <summary>
/// The result of one PDF generation attempt (spec Part H; revised Phase 13D Part AA/AB).
/// <see cref="PdfBytes"/> is null exactly when <see cref="Status"/> is
/// <see cref="AssociatePassdownPdfStatus.ValidationFailed"/> — a caller can check
/// <see cref="Status"/> alone to decide whether there is anything to save/open.
/// <see cref="PageCount"/> is the real, authoritative page count QuestPDF actually produced (0 when
/// nothing was generated) — the caller-facing multi-page warning must be driven by this value, never
/// by <see cref="Preflight"/>'s pre-render estimate.
/// </summary>
public sealed record AssociatePassdownPdfResult(
    AssociatePassdownPdfStatus Status,
    byte[]? PdfBytes,
    AssociatePassdownPreflightResult Preflight,
    int PageCount = 0);
