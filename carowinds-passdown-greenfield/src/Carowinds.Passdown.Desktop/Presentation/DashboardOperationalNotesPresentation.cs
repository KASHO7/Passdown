using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.OperationalNotes;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// One persistent Operational Note's compact Dashboard preview row (Phase 11C Part G/AG) —
/// title-first; category/priority labels reuse Phase 11B's centralized
/// <see cref="OperationalNoteCategoryOption"/>/<see cref="OperationalNotePriorityOption"/> mapping
/// (Part K) so the wording can never drift from the Operational Notes Manager screen, and
/// <see cref="DateRangeText"/> reuses <see cref="OperationalNoteDateRangeFormatter"/> directly (Part
/// L). Deliberately no <see cref="OperationalNote.Body"/> field (Part I) — this is a summary row, not
/// the full detail Operational Notes Manager already shows.
/// </summary>
public sealed record DashboardOperationalNoteItemPresentation(
    Guid Id,
    string Title,
    string CategoryLabel,
    string PriorityLabel,
    bool IsImportant,
    string DateRangeText)
{
    /// <summary>Composed accessible text (Phase 11C Part AH) so priority/category/date-range are always represented in text even though the compact visual layout only surfaces "Important" when it applies.</summary>
    public string AccessibleSummary => IsImportant
        ? $"Important. {CategoryLabel}. {Title}. {DateRangeText}."
        : $"{CategoryLabel}. {Title}. {DateRangeText}.";

    public static DashboardOperationalNoteItemPresentation Build(DashboardOperationalNoteItem item, DateOnly referenceDate)
    {
        ArgumentNullException.ThrowIfNull(item);

        // Every item here already came from IOperationalNotesService.ListRelevantForDateAsync (Phase
        // 11C Part C/E), so its lifecycle for referenceDate is always Active by construction — never
        // re-derived, only assumed for the shared formatter's Upcoming-vs-range branch.
        var dateRangeText = OperationalNoteDateRangeFormatter.Format(
            item.EffectiveFrom, item.EffectiveThrough, OperationalNoteLifecycleState.Active, referenceDate);

        return new DashboardOperationalNoteItemPresentation(
            item.Id,
            item.Title,
            OperationalNoteCategoryOption.For(item.Category).Label,
            OperationalNotePriorityOption.For(item.Priority).Label,
            item.Priority == OperationalNotePriority.Important,
            dateRangeText);
    }
}

/// <summary>
/// Turns a <see cref="DashboardOperationalNotesSummary"/> into display-ready strings (Phase 11C Part
/// G/H/M) — no relevance/ordering/business rule is recomputed here, only formatting of already-bounded,
/// already-ordered data. Never carries an "Include"/"Add to Passdown" affordance (Part T) and is never
/// consumed by anything that writes into an Associate draft (Part S) — this is a read-only summary
/// projection, nothing more.
/// </summary>
public sealed record DashboardOperationalNotesPresentation(
    int Count,
    int ImportantCount,
    IReadOnlyList<DashboardOperationalNoteItemPresentation> Items,
    bool HasOverflow,
    string OverflowText,
    bool LoadFailed)
{
    public bool HasNotes => Count > 0;

    /// <summary>Phase 11C Part M: shown only when the local query succeeded and returned nothing relevant today — a load failure gets its own distinct message instead (Part AI).</summary>
    public bool ShowEmptyState => !LoadFailed && Count == 0;

    /// <summary>
    /// Phase 22C: one truthful summary line for the Operations Overview. "Active" is the existing
    /// definition — notes whose effective range covers today (<c>ListRelevantForDateAsync</c>) — and
    /// "important" is only ever the priority a supervisor explicitly set; nothing else is implied.
    /// </summary>
    public string SummaryText => LoadFailed
        ? "Unable to load operational notes"
        : Count == 0
            ? "No active notes today"
            : $"{Count} active note{(Count == 1 ? string.Empty : "s")}" + (ImportantCount > 0 ? $" · {ImportantCount} important" : string.Empty);

    public static DashboardOperationalNotesPresentation Build(DashboardOperationalNotesSummary summary, DateOnly referenceDate)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var items = summary.Items
            .Select(item => DashboardOperationalNoteItemPresentation.Build(item, referenceDate))
            .ToList();

        var overflow = summary.Count - items.Count;
        var overflowText = overflow > 0 ? $"+ {overflow} more active note{(overflow == 1 ? string.Empty : "s")}" : string.Empty;

        return new DashboardOperationalNotesPresentation(summary.Count, summary.ImportantCount, items, overflow > 0, overflowText, summary.LoadFailed);
    }
}
