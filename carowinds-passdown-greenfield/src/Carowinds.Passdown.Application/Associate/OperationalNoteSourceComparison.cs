using Carowinds.Passdown.Application.OperationalNotes;

namespace Carowinds.Passdown.Application.Associate;

/// <summary>
/// Informational comparison between an already-selected, frozen <see cref="AssociateOperationalNoteSelection"/>
/// and its live <see cref="OperationalNote"/> source (Phase 11E Parts G-L). This never changes what a
/// selection renders/prints — a selected copy stays frozen exactly as Phase 11D designed it — it only
/// tells the Associate Notes screen whether the source has drifted since selection, so a supervisor can
/// decide (never automatically) whether to keep, replace, or remove it.
/// </summary>
public enum OperationalNoteSourceComparisonState
{
    /// <summary>The source note still exists, is Active, is relevant for the selection's operating date, and its content matches the frozen copy exactly.</summary>
    Unchanged,

    /// <summary>The source note still exists, is Active, and is still relevant for the selection's operating date, but at least one displayed field (title/body/category/priority/date range) now differs from the frozen copy.</summary>
    ContentChanged,

    /// <summary>The source note has been explicitly resolved since selection. The frozen copy is unaffected and remains in the draft until a supervisor removes it.</summary>
    Resolved,

    /// <summary>The source note is still Active but its effective date range no longer covers the selection's operating date (e.g. <see cref="OperationalNote.EffectiveThrough"/> was shortened). The frozen copy is unaffected.</summary>
    NoLongerApplicable,

    /// <summary>The source note could not be found at all. The frozen copy remains fully usable — Associate draft, PDF, and finalization never depend on the source row existing (Phase 11D Part AD).</summary>
    Missing,
}

/// <summary>
/// Computes <see cref="OperationalNoteSourceComparisonState"/> by comparing a frozen selection's own
/// already-stored display fields (Title/Body/Category/Priority/EffectiveFrom/EffectiveThrough) against
/// the current live source note (Phase 11E Part M's schema decision: no new "SourceUpdatedAtUtcAtSelection"
/// field was added to <see cref="AssociateOperationalNoteSelection"/> — a direct field comparison is
/// both simpler and more accurate than a timestamp comparison, and it works identically for a selection
/// made before this phase existed, since every field it needs already existed in schema 3). This keeps
/// <see cref="AssociateDraftPayload.CurrentSchemaVersion"/> at 3 and makes backward compatibility
/// automatic rather than a special case (Phase 11E Part N).
/// </summary>
public static class OperationalNoteSourceComparison
{
    public static OperationalNoteSourceComparisonState Compute(AssociateOperationalNoteSelection selection, OperationalNote? source, DateOnly operatingDate)
    {
        ArgumentNullException.ThrowIfNull(selection);

        if (source is null)
        {
            return OperationalNoteSourceComparisonState.Missing;
        }

        if (source.Status == OperationalNoteStatus.Resolved)
        {
            return OperationalNoteSourceComparisonState.Resolved;
        }

        if (!OperationalNoteRelevance.IsRelevantForDate(source, operatingDate))
        {
            return OperationalNoteSourceComparisonState.NoLongerApplicable;
        }

        var contentUnchanged =
            source.Title == selection.Title &&
            source.Body == selection.Body &&
            source.Category == selection.Category &&
            source.Priority == selection.Priority &&
            source.EffectiveFrom == selection.EffectiveFrom &&
            source.EffectiveThrough == selection.EffectiveThrough;

        return contentUnchanged ? OperationalNoteSourceComparisonState.Unchanged : OperationalNoteSourceComparisonState.ContentChanged;
    }

    /// <summary>Computes a state per selection, keyed by <see cref="AssociateOperationalNoteSelection.SourceOperationalNoteId"/>, from one already-loaded set of every persistent note (Active and Resolved) — never a per-row lookup (Phase 11E performance guidance mirrors Phase 11C Part AK).</summary>
    public static IReadOnlyDictionary<Guid, OperationalNoteSourceComparisonState> ComputeAll(
        IReadOnlyList<AssociateOperationalNoteSelection> selections,
        IReadOnlyList<OperationalNote> allSourceNotes,
        DateOnly operatingDate)
    {
        ArgumentNullException.ThrowIfNull(selections);
        ArgumentNullException.ThrowIfNull(allSourceNotes);

        if (selections.Count == 0)
        {
            return new Dictionary<Guid, OperationalNoteSourceComparisonState>();
        }

        var bySourceId = allSourceNotes.ToDictionary(note => note.Id);

        var result = new Dictionary<Guid, OperationalNoteSourceComparisonState>(selections.Count);
        foreach (var selection in selections)
        {
            result[selection.SourceOperationalNoteId] = Compute(selection, bySourceId.GetValueOrDefault(selection.SourceOperationalNoteId), operatingDate);
        }

        return result;
    }
}
