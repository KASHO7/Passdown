using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Compact, day-cell-sized text for one date's <see cref="OperationsCalendarOperatingState"/> (Phase
/// 12B Part C/U) — a terser vocabulary than <see cref="ParkHoursPresentation"/>'s own badge/headline
/// text (which is sized for Associate Overview's full-width panel, not a ~60px calendar cell). Never
/// recomputes <see cref="EffectiveHours"/>, never queries a service, and never decides a freshness
/// rule of its own: <see cref="IsStale"/> is read straight off <see cref="ParkHoursPresentation.Build"/>'s
/// existing <c>StaleNoteText</c> (the same staleness signal Associate Overview already surfaces),
/// never a second freshness computation (spec Part J).
/// </summary>
public sealed record OperationsCalendarOperatingStatePresentation(
    string? CompactStateLabel,
    string? CompactHoursLine,
    bool IsStale,
    string AccessibilityText,
    string? CompactWaterparkLine = null)
{
    public bool HasCompactWaterparkLine => CompactWaterparkLine is not null;

    public static OperationsCalendarOperatingStatePresentation Build(OperationsCalendarOperatingState state, AssociateParkHoursView parkHours)
    {
        ArgumentNullException.ThrowIfNull(parkHours);
        var effective = parkHours.Effective;

        // Reuses the existing formatter's own stale detection (spec Part J: "use existing
        // source/freshness state ... do not create a second freshness system") — never recomputed here.
        var isStale = ParkHoursPresentation.Build(parkHours).StaleNoteText is not null;

        string? compactStateLabel;
        string? compactHoursLine;
        string accessibilityText;

        switch (state)
        {
            case OperationsCalendarOperatingState.Open:
                // Part D: hours alone communicate "open" cleanly; a redundant "OPEN" label would be
                // clutter in a cell this small.
                compactStateLabel = null;
                compactHoursLine = FormatCompactRange(effective.Opening, effective.Closing);
                accessibilityText = $"Open, {FormatAccessibleRange(effective.Opening, effective.Closing)}";
                break;
            case OperationsCalendarOperatingState.Closed:
                compactStateLabel = "CLOSED";
                compactHoursLine = null;
                accessibilityText = "Closed";
                break;
            case OperationsCalendarOperatingState.CustomHours:
                compactStateLabel = "CUSTOM";
                compactHoursLine = FormatCompactRange(effective.Opening, effective.Closing);
                accessibilityText = $"Custom Hours, {FormatAccessibleRange(effective.Opening, effective.Closing)}";
                break;
            case OperationsCalendarOperatingState.PrivateBuyout:
                compactStateLabel = "BUYOUT";
                compactHoursLine = FormatCompactRange(effective.Opening, effective.Closing);
                accessibilityText = $"Private Buyout, {FormatAccessibleRange(effective.Opening, effective.Closing)}";
                break;
            case OperationsCalendarOperatingState.NotPublished:
                compactStateLabel = "NOT PUBLISHED";
                compactHoursLine = null;
                accessibilityText = "Hours not published";
                break;
            case OperationsCalendarOperatingState.Unavailable:
                compactStateLabel = "UNAVAILABLE";
                compactHoursLine = null;
                accessibilityText = "Hours unavailable";
                break;
            default:
                compactStateLabel = null;
                compactHoursLine = null;
                accessibilityText = "Hours not checked yet";
                break;
        }

        if (isStale)
        {
            accessibilityText += ". Stale — showing last known information.";
        }

        // Phase 13D Part F: compact, name-prefixed line in the SAME cell as main park hours — never a
        // separate badge/card — shown only when the source actually published Carolina Harbor's hours
        // for this date (never fabricated, never a placeholder for "not operating").
        string? compactWaterparkLine = parkHours.HasWaterparkHours
            ? $"Harbor {FormatCompactRange(parkHours.WaterparkOpening, parkHours.WaterparkClosing)}"
            : null;

        if (compactWaterparkLine is not null)
        {
            accessibilityText += $". Carolina Harbor {FormatAccessibleRange(parkHours.WaterparkOpening, parkHours.WaterparkClosing)}.";
        }

        return new OperationsCalendarOperatingStatePresentation(compactStateLabel, compactHoursLine, isStale, accessibilityText, compactWaterparkLine);
    }

    /// <summary>"10 AM–10 PM" — no leading zero, minutes only shown when not on the hour, to fit a compact cell (spec Part D/C).</summary>
    private static string? FormatCompactRange(TimeOnly? opening, TimeOnly? closing) =>
        opening is null || closing is null ? null : $"{FormatCompactTime(opening.Value)}–{FormatCompactTime(closing.Value)}";

    private static string FormatCompactTime(TimeOnly time) => time.Minute == 0 ? time.ToString("h tt", PresentationCulture.UnitedStates) : time.ToString("h:mm tt", PresentationCulture.UnitedStates);

    /// <summary>"10 AM to 10 PM" (spec Part X's own literal accessibility examples use "to", not a dash).</summary>
    private static string FormatAccessibleRange(TimeOnly? opening, TimeOnly? closing) =>
        opening is null || closing is null ? "hours unavailable" : $"{FormatCompactTime(opening.Value)} to {FormatCompactTime(closing.Value)}";
}
