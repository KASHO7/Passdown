namespace Carowinds.Passdown.Application.Calendar;

/// <summary>
/// Application-layer seam for the Operations Calendar (Phase 12A Part D): a local-only read model
/// that summarizes and routes — never a second Associate/Notes/History editor. The Desktop screen
/// binds to this the same way it binds to <see cref="Dashboard.IOperationsDashboardService"/> and
/// <see cref="History.IPassdownHistoryService"/>.
/// </summary>
public interface IOperationsCalendarService
{
    /// <summary>
    /// Composes one month grid (anchored at <paramref name="displayedMonth"/>, normalized to that
    /// month's first day) plus a detail summary for <paramref name="selectedDate"/>, which may fall
    /// outside the displayed month. Local-only: no HTTP, Playwright, or Task Scheduler dependency is
    /// reachable from this call.
    /// </summary>
    Task<OperationsCalendarSnapshot> LoadAsync(DateOnly displayedMonth, DateOnly selectedDate, CancellationToken cancellationToken = default);
}
