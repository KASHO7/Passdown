using Carowinds.Passdown.Application.Shows;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Shows;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>One show in the Calendar's expanded schedule: name, then "7:00, 8:00, 9:30 PM" and the venue when published.</summary>
public sealed record CalendarShowRowPresentation(string Name, string TimesText, string? Venue, int PerformanceCount)
{
    public bool HasVenue => Venue is not null;

    /// <summary>" · Carowinds Theatre" after the times (muted), or empty when no venue is published.</summary>
    public string VenueSuffix => Venue is null ? string.Empty : $" · {Venue}";

    /// <summary>"Wake the Dead, 4 performances: 7:00, 8:00, 9:30, 10:30 PM, Carowinds Theatre".</summary>
    public string AccessibleText => $"{Name}, {CalendarShowsPresentation.Plural(PerformanceCount, "performance")}: {TimesText}{(Venue is null ? string.Empty : $", {Venue}")}";
}

/// <summary>
/// Phase 22E: the Operations Calendar's compact Shows section for one selected date — a one-line summary
/// ("6 scheduled shows · 22 performances") that expands to the full official schedule. Built only from the
/// selected date's own <see cref="ShowScheduleView"/> (the persisted last-known-good record; never a
/// network call, never another date's lineup).
/// <para>
/// Unlike the Dashboard ("Today's Shows", upcoming performances only), the Calendar is a planning view, so
/// even for today it lists every verified start time. Shows are merged by official show id (a duplicated
/// record never counts twice); performances are each show's distinct start times (<see cref="ShowTimes.Normalize"/>,
/// late-night times after the evening ones). A show with no start time is never counted — the feed only
/// returns shows with times for a Scheduled date. Only <see cref="ShowScheduleState.Scheduled"/> produces a
/// count, so "0 scheduled shows" is never shown; a confirmed empty schedule says so in words.
/// </para>
/// </summary>
public sealed record CalendarShowsPresentation(
    DateOnly Date,
    ShowScheduleState State,
    int ShowCount,
    int PerformanceCount,
    string? SummaryText,
    string? StatusText,
    IReadOnlyList<CalendarShowRowPresentation> Rows,
    string? FreshnessText,
    bool IsProblem)
{
    /// <summary>True only when there is a verified schedule to expand (no empty "View schedule" control otherwise).</summary>
    public bool HasSchedule => Rows.Count > 0;

    public bool HasSummary => SummaryText is not null;

    public bool HasStatusText => StatusText is not null;

    public bool HasFreshnessText => FreshnessText is not null;

    public static CalendarShowsPresentation Build(ShowScheduleView view, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(view);

        var rows = view.State == ShowScheduleState.Scheduled ? BuildRows(view.Shows) : [];
        var performances = rows.Sum(row => row.PerformanceCount);

        var statusText = view.State switch
        {
            ShowScheduleState.Scheduled when rows.Count == 0 => "Show schedule unavailable.",
            ShowScheduleState.Scheduled => null,
            ShowScheduleState.NoShows => "No shows scheduled for this date.",
            ShowScheduleState.NotPublished => "Show schedule not published.",
            ShowScheduleState.ParkClosed => "No shows — park closed.",
            ShowScheduleState.CouldNotCheck => "Couldn't check the show schedule.",
            ShowScheduleState.HistoricalNotAvailable => "Show schedule not available for this past date.",
            _ => "Show schedule unavailable — not checked yet.",
        };

        return new CalendarShowsPresentation(
            view.Date,
            view.State,
            rows.Count,
            performances,
            rows.Count > 0 ? $"{Plural(rows.Count, "scheduled show")} · {Plural(performances, "performance")}" : null,
            statusText,
            rows,
            ShowSchedulePresentation.BuildFreshnessText(view, nowUtc),
            view.State == ShowScheduleState.CouldNotCheck || view.LastCheckFailed);
    }

    /// <summary>"1 performance", "22 performances" (en-US grouping for large counts).</summary>
    internal static string Plural(int count, string noun) => $"{OperationalCount.Format(count)} {noun}{(count == 1 ? string.Empty : "s")}";

    private static List<CalendarShowRowPresentation> BuildRows(IReadOnlyList<Application.Retrieval.ScheduledShow> shows) =>
        shows
            .GroupBy(show => string.IsNullOrWhiteSpace(show.ShowId) ? show.Name.Trim() : show.ShowId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var times = ShowTimes.Normalize(group.SelectMany(show => show.Times));
                var first = group.First();
                var venue = group.Select(show => show.Venue).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                return (Times: times, Row: new CalendarShowRowPresentation(first.Name, ShowTimes.FormatCompactList(times), venue, times.Count));
            })
            .Where(entry => entry.Times.Count > 0)
            .OrderBy(entry => ShowTimes.OperatingDayOffset(entry.Times[0]))
            .ThenBy(entry => entry.Row.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry.Row)
            .ToList();
}
