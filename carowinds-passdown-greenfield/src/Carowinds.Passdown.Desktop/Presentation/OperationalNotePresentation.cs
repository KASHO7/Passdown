using System.Globalization;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// A compact, list-row view of a persistent <see cref="OperationalNote"/> (Phase 11B Part E) — bounded
/// text only (no <see cref="OperationalNote.Body"/>), enough to scan the list quickly and pick a note
/// to inspect. <see cref="LifecycleState"/> is always the derived value (spec Phase 11A Part Y) for
/// whatever reference date the screen is using — never a re-derivation of relevance/lifecycle rules,
/// only formatting of an already-computed value (spec Phase 11B Part AK).
/// </summary>
public sealed record OperationalNoteRowPresentation(
    Guid Id,
    string Title,
    OperationalNoteCategory Category,
    string CategoryLabel,
    OperationalNotePriority Priority,
    string PriorityLabel,
    bool IsImportant,
    string DateRangeText,
    string? CarryForwardText,
    OperationalNoteLifecycleState LifecycleState,
    string LifecycleLabel)
{
    /// <summary>
    /// Phase 22E: the row's one schedule line — "Active · Oct 17 – Oct 24 · Expires today". The lifecycle
    /// word and the date range always show; <see cref="CarryForwardText"/> is appended only when it adds a
    /// relative fact the range does not already say ("Expires today/tomorrow", "(tomorrow)"), so the
    /// row no longer repeats "Through Oct 24" under "Oct 17 – Oct 24".
    /// </summary>
    public string ScheduleText => CarryForwardText switch
    {
        "Starts tomorrow" => $"{LifecycleLabel} · {DateRangeText} (tomorrow)",
        { } relative when OperationalNoteCarryForwardFormatter.IsRelativePhrase(relative) => $"{LifecycleLabel} · {DateRangeText} · {relative}",
        _ => $"{LifecycleLabel} · {DateRangeText}",
    };

    /// <summary>"South Gate scanner, Important, General, Active · Oct 17 – Oct 24" — what a screen reader announces for the row.</summary>
    public string AccessibleSummary => IsImportant
        ? $"{Title}, Important, {CategoryLabel}, {ScheduleText}"
        : $"{Title}, {CategoryLabel}, {PriorityLabel}, {ScheduleText}";

    public static OperationalNoteRowPresentation Build(OperationalNote note, DateOnly referenceDate)
    {
        ArgumentNullException.ThrowIfNull(note);

        var lifecycle = OperationalNoteLifecycle.Derive(note, referenceDate);
        return new OperationalNoteRowPresentation(
            note.Id,
            note.Title,
            note.Category,
            OperationalNoteCategoryOption.For(note.Category).Label,
            note.Priority,
            OperationalNotePriorityOption.For(note.Priority).Label,
            note.Priority == OperationalNotePriority.Important,
            OperationalNoteDateRangeFormatter.Format(note, lifecycle, referenceDate),
            OperationalNoteCarryForwardFormatter.Format(lifecycle, note.EffectiveFrom, note.EffectiveThrough, referenceDate),
            lifecycle,
            LifecycleLabelFor(lifecycle));
    }

    internal static string LifecycleLabelFor(OperationalNoteLifecycleState lifecycle) => lifecycle switch
    {
        OperationalNoteLifecycleState.Upcoming => "Upcoming",
        OperationalNoteLifecycleState.Active => "Active",
        OperationalNoteLifecycleState.Expired => "Expired",
        OperationalNoteLifecycleState.Resolved => "Resolved",
        _ => lifecycle.ToString(),
    };
}

/// <summary>
/// The full detail/editor-adjacent view of one <see cref="OperationalNote"/> (Phase 11B Part K/O/P/Q) —
/// includes <see cref="Body"/> and the exact effective-date values (unlike the list row), plus the
/// resolved timestamp when applicable (spec Part O).
/// </summary>
public sealed record OperationalNoteDetailPresentation(
    Guid Id,
    string Title,
    string? Body,
    string CategoryLabel,
    string PriorityLabel,
    bool IsImportant,
    string EffectiveFromText,
    string EffectiveThroughText,
    string DateRangeText,
    string? CarryForwardText,
    OperationalNoteLifecycleState LifecycleState,
    string LifecycleLabel,
    bool IsResolved,
    string? ResolvedAtText)
{
    /// <summary>Phase 22E: "Created Oct 12 at 9:00 AM" — the stored creation time, same formatting as <see cref="ResolvedAtText"/>.</summary>
    public string? CreatedText { get; init; }

    /// <summary>Phase 22E: "Updated Oct 17 at 8:00 AM" — only when the note was edited after it was created (same minute counts as unedited).</summary>
    public string? UpdatedText { get; init; }

    /// <summary>"Created … · Updated …" for the detail's metadata line.</summary>
    public string? RecordTimesText => (CreatedText, UpdatedText) switch
    {
        (null, null) => null,
        ({ } created, null) => created,
        (null, { } updated) => updated,
        ({ } created, { } updated) => $"{created} · {updated}",
    };

    public static OperationalNoteDetailPresentation Build(OperationalNote note, DateOnly referenceDate)
    {
        ArgumentNullException.ThrowIfNull(note);

        var lifecycle = OperationalNoteLifecycle.Derive(note, referenceDate);
        var isResolved = note.Status == OperationalNoteStatus.Resolved;

        return new OperationalNoteDetailPresentation(
            note.Id,
            note.Title,
            note.Body,
            OperationalNoteCategoryOption.For(note.Category).Label,
            OperationalNotePriorityOption.For(note.Priority).Label,
            note.Priority == OperationalNotePriority.Important,
            FormatDate(note.EffectiveFrom, referenceDate),
            note.EffectiveThrough is { } through ? FormatDate(through, referenceDate) : "Until resolved",
            OperationalNoteDateRangeFormatter.Format(note, lifecycle, referenceDate),
            OperationalNoteCarryForwardFormatter.Format(lifecycle, note.EffectiveFrom, note.EffectiveThrough, referenceDate),
            lifecycle,
            OperationalNoteRowPresentation.LifecycleLabelFor(lifecycle),
            isResolved,
            isResolved && note.ResolvedAtUtc is { } resolvedAt ? FormatTimestamp(resolvedAt) : null)
        {
            CreatedText = note.CreatedAtUtc == default ? null : $"Created {FormatTimestamp(note.CreatedAtUtc)}",
            UpdatedText = note.UpdatedAtUtc - note.CreatedAtUtc >= TimeSpan.FromMinutes(1) ? $"Updated {FormatTimestamp(note.UpdatedAtUtc)}" : null,
        };
    }

    private static string FormatDate(DateOnly date, DateOnly referenceDate) => OperationalNoteDateRangeFormatter.FormatDate(date, referenceDate);

    private static string FormatTimestamp(DateTimeOffset value) => EasternTimestampFormat.DateAndTime(value);
}

