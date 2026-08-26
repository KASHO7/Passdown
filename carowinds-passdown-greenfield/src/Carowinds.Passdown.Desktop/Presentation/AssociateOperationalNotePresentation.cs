using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.OperationalNotes;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// One "available to select" persistent-note suggestion row for Associate → Notes (Phase 11D Part
/// U). Title-first, no <see cref="OperationalNote.Body"/> shown by default (spec Part U/Part V
/// guidance to keep the suggestion list compact) — category/priority/date-range labels reuse the
/// same Phase 11B/11C centralized mapping/formatters as every other Operational Notes surface so
/// wording can never drift between screens.
/// </summary>
public sealed record AssociateOperationalNoteSuggestionPresentation(
    Guid Id,
    string Title,
    string CategoryLabel,
    string PriorityLabel,
    bool IsImportant,
    string DateRangeText,
    string? CarryForwardText)
{
    public string AccessibleSummary => IsImportant
        ? $"Add. Important. {CategoryLabel}. {Title}. {DateRangeText}."
        : $"Add. {CategoryLabel}. {Title}. {DateRangeText}.";

    public static AssociateOperationalNoteSuggestionPresentation Build(OperationalNote note, DateOnly referenceDate)
    {
        ArgumentNullException.ThrowIfNull(note);

        // Every candidate here already came from IAssociatePassdownService.GetRelevantOperationalNoteSuggestionsAsync
        // (Phase 11D Part E), so its lifecycle for referenceDate is always Active by construction —
        // never re-derived, only assumed for the shared formatter's Upcoming-vs-range branch.
        const OperationalNoteLifecycleState lifecycle = OperationalNoteLifecycleState.Active;
        var dateRangeText = OperationalNoteDateRangeFormatter.Format(note.EffectiveFrom, note.EffectiveThrough, lifecycle, referenceDate);

        // Phase 11E Part T: a short relative "date relevance" hint alongside the absolute range, using
        // the same carry-forward formatter every other Operational Notes surface uses.
        var carryForwardText = OperationalNoteCarryForwardFormatter.Format(lifecycle, note.EffectiveFrom, note.EffectiveThrough, referenceDate);

        return new AssociateOperationalNoteSuggestionPresentation(
            note.Id,
            note.Title,
            OperationalNoteCategoryOption.For(note.Category).Label,
            OperationalNotePriorityOption.For(note.Priority).Label,
            note.Priority == OperationalNotePriority.Important,
            dateRangeText,
            carryForwardText);
    }
}

/// <summary>
/// One selected (already-included) persistent note's frozen copy, as shown in Associate → Notes
/// (Phase 11D Part V). Built entirely from the frozen <see cref="AssociateOperationalNoteSelection"/>
/// — never from a live <see cref="OperationalNote"/> lookup, so this presentation can never
/// accidentally show a source note's current (possibly since-changed) content.
/// </summary>
public sealed record AssociateSelectedOperationalNotePresentation(
    Guid SourceOperationalNoteId,
    string Title,
    string? Body,
    string CategoryLabel,
    string PriorityLabel,
    bool IsImportant,
    string DateRangeText,
    OperationalNoteSourceComparisonState SourceState,
    string? SourceStatusText,
    bool ShowReplaceAction)
{
    public string AccessibleSummary
    {
        get
        {
            var prefix = IsImportant
                ? $"Remove from this passdown. Important. {CategoryLabel}. {Title}."
                : $"Remove from this passdown. {CategoryLabel}. {Title}.";
            return SourceStatusText is null ? prefix : $"{prefix} {SourceStatusText}";
        }
    }

    /// <summary>
    /// Phase 11E Part H: a short, non-technical explanation that a selected note's content is frozen —
    /// deliberately avoids "snapshot"/"detached entity" wording. Shown once above the section, not
    /// per-row, since it is true for every row in the list.
    /// </summary>
    public const string FrozenCopyExplanation = "Included notes keep the version selected for this passdown.";

    public static AssociateSelectedOperationalNotePresentation Build(AssociateOperationalNoteSelection selection, OperationalNoteSourceComparisonState sourceState)
    {
        ArgumentNullException.ThrowIfNull(selection);

        // The frozen date-range text is a plain from/through range, never "Starts"/"Upcoming" — a
        // selected note was relevant (Active) at selection time by construction (Part O), and its
        // frozen copy is never re-derived against any later reference date.
        var dateRangeText = OperationalNoteDateRangeFormatter.Format(
            selection.EffectiveFrom, selection.EffectiveThrough, OperationalNoteLifecycleState.Active, selection.EffectiveFrom);

        // Phase 11E Parts G-L: informational only — the frozen fields above are never touched by this
        // status text. Replace with Current Version (Part I) is only offered for ContentChanged; a
        // Resolved/NoLongerApplicable/Missing source is surfaced as plain informational text and the
        // supervisor is left to choose Remove (or keep) themselves (Parts J/K/L).
        var (statusText, showReplace) = sourceState switch
        {
            OperationalNoteSourceComparisonState.ContentChanged => ("Source note changed since inclusion.", true),
            OperationalNoteSourceComparisonState.Resolved => ("Source note is now resolved.", false),
            OperationalNoteSourceComparisonState.NoLongerApplicable => ("Source note no longer applies to this date.", false),
            OperationalNoteSourceComparisonState.Missing => ("Source note unavailable.", false),
            _ => (null as string, false),
        };

        return new AssociateSelectedOperationalNotePresentation(
            selection.SourceOperationalNoteId,
            selection.Title,
            selection.Body,
            OperationalNoteCategoryOption.For(selection.Category).Label,
            OperationalNotePriorityOption.For(selection.Priority).Label,
            selection.Priority == OperationalNotePriority.Important,
            dateRangeText,
            sourceState,
            statusText,
            showReplace);
    }
}
