using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Domain.Shows;

namespace Carowinds.Passdown.Application.Shows;

/// <summary>
/// Phase 21C: one show the supervisor chose for this date's passdown, with a copy of what the
/// official schedule said when it was chosen. The copy is used only when no official schedule for
/// the date is available on this computer (offline, never checked, or a Personal Sync recipient
/// without the schedule); otherwise the current verified times are used.
/// </summary>
public sealed record AssociateShowSelection(
    string ShowId,
    string Name,
    string? Venue,
    IReadOnlyList<TimeOnly> Times,
    DateTimeOffset SelectedAtUtc);

/// <summary>
/// Phase 21C: the optional Shows section of one date's draft. <see cref="IncludeInPassdown"/> is the
/// section switch; <see cref="SelectedShows"/> are the individually chosen shows (all of a selected
/// show's verified times print). Persisted as <c>AssociateDraftPayload.Shows</c>, which is omitted
/// entirely (null) while the section is off with nothing selected — so drafts that never use Shows
/// serialize and content-hash exactly as before Phase 21C.
/// </summary>
public sealed record AssociateShowsSelection(bool IncludeInPassdown, IReadOnlyList<AssociateShowSelection> SelectedShows)
{
    public static AssociateShowsSelection None { get; } = new(false, []);

    /// <summary>The value to persist: null when it carries nothing (section off, nothing selected).</summary>
    public AssociateShowsSelection? NullIfEmpty() => !IncludeInPassdown && SelectedShows.Count == 0 ? null : this;
}

/// <summary>Phase 21C: how one Shows row relates to the current official schedule.</summary>
public enum AssociateShowRowState
{
    /// <summary>On the current official schedule for this date; the times shown are the verified ones.</summary>
    Current,

    /// <summary>Selected; still on the schedule, but its official times changed since it was selected (current times are used).</summary>
    TimesChanged,

    /// <summary>Selected, but a later successful official check no longer lists it for this date — not printed.</summary>
    NoLongerListed,

    /// <summary>Selected, but the official schedule now says the park is closed or times are not published — not printed.</summary>
    NotOnCurrentSchedule,

    /// <summary>Selected, and no official schedule is available on this computer — printed from the copy saved when it was selected.</summary>
    SavedCopy,
}

/// <summary>One Shows row for the Associate screen and the PDF.</summary>
public sealed record AssociateShowRow(
    string ShowId,
    string Name,
    string? Venue,
    IReadOnlyList<TimeOnly> Times,
    bool IsSelected,
    AssociateShowRowState State)
{
    /// <summary>True when this row's times go on the PDF (when the section is included).</summary>
    public bool IsPrintable => IsSelected && State is AssociateShowRowState.Current or AssociateShowRowState.TimesChanged or AssociateShowRowState.SavedCopy;

    public string TimesText => ShowTimes.FormatList(Times);
}

/// <summary>
/// Phase 21C: the reconciled Shows section for one date — the current official schedule's shows (each
/// marked selected or not) plus any selected show the schedule no longer carries, explained rather
/// than silently dropped. A newly published show is never selected automatically.
/// </summary>
public sealed record AssociateShowsSection(
    ShowScheduleView Schedule,
    bool IncludeInPassdown,
    IReadOnlyList<AssociateShowRow> Rows)
{
    /// <summary>The rows the PDF prints, in schedule order — empty when the section is off.</summary>
    public IReadOnlyList<AssociateShowRow> PrintableRows => IncludeInPassdown ? Rows.Where(row => row.IsPrintable).ToList() : [];

    public static AssociateShowsSection Compose(ShowScheduleView schedule, AssociateShowsSelection? selection)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        selection ??= AssociateShowsSelection.None;

        var selectedById = new Dictionary<string, AssociateShowSelection>(StringComparer.Ordinal);
        foreach (var selected in selection.SelectedShows)
        {
            selectedById.TryAdd(selected.ShowId, selected); // duplicate ids in a draft never become duplicate rows
        }

        var rows = new List<AssociateShowRow>();
        var scheduledIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var show in schedule.Shows)
        {
            if (!scheduledIds.Add(show.ShowId))
            {
                continue;
            }

            var isSelected = selectedById.TryGetValue(show.ShowId, out var saved);
            var state = isSelected && !ShowTimes.Normalize(saved!.Times).SequenceEqual(show.Times)
                ? AssociateShowRowState.TimesChanged
                : AssociateShowRowState.Current;
            rows.Add(new AssociateShowRow(show.ShowId, show.Name, show.Venue, show.Times, isSelected, state));
        }

        var missingState = schedule.State switch
        {
            ShowScheduleState.Scheduled or ShowScheduleState.NoShows => AssociateShowRowState.NoLongerListed,
            ShowScheduleState.ParkClosed or ShowScheduleState.NotPublished => AssociateShowRowState.NotOnCurrentSchedule,
            _ => AssociateShowRowState.SavedCopy,
        };

        foreach (var saved in selectedById.Values.Where(saved => !scheduledIds.Contains(saved.ShowId)))
        {
            rows.Add(new AssociateShowRow(saved.ShowId, saved.Name, saved.Venue, ShowTimes.Normalize(saved.Times), IsSelected: true, missingState));
        }

        return new AssociateShowsSection(schedule, selection.IncludeInPassdown, rows);
    }

    /// <summary>
    /// The selection to persist from the rows currently selected: a show still on the official
    /// schedule refreshes its saved copy to the verified values (keeping when it was selected); a
    /// show no longer on it keeps its saved copy untouched; a newly chosen show copies its row.
    /// </summary>
    public static AssociateShowsSelection? BuildSelection(
        bool includeInPassdown,
        IEnumerable<AssociateShowRow> selectedRows,
        AssociateShowsSelection? previous,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(selectedRows);
        var previousById = (previous ?? AssociateShowsSelection.None).SelectedShows
            .GroupBy(saved => saved.ShowId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var selections = new List<AssociateShowSelection>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in selectedRows)
        {
            if (!seen.Add(row.ShowId))
            {
                continue;
            }

            var isOnSchedule = row.State is AssociateShowRowState.Current or AssociateShowRowState.TimesChanged;
            selections.Add(previousById.TryGetValue(row.ShowId, out var existing)
                ? isOnSchedule
                    // Keep the saved copy current with the verified schedule, so a device without the
                    // schedule (Personal Sync) never prints superseded times.
                    ? existing with { Name = row.Name, Venue = row.Venue, Times = row.Times }
                    : existing
                : new AssociateShowSelection(row.ShowId, row.Name, row.Venue, row.Times, nowUtc));
        }

        return new AssociateShowsSelection(includeInPassdown, selections).NullIfEmpty();
    }
}
