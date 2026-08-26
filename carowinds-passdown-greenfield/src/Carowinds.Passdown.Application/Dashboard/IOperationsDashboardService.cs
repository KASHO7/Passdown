namespace Carowinds.Passdown.Application.Dashboard;

/// <summary>
/// The Application-layer seam a supervisor's Operations Dashboard is built from (Phase 10A Part J):
/// composes <see cref="OperationsDashboardSnapshot"/> entirely from existing local
/// Associate/finalization state. Never performs HTTP/Playwright retrieval itself — opening the
/// dashboard must never trigger a network check (Phase 10A Part K).
/// </summary>
public interface IOperationsDashboardService
{
    /// <summary>
    /// Today's snapshot, where "today" is the current date in America/New_York (Phase 10A Part D) —
    /// deliberately independent of whatever date the Associate editor currently has selected.
    /// </summary>
    Task<OperationsDashboardSnapshot> GetTodayAsync(CancellationToken cancellationToken = default);

    /// <summary>The snapshot for an arbitrary operating date, for future callers/tests that need a date other than today.</summary>
    Task<OperationsDashboardSnapshot> LoadAsync(DateOnly operatingDate, CancellationToken cancellationToken = default);
}
