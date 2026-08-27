namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// One Sunday-through-Saturday row of the Operations Calendar month grid (Phase 15B). Purely a
/// presentation-level grouping of the same <see cref="OperationsCalendarDayViewModel"/> instances
/// <see cref="OperationsCalendarViewModel.Days"/> already carries (chunked 7 at a time — the grid's
/// underlying dates are already complete Sunday-through-Saturday weeks, see
/// <c>OperationsCalendarService.BuildGridDates</c>) — never a second query or a re-derivation of which
/// dates belong to the grid. Exists so the view can render one real 7-column <c>Grid</c> per week
/// (spec Part 1 "full-width 7-column grid") instead of relying on <c>UniformGrid</c>'s own
/// content-driven cell sizing, which is what produced the narrow-strip/off-month-sliver appearance
/// this phase fixes.
/// </summary>
public sealed class OperationsCalendarWeekViewModel(IReadOnlyList<OperationsCalendarDayViewModel> days)
{
    public IReadOnlyList<OperationsCalendarDayViewModel> Days { get; } = days;
}
