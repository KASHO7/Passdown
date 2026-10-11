using Carowinds.Passdown.Application.Preferences;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Carowinds.Passdown.Documents.Associate.Templates;

/// <summary>
/// One visual rendering of an <see cref="AssociatePassdownDocumentModel"/> (Phase 17C §20/§21) — the
/// seam between the template-neutral document model and one specific presentation (Classic/Compact/
/// Modern). A renderer owns the entire page (size, margins, header, body composition, and footer) so
/// each template is free to choose a genuinely different visual hierarchy/section composition, not
/// just different colors on an identical layout — but it must never independently reclassify
/// products, resolve ERT, decide expiration, or otherwise duplicate business logic that already lives
/// upstream in <see cref="AssociatePassdownDocumentModelBuilder"/> (spec §23: "templates must receive
/// already-resolved product sections"). <see cref="AssociatePassdownPdfGenerator"/> is the only
/// caller — it owns font registration, preflight, and page counting, all of which are identical
/// regardless of which renderer composes the page.
/// </summary>
public interface IAssociatePassdownTemplateRenderer
{
    /// <summary>Which <see cref="AssociatePassdownTemplate"/> this renderer implements — used by <see cref="AssociatePassdownPdfGenerator"/> to select it, never by the renderer itself.</summary>
    AssociatePassdownTemplate Template { get; }

    /// <summary>
    /// Composes the entire page (size/margin/header/content/footer) from <paramref name="model"/>. Must not mutate <paramref name="model"/> — the same model is reused across preview/finalize calls and, in tests, across multiple renderers.
    /// <paramref name="density"/> (Phase 17F) only ever changes vertical spacing — never content, order, composition, or type size.
    /// </summary>
    void Compose(PageDescriptor page, AssociatePassdownDocumentModel model, PdfDensity density, CancellationToken cancellationToken);
}

/// <summary>
/// Phase 17F "compress before paginating": <see cref="AssociatePassdownPdfGenerator"/> always renders
/// <see cref="Standard"/> first and, only when that overflows onto more than one page, retries with
/// <see cref="Tight"/> — keeping the tighter render only if it actually saves a page. Same content,
/// same composition, same type sizes; only section/row/note spacing shrinks.
/// </summary>
public enum PdfDensity
{
    Standard,
    Tight,
}
