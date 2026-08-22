namespace Carowinds.Passdown.Desktop.ScheduledRefresh;

/// <summary>
/// Deterministic process exit codes for <c>--scheduled-refresh</c> (Phase 6A Part E). A Windows
/// Task Scheduler entry (Phase 6C) will read this to decide whether the run needs attention.
/// </summary>
public enum ScheduledRefreshExitCode
{
    /// <summary>Completed with no operationally blocking failure.</summary>
    Success = 0,

    /// <summary>Completed, but one or more source operations failed.</summary>
    PartialFailure = 1,

    /// <summary>Startup/configuration/database/unhandled fatal failure — the job never produced a summary.</summary>
    FatalFailure = 2,

    /// <summary>Invalid command-line usage.</summary>
    InvalidUsage = 3,
}
