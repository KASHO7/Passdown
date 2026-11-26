using Carowinds.Passdown.Application.Seasons;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Seasons;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>One official event that applies to the selected date, as shown in "Include in Passdown".</summary>
public sealed record SeasonEventLine(string Name, string DateText, string StatusText, Uri SourceUrl);

/// <summary>
/// Phase 31: the selected date's official season, as shown above the "Include in Passdown" options —
/// which official event(s) apply, with their official date wording and freshness, and whether the
/// seasonal SCarowinds / Haunt option is offered for this date. Pure formatting of the composed
/// <see cref="AssociateSeasonView"/>; it never changes a saved selection.
/// </summary>
public sealed record SeasonContextPresentation(
    string HeadingText,
    IReadOnlyList<SeasonEventLine> Events,
    string? NoEventText,
    string? UpcomingText,
    string? RecentlyEndedText,
    string FreshnessText,
    bool IsFreshnessWarning,
    bool ShowHauntedOption,
    string? HauntedNote)
{
    public bool HasEvents => Events.Count > 0;

    public bool HasNoEventText => NoEventText is not null;

    public bool HasUpcomingText => UpcomingText is not null;

    public bool HasRecentlyEndedText => RecentlyEndedText is not null;

    public bool HasHauntedNote => HauntedNote is not null;

    /// <param name="season">The composed season context for the selected date.</param>
    /// <param name="hauntedSelected">The saved "SCarowinds / Haunt" section choice (never modified here).</param>
    /// <param name="hasHauntedProductsForDate">True when official SCarowinds prices exist for this exact date.</param>
    public static SeasonContextPresentation Build(AssociateSeasonView season, bool hauntedSelected, bool hasHauntedProductsForDate)
    {
        ArgumentNullException.ThrowIfNull(season);
        var culture = PresentationCulture.UnitedStates;
        var resolution = season.Resolution;
        var heading = $"Official events for {season.OperatingDate.ToString("dddd, MMMM d, yyyy", culture)}";

        var events = resolution.Applicable
            .Select(a => new SeasonEventLine(
                OperatingEventKinds.DisplayName(a.Event.Kind),
                a.Event.DateText,
                StatusText(a.Status, season.ParkDay),
                a.Event.SourceUrl))
            .ToList();

        string? noEvent = null;
        if (!resolution.ScheduleKnown)
        {
            noEvent = "Event schedule not available yet — it is checked with the other official data.";
        }
        else if (events.Count == 0)
        {
            noEvent = "No official seasonal event is published for this date.";
        }

        var upcoming = resolution.Upcoming.FirstOrDefault() is { } next
            ? $"Next: {OperatingEventKinds.DisplayName(next.Kind)} begins {next.StartDate.ToString("dddd, MMMM d, yyyy", culture)} ({next.DateText})."
            : null;

        var ended = resolution.RecentlyEnded.FirstOrDefault() is { } last && events.All(e => e.Name != OperatingEventKinds.DisplayName(last.Kind))
            ? $"{OperatingEventKinds.DisplayName(last.Kind)} {last.EndDate.Year} ended {last.EndDate.ToString("MMMM d, yyyy", culture)}."
            : null;

        var (freshness, warning) = Freshness(season.ScheduleStatus);

        var (showHaunted, hauntedNote) = HauntedOption(season, hauntedSelected, hasHauntedProductsForDate);

        return new SeasonContextPresentation(heading, events, noEvent, upcoming, ended, freshness, warning, showHaunted, hauntedNote);
    }

    private static string StatusText(EventDayStatus status, ParkDayOperation parkDay) => status switch
    {
        EventDayStatus.NotOperatingThatDay => "Park closed this date",
        EventDayStatus.WithinSelectDateRange => "Select dates — this date is not confirmed as an event day until its park hours are published",
        _ when parkDay == ParkDayOperation.Open => "Park open this date",
        _ => "Within the official dates",
    };

    /// <summary>
    /// The seasonal SCarowinds / Haunt option is offered when SCarowinds applies to the date, when
    /// official SCarowinds prices exist for the date (a past SCarowinds date keeps its relevance), or
    /// when the schedule is unknown (never hidden on missing data). Otherwise it is not offered; a
    /// saved "included" choice is still shown with an explanation instead of being silently removed.
    /// </summary>
    private static (bool Show, string? Note) HauntedOption(AssociateSeasonView season, bool hauntedSelected, bool hasHauntedProductsForDate)
    {
        var resolution = season.Resolution;
        if (!resolution.ScheduleKnown || hasHauntedProductsForDate ||
            resolution.Applicable.Any(a => a.Event.Kind == OperatingEventKind.SCarowinds))
        {
            return (true, null);
        }

        var latest = season.Schedule?.Events
            .Where(e => e.Kind == OperatingEventKind.SCarowinds)
            .OrderByDescending(e => e.StartDate)
            .FirstOrDefault();
        var dates = latest is null
            ? "no official SCarowinds dates are published"
            : $"official {latest.StartDate.Year} dates: {latest.DateText}";

        return hauntedSelected
            ? (true, $"SCarowinds is not running on this date ({dates}). Your saved choice is kept; nothing prints for it unless official SCarowinds prices exist for this date.")
            : (false, $"SCarowinds / Haunt is not offered for this date ({dates}).");
    }

    internal static (string Text, bool Warning) Freshness(OfficialDocumentStatus status) => status.Freshness switch
    {
        OfficialDocumentFreshness.NotChecked => ("Event schedule not checked yet.", true),
        OfficialDocumentFreshness.LastCheckFailed when status.LastSuccessAt is { } success =>
            ($"Last check failed {When(status.LastAttemptAt)}; showing the schedule verified {When(success)}.", true),
        OfficialDocumentFreshness.LastCheckFailed => ($"Last check failed {When(status.LastAttemptAt)}; no verified schedule yet.", true),
        _ => ($"Official event schedule checked {When(status.LastSuccessAt)}.", false),
    };

    internal static string When(DateTimeOffset? instant) => instant is { } value
        ? EasternTimeZone.ConvertFromUtc(value).ToString("MMM d, yyyy 'at' h:mm tt", PresentationCulture.UnitedStates)
        : "at an unknown time";
}
