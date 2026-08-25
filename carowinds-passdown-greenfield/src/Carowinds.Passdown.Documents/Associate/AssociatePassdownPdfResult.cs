namespace Carowinds.Passdown.Documents.Associate;

/// <summary>What actually happened when <see cref="IAssociatePassdownPdfGenerator.Generate"/> ran. The Associate Passdown is a strict one-page document — there is no continuation-page outcome; content that would not fit on one page fails validation instead (see <see cref="ValidationFailed"/>).</summary>
public enum AssociatePassdownPdfStatus
{
    /// <summary>Rendered as exactly one Letter page.</summary>
    Generated,

    /// <summary>Generation did not run at all — see <see cref="Preflight"/> for why (spec Part G). Includes content that would not fit on one page (spec correction: refused rather than spilling to a second page).</summary>
    ValidationFailed,
}

/// <summary>
/// The result of one PDF generation attempt (spec Part H). <see cref="PdfBytes"/> is null exactly
/// when <see cref="Status"/> is <see cref="AssociatePassdownPdfStatus.ValidationFailed"/> — a caller
/// can check <see cref="Status"/> alone to decide whether there is anything to save/open.
/// </summary>
public sealed record AssociatePassdownPdfResult(
    AssociatePassdownPdfStatus Status,
    byte[]? PdfBytes,
    AssociatePassdownPreflightResult Preflight);
