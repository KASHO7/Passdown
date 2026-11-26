using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Phase 21B: the Associate date header's text, built purely from the selected date and today's
/// operational (Eastern) date — never from the host machine's local date. "Today" is passed in so the
/// caller decides when it is read (at click/selection time, never a startup-cached value).
/// </summary>
/// <param name="DateText">"Thursday, November 26, 2026" — the full date (tooltip, screen readers, other headings).</param>
/// <param name="CompactDateText">Phase 35D follow-up: "Nov 26, 2026" — what the toolbar's date button shows.</param>
public sealed record AssociateDateNavigationPresentation(
    string DateText,
    string? RelativeText,
    bool IsToday,
    string AutomationText,
    string CompactDateText)
{
    /// <summary>
    /// Phase 35D follow-up: the toolbar's Today button is a way back to today, so it is shown only when another date is
    /// selected; on today it is hidden and takes no space (the compact control has no separate "Today" badge).
    /// </summary>
    public bool ShowsTodayAction => !IsToday;

    /// <summary>The date button's tooltip: the full date, plus "Today" / "Yesterday" / "Tomorrow" when it applies.</summary>
    public string ToolTipText => RelativeText is null ? DateText : $"{DateText} ({RelativeText})";

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

        var compactDateText = selectedDate.ToString("MMM d, yyyy", PresentationCulture.UnitedStates);
        return new AssociateDateNavigationPresentation(dateText, relativeText, dayOffset == 0, automationText, compactDateText);
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
