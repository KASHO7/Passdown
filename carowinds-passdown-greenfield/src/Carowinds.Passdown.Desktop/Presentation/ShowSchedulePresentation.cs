using Carowinds.Passdown.Application.Shows;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Shows;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>One show line: name, its times, and its venue when published.</summary>
/// <param name="TimesText">For today: the performances that have not started yet; otherwise every performance.</param>
/// <param name="EarlierText">For today only: "+2 earlier" when some performances have already started; never a claim that a show is still running.</param>
/// <param name="Key">Phase 23B: the official show id (the name when the feed gives none) — what Dashboard expansion state is keyed on, never a list index.</param>
/// <param name="Times">Phase 23B: the same start times as <see cref="TimesText"/>, one formatted entry each, in operating-day order.</param>
public sealed record ShowRowPresentation(string Name, string TimesText, string? Venue, string? EarlierText, string Key, IReadOnlyList<string> Times)
{
    public bool HasVenue => Venue is not null;

    public bool HasEarlierText => EarlierText is not null;
}

/// <summary>
/// Phase 23B: the Dashboard's compact performance-times rule. A show with at most <see cref="CollapsedLimit"/>
/// times lists them all with no control; a longer list starts collapsed to its first
/// <see cref="CollapsedVisibleCount"/> times plus "+N more" (so N is always at least 2 — a "+1 more" control
/// would take the space of the time it hides). Expanding lists every time. Nothing is ever dropped silently.
/// </summary>
public static class ShowTimesDisclosure
{
    public const int CollapsedLimit = 5;

    public const int CollapsedVisibleCount = 4;

    public static bool CanCollapse(int timeCount) => timeCount > CollapsedLimit;

    public static int VisibleCount(int timeCount, bool isExpanded) =>
        isExpanded || !CanCollapse(timeCount) ? timeCount : CollapsedVisibleCount;

    public static int HiddenCount(int timeCount, bool isExpanded) => timeCount - VisibleCount(timeCount, isExpanded);

    /// <summary>"11:15 AM · 11:30 AM" with a no-break space inside each time, so a wrapped line never splits "11:15" from "AM".</summary>
    public static string Join(IEnumerable<string> times) =>
        string.Join(" · ", times.Select(time => time.Replace(' ', '\u00A0')));
}

