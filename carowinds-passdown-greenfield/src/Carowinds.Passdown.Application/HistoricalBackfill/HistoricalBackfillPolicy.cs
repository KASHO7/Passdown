using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Time;

namespace Carowinds.Passdown.Application.HistoricalBackfill;

/// <summary>
/// Phase 20C: the rules of the current-year historical park-hours backfill — what is in scope and
/// when a stored date counts as done. Pure functions over the operational (Eastern) date and existing
/// <c>dated_results</c> rows; nothing here is persisted separately, so progress is always derived
/// from the normalized data itself (which is what makes the backfill resumable and idempotent).
/// </summary>
public static class HistoricalBackfillPolicy
{
    /// <summary>
    /// The automatic backfill scope for <paramref name="todayEastern"/>: January 1 of the current
    /// Eastern calendar year through yesterday. Today and later belong to the operational refresh
    /// (priorities 1–3) and are never part of it. <see langword="null"/> on January 1 — there is no
    /// past date in the new year yet, and the previous year is never backfilled automatically.
    /// </summary>
    public static (DateOnly Start, DateOnly End)? TargetRange(DateOnly todayEastern)
    {
        var start = new DateOnly(todayEastern.Year, 1, 1);
        var end = todayEastern.AddDays(-1);
        return end < start ? null : (start, end);
    }

    /// <summary>
    /// True when a stored park-hours row already holds the official answer for its date as a past date:
    /// a successful completion (Verified, Closed, NotPublished or Unavailable — the existing
    /// last-known-good semantics) that was checked on or after the date itself. A value captured only
    /// before the date (e.g. a month-ahead forecast never re-checked because the app was closed) is
    /// eligible for one post-date confirmation; an error-only row is never complete.
    /// </summary>
    public static bool IsComplete(DatedResultRecord? record, DateOnly date) =>
        record?.SuccessEvidence is { } success
        && DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(success.TimeChecked.ToUniversalTime()).DateTime) >= date;
}

/// <summary>Phase 20C: pacing knobs. Defaults are the shipped behavior; tests shrink the delays to zero.</summary>
public sealed record HistoricalBackfillOptions
{
    /// <summary>How long after the operational startup work finishes before history starts ("starts later in background").</summary>
    public TimeSpan StartDelay { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>Pause between two historical requests — one request per date, never a burst.</summary>
    public TimeSpan RequestSpacing { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How long history stays quiet after yielding to operational work (and between "is the gate free yet?" checks).</summary>
    public TimeSpan YieldQuietPeriod { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Consecutive transient failures (offline, outage) after which this session stops; the next app start resumes.</summary>
    public int MaxConsecutiveFailures { get; init; } = 3;

    /// <summary>Stored dates per local change notification, so a viewed Calendar month refreshes in steps rather than once a second.</summary>
    public int ChangeBatchSize { get; init; } = 7;
}
