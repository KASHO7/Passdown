namespace Carowinds.Passdown.Application.Freshness;

/// <summary>
/// Selects which dates the daily 6:00 AM refresh should prioritize for per-date
/// sources (spec §6.5): today first, then tomorrow, then the remaining window
/// days, in that order. Dates outside the window are checked only on demand
/// (spec §6.5) — this planner does not decide that; it only produces the window.
/// </summary>
public interface IRollingDatePlanner
{
    /// <summary>The window using the configured default length.</summary>
    IReadOnlyList<DateOnly> GetRefreshWindow();

    /// <summary>The window using an explicit length, overriding the configured default.</summary>
    IReadOnlyList<DateOnly> GetRefreshWindow(int windowLengthDays);
}