/// <summary>
/// Phase 21C: display text for one date's official show schedule, shared by the Dashboard ("Today's
/// Shows") and the Operations Calendar's selected-date detail so the empty states read identically.
/// Only <see cref="ShowScheduleState.NoShows"/> ever says "No shows scheduled"; an unpublished, unchecked
/// or failed schedule says so instead. Today's view orders shows by their next performance and moves
/// shows whose performances have all started to <see cref="StartedEarlierText"/> — the feed publishes no
/// durations, so nothing here guesses whether a started show is still running.
/// </summary>
public sealed record ShowSchedulePresentation(
    string Heading,
    IReadOnlyList<ShowRowPresentation> Rows,
    string? StatusText,
    string? StartedEarlierText,
    string? FreshnessText,
    bool IsProblem)
{
    public bool HasRows => Rows.Count > 0;

    public bool HasStatusText => StatusText is not null;

    public bool HasStartedEarlierText => StartedEarlierText is not null;

    public bool HasFreshnessText => FreshnessText is not null;

    public static ShowSchedulePresentation Build(ShowScheduleView view, DateTimeOffset nowUtc, bool isDashboard = false)
    {
        ArgumentNullException.ThrowIfNull(view);

        var nowEastern = EasternTimeZone.ConvertFromUtc(nowUtc);
        var today = DateOnly.FromDateTime(nowEastern.DateTime);
        var isToday = view.Date == today;
        var dayText = view.Date.ToString("MMMM d", PresentationCulture.UnitedStates);
        var heading = isDashboard ? "Today's Shows" : $"Shows — {dayText}";
        var subject = isToday ? "today" : $"on {dayText}";
        var scheduleSubject = isToday ? "Today's show schedule" : $"The show schedule for {dayText}";

        var statusText = view.State switch
        {
            ShowScheduleState.NoShows => $"No shows scheduled {subject}.",
            ShowScheduleState.NotPublished => $"{scheduleSubject} is not published.",
            ShowScheduleState.ParkClosed => $"No shows — the park is closed {subject}.",
            ShowScheduleState.CouldNotCheck => isToday ? "Couldn't check today's shows." : $"Couldn't check shows for {dayText}.",
            ShowScheduleState.HistoricalNotAvailable => "Show schedule not available for this past date.",
            ShowScheduleState.NotChecked => isToday ? "Today's shows haven't been checked yet." : $"Shows for {dayText} haven't been checked yet.",
            _ => null,
        };

        var rows = new List<ShowRowPresentation>();
        string? startedEarlier = null;
        if (view.State == ShowScheduleState.Scheduled)
        {
            if (isToday)
            {
                var upcoming = view.Shows
                    .Select(show => (Show: show, Remaining: show.Times.Where(time => ShowTimes.StartsAt(view.Date, time) > nowEastern.DateTime).ToList()))
                    .ToList();

                rows.AddRange(upcoming
                    .Where(entry => entry.Remaining.Count > 0)
                    .OrderBy(entry => ShowTimes.OperatingDayOffset(entry.Remaining[0]))
                    .ThenBy(entry => entry.Show.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(entry =>
                    {
                        var earlier = entry.Show.Times.Count - entry.Remaining.Count;
                        return new ShowRowPresentation(
                            entry.Show.Name,
                            ShowTimes.FormatList(entry.Remaining, " · "),
                            entry.Show.Venue,
                            earlier > 0 ? $"+{earlier} earlier" : null,
                            KeyOf(entry.Show),
                            FormatEach(entry.Remaining));
                    }));

                var finished = upcoming.Where(entry => entry.Remaining.Count == 0).Select(entry => entry.Show.Name).ToList();
                if (finished.Count > 0)
                {
                    startedEarlier = $"Started earlier today: {string.Join(", ", finished)}";
                }

                if (rows.Count == 0)
                {
                    statusText = "All of today's scheduled performances have started.";
                }
            }
            else
            {
                rows.AddRange(view.Shows.Select(show => new ShowRowPresentation(show.Name, ShowTimes.FormatList(show.Times, " · "), show.Venue, null, KeyOf(show), FormatEach(show.Times))));
            }
        }

        return new ShowSchedulePresentation(
            heading,
            rows,
            statusText,
            startedEarlier,
            BuildFreshnessText(view, nowUtc),
            view.State == ShowScheduleState.CouldNotCheck || view.LastCheckFailed);
    }

    /// <summary>Phase 23B: the official show id, or the trimmed name when the feed gives none (the same identity rule as <see cref="CalendarShowsPresentation"/>).</summary>
    internal static string KeyOf(Application.Retrieval.ScheduledShow show) =>
        string.IsNullOrWhiteSpace(show.ShowId) ? show.Name.Trim() : show.ShowId;

    /// <summary>Exactly the times <see cref="ShowTimes.FormatList"/> joins, one entry each (distinct, operating-day order).</summary>
    private static IReadOnlyList<string> FormatEach(IEnumerable<TimeOnly> times) =>
        ShowTimes.Normalize(times).Select(ShowTimes.Format).ToList();

    /// <summary>"Official schedule checked 10:32 AM ET" — plus "latest check failed" when older verified data is being kept.</summary>
    /// <remarks>Phase 22E: shared with <see cref="CalendarShowsPresentation"/> so both surfaces word freshness identically.</remarks>
    internal static string? BuildFreshnessText(ShowScheduleView view, DateTimeOffset nowUtc)
    {
        if (view.LastVerifiedAt is not { } verifiedAt)
        {
            return null;
        }

        var when = EasternTimestampFormat.Relative(verifiedAt, nowUtc);
        return view.LastCheckFailed
            ? $"Official schedule checked {when} · latest check failed"
            : $"Official schedule checked {when}";
    }
}
