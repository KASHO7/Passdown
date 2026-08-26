using Carowinds.Passdown.Application.OperationalNotes;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// One selectable entry for Operational Notes Manager's category filter (Phase 11E Part A) —
/// <see langword="null"/> <see cref="Value"/> means "All". Deliberately reuses
/// <see cref="OperationalNoteCategoryOption"/>'s own centralized labels rather than a second mapping
/// (spec Part A: "Use centralized category labels... Do not duplicate enum formatting"), and is a
/// nullable-<see cref="OperationalNoteCategory"/> wrapper rather than a parallel enum, so there is
/// exactly one place a category's label can ever be defined.
/// </summary>
public sealed record OperationalNoteCategoryFilterOption(OperationalNoteCategory? Value, string Label)
{
    public static OperationalNoteCategoryFilterOption AllOption { get; } = new(null, "All");

    public static IReadOnlyList<OperationalNoteCategoryFilterOption> All { get; } =
        [AllOption, .. OperationalNoteCategoryOption.All.Select(option => new OperationalNoteCategoryFilterOption(option.Value, option.Label))];
}

/// <summary>
/// One selectable entry for Operational Notes Manager's priority filter (Phase 11E Part B) — mirrors
/// <see cref="OperationalNoteCategoryFilterOption"/>'s "All plus one entry per real value" shape, reusing
/// <see cref="OperationalNotePriorityOption"/>'s labels. Deliberately no new "severity" terminology (spec
/// Part B) — priority filtering only ever reuses the exact Normal/Important words already established.
/// </summary>
public sealed record OperationalNotePriorityFilterOption(OperationalNotePriority? Value, string Label)
{
    public static OperationalNotePriorityFilterOption AllOption { get; } = new(null, "All");

    public static IReadOnlyList<OperationalNotePriorityFilterOption> All { get; } =
        [AllOption, .. OperationalNotePriorityOption.All.Select(option => new OperationalNotePriorityFilterOption(option.Value, option.Label))];
}
