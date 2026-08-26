using Carowinds.Passdown.Application.OperationalNotes;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// One selectable entry for the Operational Notes category picker (Phase 11B Part J/S) — the single
/// place that maps <see cref="OperationalNoteCategory"/> to a user-facing label (e.g.
/// <see cref="OperationalNoteCategory.GuestServices"/> → "Guest Services"), reused by both the
/// create/edit ComboBox and the list/detail presentation builders so the mapping can never drift.
/// Unlike <c>DailyFeatureTypeOption</c>, Category is required on a note (spec Phase 11A Part D), so
/// there is no "not set" entry here.
/// </summary>
public sealed record OperationalNoteCategoryOption(OperationalNoteCategory Value, string Label)
{
    public static IReadOnlyList<OperationalNoteCategoryOption> All { get; } =
    [
        new(OperationalNoteCategory.General, "General"),
        new(OperationalNoteCategory.Staffing, "Staffing"),
        new(OperationalNoteCategory.Admissions, "Admissions"),
        new(OperationalNoteCategory.GuestServices, "Guest Services"),
        new(OperationalNoteCategory.Equipment, "Equipment"),
        new(OperationalNoteCategory.Events, "Events"),
        new(OperationalNoteCategory.Operations, "Operations"),
    ];

    public static OperationalNoteCategoryOption For(OperationalNoteCategory value) => All.First(option => option.Value == value);
}
