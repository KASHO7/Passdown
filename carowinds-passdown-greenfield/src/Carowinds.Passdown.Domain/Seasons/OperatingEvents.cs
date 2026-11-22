namespace Carowinds.Passdown.Domain.Seasons;

/// <summary>
/// Phase 31: which recurring Carowinds operating event an official event entry is. The kind only
/// identifies the event (its brand); whether it applies to a date is decided solely by the official
/// dates published for it (<see cref="OfficialOperatingEvent"/>), never by the calendar month or by
/// the name alone.
/// </summary>
public enum OperatingEventKind
{
    /// <summary>Any other official listing (education days, group programs, performances) — stored, never treated as a season.</summary>
    Other = 0,
    SCarowinds,
    Oktoberfest,
    WinterFest,
    NewYearsEve,
}

public static class OperatingEventKinds
{
    /// <summary>
    /// Classifies an official event title by its brand name (case-insensitive). Brand words only —
    /// never a full, year-specific title — so a renamed or re-sponsored event ("Oktoberfest Presented
    /// by …") is still recognized.
    /// </summary>
    public static OperatingEventKind FromOfficialTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return OperatingEventKind.Other;
        }

        if (title.Contains("SCarowinds", StringComparison.OrdinalIgnoreCase))
        {
            return OperatingEventKind.SCarowinds;
        }

        if (title.Contains("Oktoberfest", StringComparison.OrdinalIgnoreCase))
        {
            return OperatingEventKind.Oktoberfest;
        }

        // "WinterFest Theatre Arts Day" is a school program held during the season, not the season.
        if (title.Contains("WinterFest", StringComparison.OrdinalIgnoreCase) &&
            !title.Contains(" Day", StringComparison.OrdinalIgnoreCase))
        {
            return OperatingEventKind.WinterFest;
        }

        if (title.Contains("New Year", StringComparison.OrdinalIgnoreCase))
        {
            return OperatingEventKind.NewYearsEve;
        }

        return OperatingEventKind.Other;
    }

    /// <summary>True for the events that define what an associate is selling for a day (the operating seasons).</summary>
    public static bool IsSeasonal(OperatingEventKind kind) => kind != OperatingEventKind.Other;

    public static string DisplayName(OperatingEventKind kind) => kind switch
    {
        OperatingEventKind.SCarowinds => "SCarowinds",
        OperatingEventKind.Oktoberfest => "Oktoberfest",
        OperatingEventKind.WinterFest => "WinterFest",
        OperatingEventKind.NewYearsEve => "New Year's Eve",
        _ => "Event",
    };
}

/// <summary>
/// Phase 31: one official event occurrence as published by Carowinds (its event listing or event
/// page). <see cref="StartDate"/>/<see cref="EndDate"/> are the advertised, inclusive range; when
/// <see cref="IsSelectDates"/> is true the official wording says the event runs only on select
/// days/nights inside that range, so a date inside it is not by itself proof the event runs that
/// day. <see cref="EventId"/> is the official CMS id when the page provides one, otherwise a stable
/// id derived from kind/title and start date, so the same occurrence merges across checks.
/// </summary>
public sealed record OfficialOperatingEvent(
    string EventId,
    string Title,
    OperatingEventKind Kind,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsSelectDates,
    string? DateLabel,
    Uri SourceUrl,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastListedAt)
{
    public bool Covers(DateOnly date) => date >= StartDate && date <= EndDate;

    /// <summary>The official wording when available, else a plain range ("November 7 – 22, 2026").</summary>
    public string DateText => !string.IsNullOrWhiteSpace(DateLabel)
        ? DateLabel!
        : FormatRange(StartDate, EndDate, IsSelectDates);

    public static string FormatRange(DateOnly start, DateOnly end, bool selectDates)
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        var range = start == end
            ? start.ToString("MMMM d, yyyy", culture)
            : start.Year == end.Year
                ? $"{start.ToString("MMMM d", culture)} – {end.ToString("MMMM d, yyyy", culture)}"
                : $"{start.ToString("MMMM d, yyyy", culture)} – {end.ToString("MMMM d, yyyy", culture)}";
        return selectDates ? "Select dates, " + range : range;
    }
}

/// <summary>
/// Phase 31: the last-known-good set of official event occurrences. Merging a newer successful check
/// updates the occurrences it lists and keeps every previously known occurrence it no longer lists —
/// an event dropping off the listing after it ends is normal, and its dates stay the authority for
/// historical dates. Missing data is never treated as proof an event ended.
/// </summary>
public sealed record OperatingEventSchedule(IReadOnlyList<OfficialOperatingEvent> Events)
{
    public static OperatingEventSchedule Empty { get; } = new([]);

    public OperatingEventSchedule Merge(IReadOnlyList<OfficialOperatingEvent> observed)
    {
        ArgumentNullException.ThrowIfNull(observed);

        var merged = Events.ToDictionary(e => e.EventId, StringComparer.Ordinal);
        foreach (var fresh in observed)
        {
            merged[fresh.EventId] = merged.TryGetValue(fresh.EventId, out var previous)
                ? fresh with { FirstSeenAt = previous.FirstSeenAt < fresh.FirstSeenAt ? previous.FirstSeenAt : fresh.FirstSeenAt }
                : fresh;
        }

        return new OperatingEventSchedule(merged.Values
            .OrderBy(e => e.StartDate)
            .ThenBy(e => e.Title, StringComparer.Ordinal)
            .ToList());
    }
}

