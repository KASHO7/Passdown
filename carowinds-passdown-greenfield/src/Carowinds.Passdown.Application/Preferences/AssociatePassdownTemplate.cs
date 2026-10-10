namespace Carowinds.Passdown.Application.Preferences;

/// <summary>
/// One of the interchangeable Associate Passdown PDF visual templates (Phase 17C). All three render
/// the exact same template-neutral document model (<c>Documents.Associate.AssociatePassdownDocumentModel</c>)
/// — this enum selects presentation only, never business logic; no product classification, ERT
/// resolution, or section-inclusion decision varies by template.
/// </summary>
public enum AssociatePassdownTemplate
{
    /// <summary>Preserves the pre-17C recognizable passdown style — the default for existing and fresh installs.</summary>
    Classic,

    /// <summary>Optimized for information density: minimal borders, reduced whitespace, the best chance of fitting a busy day on one page.</summary>
    Compact,

    /// <summary>An alternative professional hierarchy/section composition — still printer-friendly and operational, not decorative.</summary>
    Modern,
}
