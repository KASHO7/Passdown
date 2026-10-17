namespace Carowinds.Passdown.Domain.Shows;

/// <summary>
/// Phase 21C: pure rules for official show performance times on one Carowinds operating date. The
/// official feed publishes only America/New_York wall-clock start times ("07:00 PM") for the date —
/// no duration, no end time, no time zone — so everything here works on <see cref="TimeOnly"/> and the
/// operating date, never on UTC.
/// <para>
/// Late-night SCarowinds nights run to midnight, so a start time in the small hours (before
/// <see cref="OperatingDayRollover"/>) belongs to the end of the operating day, not its beginning:
/// "11:30 PM, 12:15 AM" orders 11:30 PM first, and 12:15 AM starts on the next calendar date.
/// </para>
/// </summary>
public static class ShowTimes
{
    /// <summary>Start times earlier than this belong to the end of the operating day (after midnight).</summary>
    public static readonly TimeOnly OperatingDayRollover = new(4, 0);

    /// <summary>Distinct times in operating-day order (late-night times after the evening ones).</summary>
    public static IReadOnlyList<TimeOnly> Normalize(IEnumerable<TimeOnly> times)
    {
        ArgumentNullException.ThrowIfNull(times);
        return times.Distinct().OrderBy(OperatingDayOffset).ToList();
    }

    /// <summary>Position of <paramref name="time"/> within the operating day, from midnight at its start.</summary>
    public static TimeSpan OperatingDayOffset(TimeOnly time) =>
        time < OperatingDayRollover ? time.ToTimeSpan() + TimeSpan.FromDays(1) : time.ToTimeSpan();

    /// <summary>The Eastern wall-clock date and time a performance on <paramref name="operatingDate"/> starts.</summary>
    public static DateTime StartsAt(DateOnly operatingDate, TimeOnly time) =>
        operatingDate.ToDateTime(TimeOnly.MinValue) + OperatingDayOffset(time);

    /// <summary>"1:00 PM" — standard U.S. format, no seconds.</summary>
    public static string Format(TimeOnly time) => time.ToString("h:mm tt", PresentationCulture.UnitedStates);

    /// <summary>"1:00 PM, 3:00 PM, 7:00 PM".</summary>
    public static string FormatList(IEnumerable<TimeOnly> times, string separator = ", ") =>
        string.Join(separator, Normalize(times).Select(Format));

    /// <summary>
    /// The space-saving form for dense layouts: the AM/PM marker is written once per run of times that
    /// share it — "1:00, 3:00 PM" and "11:30 AM, 1:00, 3:00 PM". Every time keeps its exact minutes.
    /// </summary>
    public static string FormatCompactList(IEnumerable<TimeOnly> times)
    {
        var ordered = Normalize(times);
        var parts = new List<string>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var current = ordered[i];
            var isLastOfPeriod = i == ordered.Count - 1 || IsMorning(ordered[i + 1]) != IsMorning(current);
            parts.Add(isLastOfPeriod
                ? Format(current)
                : current.ToString("h:mm", PresentationCulture.UnitedStates));
        }

        return string.Join(", ", parts);
    }

    private static bool IsMorning(TimeOnly time) => time.Hour < 12;
}
