using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Phase 21B: the Associate date header's text, built purely from the selected date and today's
/// operational (Eastern) date — never from the host machine's local date. "Today" is passed in so the
/// caller decides when it is read (at click/selection time, never a startup-cached value).
/// </summary>
public sealed record AssociateDateNavigationPresentation(
    string DateText,
    string? RelativeText,
    bool IsToday,
    string AutomationText)
{
    public static AssociateDateNavigationPresentation Build(DateOnly selectedDate, DateOnly easternToday)
    {
        var dateText = selectedDate.ToString("dddd, MMMM d, yyyy", PresentationCulture.UnitedStates);
        var dayOffset = selectedDate.DayNumber - easternToday.DayNumber;
        var relativeText = dayOffset switch
        {
            0 => "Today",
            -1 => "Yesterday",
            1 => "Tomorrow",
            _ => null,
        };

        // Selection is spoken as text, never conveyed by color alone.
        var automationText = relativeText is null
            ? $"Selected date: {dateText}. Choose a different date."
            : $"Selected date: {dateText} ({relativeText}). Choose a different date.";

        return new AssociateDateNavigationPresentation(dateText, relativeText, dayOffset == 0, automationText);
    }

    /// <summary>
    /// The calendar popup's built-in "today" highlight is drawn from the host's local date. Show it only
    /// when that date agrees with the Eastern operational date, so the popup never marks a different day
    /// as "today" than the Today button would select (e.g. a host in another time zone near midnight).
    /// </summary>
    public static bool ShouldHighlightHostToday(DateTimeOffset utcNow, TimeZoneInfo hostTimeZone, DateOnly easternToday)
    {
        ArgumentNullException.ThrowIfNull(hostTimeZone);
        var hostToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, hostTimeZone).DateTime);
        return hostToday == easternToday;
    }
}
