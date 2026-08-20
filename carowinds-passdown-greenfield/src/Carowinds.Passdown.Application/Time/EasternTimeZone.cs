namespace Carowinds.Passdown.Application.Time;

/// <summary>
/// Converts an instant to America/New_York wall-clock time, correctly accounting
/// for the EST/EDT transition (spec §6.6). Never assume a fixed UTC offset for
/// Eastern time — <see cref="TimeZoneInfo.ConvertTime(DateTimeOffset, TimeZoneInfo)"/>
/// applies the IANA rules for whatever date is being converted, so this is safe
/// across a DST boundary regardless of what offset the input instant carries.
/// </summary>
public static class EasternTimeZone
{
    public static TimeZoneInfo Instance { get; } = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    /// <summary>The given instant, re-expressed with Eastern's offset for the date it falls on.</summary>
    public static DateTimeOffset ConvertFromUtc(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, Instance);
}
