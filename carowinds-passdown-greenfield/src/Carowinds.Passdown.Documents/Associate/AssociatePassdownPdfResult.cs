namespace Carowinds.Passdown.Documents.Associate;

/// <summary>What actually happened when <see cref="IAssociatePassdownPdfGenerator.Generate"/> ran.</summary>
public enum AssociatePassdownPdfStatus
{
    /// <summary>Rendered as exactly one Letter page.</summary>
    Generated,

    /// <summary>Rendered as a page-one executive summary plus a labeled "Supporting Details" continuation page (spec §11.4) — content still fits, nothing was dropped or truncated.</summary>
    GeneratedWithContinuationPage,

    /// <summary>Generation did not run at all — see <see cref="Preflight"/> for why (spec Part G).</summary>
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
