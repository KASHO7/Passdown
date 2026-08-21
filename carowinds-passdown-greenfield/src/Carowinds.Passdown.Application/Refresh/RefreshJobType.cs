namespace Carowinds.Passdown.Application.Refresh;

/// <summary>Which refresh operation produced a <see cref="RefreshSummary"/> (Phase 3G Parts B/C).</summary>
public enum RefreshJobType
{
    /// <summary>The due-sources rolling-window job (spec §6.5/§6.6) — every source not yet due is skipped entirely.</summary>
    ScheduledRollingWindow,

    /// <summary>A user-initiated "Check this date online" for one exact date (spec §6.3) — always runs every dated source for that date, regardless of due-ness, and never touches scheduled-cadence state.</summary>
    ManualSingleDate,
}
