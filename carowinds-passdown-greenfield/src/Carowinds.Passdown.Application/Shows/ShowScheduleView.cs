using System.Text.Json;
using Carowinds.Passdown.Application.Freshness;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Domain.Shows;

namespace Carowinds.Passdown.Application.Shows;

/// <summary>Phase 21C: what is known about one date's official show schedule. Every consumer (Dashboard, Operations Calendar, Associate, PDF) reads this one classification.</summary>
public enum ShowScheduleState
{
    /// <summary>No official answer has been recorded for this current/future date yet.</summary>
    NotChecked,

    /// <summary>Verified: at least one show has published times for the date.</summary>
    Scheduled,

    /// <summary>Verified: the park is open with published hours and the official feed lists no shows.</summary>
    NoShows,

    /// <summary>The official feed has no show times for the date yet (shows may be listed without times).</summary>
    NotPublished,

    /// <summary>The park is officially closed on the date.</summary>
    ParkClosed,

    /// <summary>Only failed attempts so far — nothing verified to show.</summary>
    CouldNotCheck,

    /// <summary>A past date with no schedule recorded for it; current schedules are never substituted.</summary>
    HistoricalNotAvailable,
}

/// <summary>
/// Phase 21C: the official show schedule for one operating date, composed from the persisted
/// last-known-good record only — never a network call. <see cref="Shows"/> is non-empty only for
/// <see cref="ShowScheduleState.Scheduled"/>. <see cref="LastCheckFailed"/> marks verified data kept
/// after a later attempt failed (shown, never presented as newly verified).
/// </summary>
public sealed record ShowScheduleView(
    DateOnly Date,
    ShowScheduleState State,
    IReadOnlyList<ScheduledShow> Shows,
    bool LastCheckFailed,
    DateTimeOffset? LastVerifiedAt,
    DateTimeOffset? LastCheckedAt)
{
    public static ShowScheduleView NotCheckedFor(DateOnly date) => new(date, ShowScheduleState.NotChecked, [], false, null, null);

    /// <summary>True when the official source has answered for this date (any verified/closed/not-published outcome).</summary>
    public bool HasOfficialAnswer => State is ShowScheduleState.Scheduled or ShowScheduleState.NoShows or ShowScheduleState.NotPublished or ShowScheduleState.ParkClosed;

    /// <summary>Performances that have not started yet at <paramref name="nowEastern"/>, in start order. No duration is published, so a started show is never assumed to still be running.</summary>
    public IReadOnlyList<(ScheduledShow Show, TimeOnly Time)> UpcomingPerformances(DateTime nowEastern) =>
        Shows
            .SelectMany(show => show.Times.Select(time => (Show: show, Time: time)))
            .Where(performance => ShowTimes.StartsAt(Date, performance.Time) > nowEastern)
            .OrderBy(performance => ShowTimes.OperatingDayOffset(performance.Time))
            .ThenBy(performance => performance.Show.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Composes the view from the persisted record for <paramref name="date"/>. Past dates show only
    /// what was recorded for that exact date; with nothing recorded they are
    /// <see cref="ShowScheduleState.HistoricalNotAvailable"/> — never another date's lineup.
    /// </summary>
    public static ShowScheduleView Compose(DateOnly date, DatedResultRecord? record, DateTimeOffset asOfUtc)
    {
        var today = DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(asOfUtc).DateTime);
        var isPast = date < today;

        if (record is null)
        {
            return isPast
                ? new ShowScheduleView(date, ShowScheduleState.HistoricalNotAvailable, [], false, null, null)
                : NotCheckedFor(date);
        }

        var lastKnownGood = record.ToLastKnownGood();
        var success = lastKnownGood.SuccessEvidence;
        var lastCheckedAt = lastKnownGood.LastAttemptEvidence?.TimeChecked ?? success?.TimeChecked;

        if (success is null)
        {
            var state = isPast ? ShowScheduleState.HistoricalNotAvailable : ShowScheduleState.CouldNotCheck;
            return new ShowScheduleView(date, state, [], LastCheckFailed: true, null, lastCheckedAt);
        }

        var failedSince = lastKnownGood.IsStale;
        switch (success.Status)
        {
            case RetrievalStatus.Verified:
                var payload = Deserialize(lastKnownGood.SuccessValue);
                if (payload is null)
                {
                    return new ShowScheduleView(date, isPast ? ShowScheduleState.HistoricalNotAvailable : ShowScheduleState.CouldNotCheck, [], true, null, lastCheckedAt);
                }

                return new ShowScheduleView(
                    date,
                    payload.Shows.Count > 0 ? ShowScheduleState.Scheduled : ShowScheduleState.NoShows,
                    payload.Shows,
                    failedSince,
                    success.TimeChecked,
                    lastCheckedAt);

            case RetrievalStatus.Closed:
                return new ShowScheduleView(date, ShowScheduleState.ParkClosed, [], failedSince, success.TimeChecked, lastCheckedAt);

            case RetrievalStatus.NotPublished:
                return new ShowScheduleView(date, isPast ? ShowScheduleState.HistoricalNotAvailable : ShowScheduleState.NotPublished, [], failedSince, success.TimeChecked, lastCheckedAt);

            default:
                return new ShowScheduleView(date, isPast ? ShowScheduleState.HistoricalNotAvailable : ShowScheduleState.CouldNotCheck, [], failedSince, null, lastCheckedAt);
        }
    }

    private static ShowSchedulePayload? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<ShowSchedulePayload>(json);
            return payload is { Shows: not null } ? payload : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
