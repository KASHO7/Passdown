using System.Globalization;
using Carowinds.Passdown.Application.OperationalNotes;

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
    string CategoryLabel,
    string PriorityLabel,
    bool IsImportant,
    string DateRangeText,
    OperationalNoteLifecycleState LifecycleState,
    string LifecycleLabel)
{
    public static OperationalNoteRowPresentation Build(OperationalNote note, DateOnly referenceDate)
    {
        ArgumentNullException.ThrowIfNull(note);

        var lifecycle = OperationalNoteLifecycle.Derive(note, referenceDate);
        return new OperationalNoteRowPresentation(
            note.Id,
            note.Title,
            OperationalNoteCategoryOption.For(note.Category).Label,
            OperationalNotePriorityOption.For(note.Priority).Label,
            note.Priority == OperationalNotePriority.Important,
            OperationalNoteDateRangeFormatter.Format(note, lifecycle, referenceDate),
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
    OperationalNoteLifecycleState LifecycleState,
    string LifecycleLabel,
    bool IsResolved,
    string? ResolvedAtText)
{
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
            lifecycle,
            OperationalNoteRowPresentation.LifecycleLabelFor(lifecycle),
            isResolved,
            isResolved && note.ResolvedAtUtc is { } resolvedAt ? FormatTimestamp(resolvedAt) : null);
    }

    private static string FormatDate(DateOnly date, DateOnly referenceDate) => OperationalNoteDateRangeFormatter.FormatDate(date, referenceDate);

    private static string FormatTimestamp(DateTimeOffset value) => value.ToLocalTime().ToString("MMM d 'at' h:mm tt", CultureInfo.InvariantCulture);
}

/// <summary>
/// Friendly effective-date-range formatting (spec Phase 11B Part T), shared by the list row and detail
/// presentation so the two can never disagree. Never displays a raw ISO database value; includes the
/// year only when it differs from <paramref name="referenceDate"/>'s year, since a bare "Aug 26" is
/// ambiguous once notes can span a year boundary.
/// </summary>
internal static class OperationalNoteDateRangeFormatter
{
    public static string Format(OperationalNote note, OperationalNoteLifecycleState lifecycle, DateOnly referenceDate)
    {
        // Upcoming (spec Part Q): lead with "Starts <date>" rather than a from/through range, since the
        // range hasn't begun yet and isn't the operationally relevant fact.
        if (lifecycle == OperationalNoteLifecycleState.Upcoming)
        {
            return $"Starts {FormatDate(note.EffectiveFrom, referenceDate)}";
        }

        var from = FormatDate(note.EffectiveFrom, referenceDate);
        var through = note.EffectiveThrough is { } effectiveThrough ? FormatDate(effectiveThrough, referenceDate) : "Until resolved";
        return $"{from} – {through}";
    }

    public static string FormatDate(DateOnly date, DateOnly referenceDate)
    {
        var format = date.Year == referenceDate.Year ? "MMM d" : "MMM d, yyyy";
        return date.ToDateTime(TimeOnly.MinValue).ToString(format, CultureInfo.InvariantCulture);
    }
}
