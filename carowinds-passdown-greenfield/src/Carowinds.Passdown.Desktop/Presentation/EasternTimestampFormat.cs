using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Phase 22F: the one display formatter for operational timestamps (an instant something happened —
/// saved, checked, verified, finalized, created, synced). Every such time is shown on the park's
/// America/New_York wall clock via <see cref="EasternTimeZone"/> (real IANA rules, so EST/EDT and the
/// transition days are handled; never a fixed offset) and carries a short " ET" label, so the text is the
/// same whatever time zone the Windows PC is set to. US formatting regardless of host culture.
/// <para>
/// Not for schedule times (park hours, show times — already Eastern operating-day wall-clock values) or
/// for date-only values; neither gets an "ET". Stored values are never changed — this only formats them.
/// </para>
/// </summary>
public static class EasternTimestampFormat
{
    /// <summary>The short zone label appended to every operational timestamp.</summary>
    public const string ZoneLabel = "ET";

    /// <summary>"3:45 PM ET".</summary>
    public static string Time(DateTimeOffset instant) => Format(instant, "h:mm tt");

    /// <summary>"Oct 17 at 3:45 PM ET".</summary>
    public static string DateAndTime(DateTimeOffset instant) => Format(instant, "MMM d 'at' h:mm tt");

    /// <summary>"Oct 17, 2026 at 3:45 PM ET".</summary>
    public static string DateYearAndTime(DateTimeOffset instant) => Format(instant, "MMM d, yyyy 'at' h:mm tt");

    /// <summary>"3:45 PM ET" when <paramref name="instant"/> falls on the same Eastern day as <paramref name="nowUtc"/>; otherwise "Oct 14, 3:45 PM ET".</summary>
    public static string Relative(DateTimeOffset instant, DateTimeOffset nowUtc) =>
        EasternTimeZone.ConvertFromUtc(instant).Date == EasternTimeZone.ConvertFromUtc(nowUtc).Date
            ? Format(instant, "h:mm tt")
            : Format(instant, "MMM d, h:mm tt");

    /// <summary>The instant on the Eastern wall clock in <paramref name="pattern"/> (US culture), followed by " ET".</summary>
    public static string Format(DateTimeOffset instant, string pattern) =>
        EasternTimeZone.ConvertFromUtc(instant).ToString(pattern, PresentationCulture.UnitedStates) + " " + ZoneLabel;
}
