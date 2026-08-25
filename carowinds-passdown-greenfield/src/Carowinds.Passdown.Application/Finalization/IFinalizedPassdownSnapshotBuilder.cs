using Carowinds.Passdown.Application.Associate;

namespace Carowinds.Passdown.Application.Finalization;

/// <summary>
/// Converts the CURRENT composed passdown state into an immutable <see cref="FinalizedPassdownSnapshot"/>
/// (spec Phase 9 Part J). Pure and entirely local: an implementation must build only from
/// <paramref name="workspace"/> as already composed by <see cref="Associate.IAssociatePassdownService.LoadAssociateDateAsync"/>
/// — no HTTP/Playwright/website call, so a future Phase 9B scheduled finalization can call this with
/// whatever is already stored at cutoff time, offline. The interface lives in Application (so
/// Phase 9's persistence/finalization plumbing never needs to reference the Documents project), but
/// the canonical implementation lives in Documents, reusing <c>AssociatePassdownDocumentModelBuilder</c>
/// — the same print-ready projection <c>AssociatePassdownPdfGenerator</c> renders — so the finalized
/// history can never disagree with what the PDF actually showed (spec Part K).
/// </summary>
public interface IFinalizedPassdownSnapshotBuilder
{
    /// <summary>Builds a snapshot of <paramref name="workspace"/> as of <paramref name="asOf"/> (used only for the snapshot's own "generated at" display text, not for any retrieval).</summary>
    FinalizedPassdownSnapshot Build(AssociateWorkspace workspace, DateTimeOffset asOf);
}
