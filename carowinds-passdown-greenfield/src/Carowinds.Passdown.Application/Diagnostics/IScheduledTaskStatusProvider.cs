namespace Carowinds.Passdown.Application.Diagnostics;

/// <summary>
/// Reports whether the "Carowinds Passdown Daily Refresh" Windows Task Scheduler task is
/// registered (Phase 7F Part K) — existence only. Must never throw: an unavailable platform, a
/// missing <c>schtasks</c>, or any query failure is reported as
/// <see cref="ScheduledTaskDiagnosticStatus.NotAvailableOnThisPlatform"/> or
/// <see cref="ScheduledTaskDiagnosticStatus.Unknown"/>, never surfaced as an exception that would
/// fail the whole diagnostic export (Part K "must not fail solely because Task Scheduler status
/// cannot be read").
/// </summary>
public interface IScheduledTaskStatusProvider
{
    Task<ScheduledTaskDiagnostics> GetStatusAsync(CancellationToken cancellationToken = default);
}