/// <summary>Whether the official park-hours data says the park operates on a date.</summary>
public enum ParkDayOperation
{
    Unknown = 0,
    Open,
    Closed,
}

/// <summary>How an official event relates to one selected operating date.</summary>
public enum EventDayStatus
{
    /// <summary>Inside the official range and the park is confirmed open (or the event runs every day of its range).</summary>
    Scheduled,

    /// <summary>Inside an official "select dates" range, but whether this exact day is an event day is not confirmed.</summary>
    WithinSelectDateRange,

    /// <summary>Inside the official range, but the park is officially closed that day.</summary>
    NotOperatingThatDay,
}

public sealed record EventApplicability(OfficialOperatingEvent Event, EventDayStatus Status);

/// <summary>
/// Phase 31: the official season context for one selected operating date. <see cref="ScheduleKnown"/>
/// is false when no official event schedule has ever been retrieved — callers must then treat
/// seasonal questions as unknown, not as "no event".
/// </summary>
public sealed record SeasonResolution(
    DateOnly OperatingDate,
    bool ScheduleKnown,
    IReadOnlyList<EventApplicability> Applicable,
    IReadOnlyList<OfficialOperatingEvent> Upcoming,
    IReadOnlyList<OfficialOperatingEvent> RecentlyEnded)
{
    public static SeasonResolution Unknown(DateOnly date) => new(date, ScheduleKnown: false, [], [], []);

    /// <summary>True when the event kind applies to the date (scheduled or inside a select-date range).</summary>
    public bool Offers(OperatingEventKind kind) =>
        Applicable.Any(a => a.Event.Kind == kind && a.Status != EventDayStatus.NotOperatingThatDay);
}

/// <summary>
/// Phase 31: pure season resolution — selected date + official event occurrences + the official
/// park-day status for that date. Never reads the clock, never infers dates from a month or a name.
/// </summary>
public static class SeasonResolver
{
    /// <summary>How far ahead an event counts as "upcoming" for the selected date.</summary>
    public const int UpcomingWindowDays = 45;

    /// <summary>How far back an ended event is still mentioned (e.g. "SCarowinds ended November 7").</summary>
    public const int RecentlyEndedWindowDays = 60;

    public static SeasonResolution Resolve(DateOnly operatingDate, OperatingEventSchedule? schedule, ParkDayOperation parkDay)
    {
        if (schedule is null || schedule.Events.Count == 0)
        {
            return SeasonResolution.Unknown(operatingDate);
        }

        var seasonal = schedule.Events.Where(e => OperatingEventKinds.IsSeasonal(e.Kind)).ToList();

        var applicable = seasonal
            .Where(e => e.Covers(operatingDate))
            .Select(e => new EventApplicability(e, StatusFor(e, parkDay)))
            .OrderBy(a => a.Event.StartDate)
            .ToList();

        var upcoming = seasonal
            .Where(e => e.StartDate > operatingDate && e.StartDate.DayNumber - operatingDate.DayNumber <= UpcomingWindowDays)
            .OrderBy(e => e.StartDate)
            .ToList();

        var recentlyEnded = seasonal
            .Where(e => e.EndDate < operatingDate && operatingDate.DayNumber - e.EndDate.DayNumber <= RecentlyEndedWindowDays)
            .OrderByDescending(e => e.EndDate)
            .ToList();

        return new SeasonResolution(operatingDate, ScheduleKnown: true, applicable, upcoming, recentlyEnded);
    }

    private static EventDayStatus StatusFor(OfficialOperatingEvent occurrence, ParkDayOperation parkDay) => parkDay switch
    {
        ParkDayOperation.Closed => EventDayStatus.NotOperatingThatDay,
        ParkDayOperation.Open => EventDayStatus.Scheduled,
        _ => occurrence.IsSelectDates ? EventDayStatus.WithinSelectDateRange : EventDayStatus.Scheduled,
    };

    /// <summary>
    /// True when the official occurrence of <paramref name="kind"/> that most recently started on or
    /// before <paramref name="today"/> has ended, and no later occurrence has started. False when the
    /// schedule is unknown or has no occurrence of that kind — never an inferred "ended".
    /// </summary>
    public static bool HasEnded(OperatingEventSchedule? schedule, OperatingEventKind kind, DateOnly today, out OfficialOperatingEvent? endedOccurrence)
    {
        endedOccurrence = null;
        if (schedule is null)
        {
            return false;
        }

        var latest = schedule.Events
            .Where(e => e.Kind == kind && e.StartDate <= today)
            .OrderByDescending(e => e.StartDate)
            .FirstOrDefault();

        if (latest is null || latest.EndDate >= today)
        {
            return false;
        }

        endedOccurrence = latest;
        return true;
    }
}