/// <summary>
/// Friendly effective-date-range formatting (spec Phase 11B Part T), shared by the list row and detail
/// presentation so the two can never disagree. Never displays a raw ISO database value; includes the
/// year only when it differs from <paramref name="referenceDate"/>'s year, since a bare "Aug 26" is
/// ambiguous once notes can span a year boundary.
/// </summary>
internal static class OperationalNoteDateRangeFormatter
{
    public static string Format(OperationalNote note, OperationalNoteLifecycleState lifecycle, DateOnly referenceDate) =>
        Format(note.EffectiveFrom, note.EffectiveThrough, lifecycle, referenceDate);

    /// <summary>
    /// The primitive-field overload (Phase 11C Part L) — lets a caller with only
    /// <c>EffectiveFrom</c>/<c>EffectiveThrough</c> (e.g. the Dashboard's lightweight
    /// <see cref="Application.Dashboard.DashboardOperationalNoteItem"/>, which carries no full
    /// <see cref="OperationalNote"/>) reuse this exact formatter instead of a second, competing one.
    /// </summary>
    public static string Format(DateOnly effectiveFrom, DateOnly? effectiveThrough, OperationalNoteLifecycleState lifecycle, DateOnly referenceDate)
    {
        // Upcoming (spec Part Q): lead with "Starts <date>" rather than a from/through range, since the
        // range hasn't begun yet and isn't the operationally relevant fact.
        if (lifecycle == OperationalNoteLifecycleState.Upcoming)
        {
            return $"Starts {FormatDate(effectiveFrom, referenceDate)}";
        }

        var from = FormatDate(effectiveFrom, referenceDate);
        var through = effectiveThrough is { } through2 ? FormatDate(through2, referenceDate) : "Until resolved";
        return $"{from} – {through}";
    }

    public static string FormatDate(DateOnly date, DateOnly referenceDate)
    {
        var format = date.Year == referenceDate.Year ? "MMM d" : "MMM d, yyyy";
        return date.ToDateTime(TimeOnly.MinValue).ToString(format, PresentationCulture.UnitedStates);
    }
}

/// <summary>
/// "Carry-forward" relevance text (Phase 11E Parts E/F) — a short, relative-when-close phrase
/// answering "does this note still apply, and for how long" without a supervisor having to do date
/// arithmetic against <see cref="OperationalNoteDateRangeFormatter"/>'s absolute range text. This is
/// purely derived presentation, layered on the same already-computed <see cref="OperationalNoteLifecycleState"/>
/// every other Phase 11 surface uses — it never re-derives lifecycle/relevance itself (spec Part F), and
/// it never mutates or duplicates a daily record (no recurrence, no copy-forward). Returns
/// <see langword="null"/> for <see cref="OperationalNoteLifecycleState.Resolved"/>, since a resolved
/// note's own lifecycle label already says everything relevant here.
/// </summary>
internal static class OperationalNoteCarryForwardFormatter
{
    /// <summary>Phase 22E: the phrases that are relative to today ("Expires today/tomorrow", "Starts tomorrow") rather than a restatement of the absolute range.</summary>
    public static bool IsRelativePhrase(string text) => text is "Expires today" or "Expires tomorrow" or "Starts tomorrow";

    public static string? Format(OperationalNoteLifecycleState lifecycle, DateOnly effectiveFrom, DateOnly? effectiveThrough, DateOnly referenceDate)
    {
        switch (lifecycle)
        {
            case OperationalNoteLifecycleState.Upcoming:
                return (effectiveFrom.DayNumber - referenceDate.DayNumber) switch
                {
                    1 => "Starts tomorrow",
                    _ => $"Starts {OperationalNoteDateRangeFormatter.FormatDate(effectiveFrom, referenceDate)}",
                };

            case OperationalNoteLifecycleState.Active when effectiveThrough is null:
                return "Until resolved";

            case OperationalNoteLifecycleState.Active:
                return (effectiveThrough!.Value.DayNumber - referenceDate.DayNumber) switch
                {
                    0 => "Expires today",
                    1 => "Expires tomorrow",
                    _ => $"Through {OperationalNoteDateRangeFormatter.FormatDate(effectiveThrough.Value, referenceDate)}",
                };

            case OperationalNoteLifecycleState.Expired:
                // Derive() only reports Expired when EffectiveThrough has passed, so it is always set here.
                return $"Expired {OperationalNoteDateRangeFormatter.FormatDate(effectiveThrough!.Value, referenceDate)}";

            default:
                return null;
        }
    }
}
