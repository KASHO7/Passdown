using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Preferences;

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

    /// <summary>
    /// Builds the print-ready document model exactly once from <paramref name="workspace"/>/
    /// <paramref name="asOf"/> and derives both the frozen <see cref="FinalizedPassdownSnapshot"/> and
    /// the frozen PDF bytes from that single in-memory build (spec Phase 9C Part E) — the snapshot and
    /// the PDF must never risk representing different passdown state by being composed from two
    /// separate reads/builds. Throws if PDF generation cannot proceed for this workspace (spec Part
    /// I) — callers must treat that as "finalization failed" and write no row, exactly like a
    /// serialization/hash failure.
    /// </summary>
    /// <param name="template">
    /// Phase 17C: which visual template renders the frozen PDF — the current
    /// <see cref="AssociatePassdownTemplatePreference"/> at the moment of finalization, frozen forever
    /// once written (spec §18/§31: switching the preference afterward never alters an already-finalized
    /// PDF). Defaults to <see cref="AssociatePassdownTemplate.Classic"/> for any caller that does not
    /// pass one explicitly (e.g. existing tests), matching Classic's status as the safe default
    /// (spec §38).
    /// </param>
    FinalizedPassdownProjection BuildProjection(AssociateWorkspace workspace, DateTimeOffset asOf, AssociatePassdownTemplate template = AssociatePassdownTemplate.Classic);
}

/// <summary>
/// The paired result of one <see cref="IFinalizedPassdownSnapshotBuilder.BuildProjection"/> call (spec
/// Phase 9C Part E/W) — <see cref="Snapshot"/> and <see cref="PdfBytes"/> are guaranteed to derive from
/// the exact same canonical document-model build, never two independent ones.
/// </summary>
public sealed record FinalizedPassdownProjection(FinalizedPassdownSnapshot Snapshot, byte[] PdfBytes);
