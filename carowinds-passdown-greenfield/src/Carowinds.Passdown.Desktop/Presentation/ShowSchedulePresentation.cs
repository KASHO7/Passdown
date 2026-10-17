using Carowinds.Passdown.Application.Shows;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Shows;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>One show line: name, its times, and its venue when published.</summary>
/// <param name="TimesText">For today: the performances that have not started yet; otherwise every performance.</param>
/// <param name="EarlierText">For today only: "+2 earlier" when some performances have already started; never a claim that a show is still running.</param>
public sealed record ShowRowPresentation(string Name, string TimesText, string? Venue, string? EarlierText)
{
    public bool HasVenue => Venue is not null;

    public bool HasEarlierText => EarlierText is not null;
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
                            earlier > 0 ? $"+{earlier} earlier" : null);
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
                rows.AddRange(view.Shows.Select(show => new ShowRowPresentation(show.Name, ShowTimes.FormatList(show.Times, " · "), show.Venue, null)));
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

    /// <summary>"Official schedule checked 10:32 AM" — plus "latest check failed" when older verified data is being kept.</summary>
    /// <remarks>Phase 22E: shared with <see cref="CalendarShowsPresentation"/> so both surfaces word freshness identically.</remarks>
    internal static string? BuildFreshnessText(ShowScheduleView view, DateTimeOffset nowUtc)
    {
        if (view.LastVerifiedAt is not { } verifiedAt)
        {
            return null;
        }

        var nowEastern = EasternTimeZone.ConvertFromUtc(nowUtc);
        var verifiedEastern = EasternTimeZone.ConvertFromUtc(verifiedAt);
        var when = verifiedEastern.Date == nowEastern.Date
            ? verifiedEastern.ToString("h:mm tt", PresentationCulture.UnitedStates)
            : verifiedEastern.ToString("MMM d, h:mm tt", PresentationCulture.UnitedStates);
        return view.LastCheckFailed
            ? $"Official schedule checked {when} · latest check failed"
            : $"Official schedule checked {when}";
    }
}
