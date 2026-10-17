namespace Carowinds.Passdown.Application.HistoricalBackfill;

public enum HistoricalBackfillState
{
    /// <summary>Not started in this process (headless host, tests, or before the operational startup work finished).</summary>
    NotStarted,

    /// <summary>January 1: the new year has no past dates yet.</summary>
    NotApplicable,

    /// <summary>Checking past dates in the background (including while yielding to operational work).</summary>
    Running,

    /// <summary>Every current-year past date holds its official answer.</summary>
    UpToDate,

    /// <summary>
    /// The session ended with dates still missing: the official feed no longer publishes the oldest
    /// ones, or some checks failed (offline/outage) and will be tried again on a later app start.
    /// </summary>
    PartiallyAvailable,
}

/// <summary>
/// Phase 20C: the historical backfill's own status — deliberately separate from
/// <see cref="Refresh.OfficialDataStatus"/>, so a historical failure never makes the operational
/// "Official data checked …" line look broken.
/// </summary>
/// <param name="TargetYear">The Eastern calendar year being backfilled.</param>
/// <param name="DatesInScope">Past dates of <paramref name="TargetYear"/> (January 1 through yesterday).</param>
/// <param name="DatesComplete">Of those, how many already hold their official answer.</param>
/// <param name="SessionQueued">Dates this session set out to check — the fixed progress denominator.</param>
/// <param name="SessionChecked">Dates this session has checked so far (stored or answered), excluding failures.</param>
/// <param name="SessionFailed">Dates whose check failed this session (nothing stored; retried on a later start).</param>
/// <param name="OldestNotRetainedDate">The newest past date the official feed answered "Not Found" for — official history ends after it.</param>
/// <param name="LastActivityAtUtc">When the backfill last made a request or finished a session.</param>
public sealed record HistoricalBackfillStatus(
    int TargetYear,
    HistoricalBackfillState State,
    int DatesInScope = 0,
    int DatesComplete = 0,
    int SessionQueued = 0,
    int SessionChecked = 0,
    int SessionFailed = 0,
    DateOnly? OldestNotRetainedDate = null,
    DateTimeOffset? LastActivityAtUtc = null)
{
    public static HistoricalBackfillStatus NotStarted(int targetYear) => new(targetYear, HistoricalBackfillState.NotStarted);

    /// <summary>Dates still without their official answer (known once a session has read the database).</summary>
    public int DatesRemaining => Math.Max(0, DatesInScope - DatesComplete);
}
