using Carowinds.Passdown.Application.OperationalNotes;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// One selectable entry for the Operational Notes priority picker (Phase 11B Part J/R) — Priority is
/// required (spec Phase 11A Part E has no "unset" value), so there are exactly two entries.
/// </summary>
public sealed record OperationalNotePriorityOption(OperationalNotePriority Value, string Label)
{
    public static IReadOnlyList<OperationalNotePriorityOption> All { get; } =
    [
        new(OperationalNotePriority.Normal, "Normal"),
        new(OperationalNotePriority.Important, "Important"),
    ];

    public static OperationalNotePriorityOption For(OperationalNotePriority value) => All.First(option => option.Value == value);
}
