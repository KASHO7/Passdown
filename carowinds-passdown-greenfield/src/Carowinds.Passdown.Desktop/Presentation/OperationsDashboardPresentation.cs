using System.Globalization;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Turns an already-composed <see cref="OperationsDashboardSnapshot"/> into display-ready strings
/// (Phase 10A Part M/T), mirroring how <see cref="ParkHoursPresentation"/> already formats
/// <c>AssociateParkHoursView</c> — nothing here recalculates hours, freshness, or draft state;
/// <see cref="ParkHours"/> is built by reusing <see cref="ParkHoursPresentation.Build"/> directly on
/// the snapshot's own <see cref="OperationsDashboardSnapshot.ParkStatus"/> hours view.
/// </summary>
/// <param name="LatestFinalized">
/// The Dashboard's compact History integration surface (Phase 10C Part B) — built by reusing
/// <see cref="PassdownHistorySummaryPresentation.Build"/> directly, the exact same metadata-only
/// formatter the History screen's own list row already uses, rather than a second date/timestamp/
/// origin formatter. <c>null</c> when no passdown has ever been finalized (Part M).
/// </param>
public sealed record OperationsDashboardPresentation(
    string OperatingDayLabel,
    ParkHoursPresentation ParkHours,
    string ParkStatusBadgeText,
    string PassdownStatusText,
    string? PassdownStatusDetailText,
    bool ShowOpenPassdownAction,
    string AttendanceText,
    string PreSoldsText,
    string EarlyRideTimeText,
    string DailyFeatureLabel,
    string DailyFeatureText,
    string NotesSummaryText,
    IReadOnlyList<string> NotesPreviewLines,
    DashboardOperationalNotesPresentation ActiveOperationalNotes,
    DashboardDataHealthPresentation DataHealth,
    PassdownHistorySummaryPresentation? LatestFinalized,
    string GeneratedAtText)
{
    /// <summary>Presentation-only bound (spec Phase 10B Part K) — never applied to persisted draft data, only to the Dashboard's own read-only display strings.</summary>
    private const int MaxDailyFeaturePreviewLength = 140;

    private const int MaxNotesPreviewLineLength = 100;

    public static OperationsDashboardPresentation Build(OperationsDashboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var dayLabel = snapshot.OperatingDate.ToDateTime(TimeOnly.MinValue)
            .ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);

        var parkHours = ParkHoursPresentation.Build(snapshot.ParkStatus.Hours);
        var parkBadgeText = snapshot.ParkStatus.Status switch
        {
            DashboardParkStatus.Open => "Open",
            DashboardParkStatus.Closed => "Closed",
            DashboardParkStatus.CustomHours => "Custom Hours",
            DashboardParkStatus.PrivateBuyout => "Private Buyout",
            DashboardParkStatus.NotPublished => "Not Published",
            DashboardParkStatus.Unavailable => "Unavailable",
            _ => "Not checked yet",
        };

        var (passdownText, passdownDetail, showOpenAction) = snapshot.PassdownStatus.Status switch
        {
            DashboardPassdownStatus.NoDraft =>
                ("No Draft", (string?)"Nothing has been entered for today yet.", true),
            DashboardPassdownStatus.DraftInProgress =>
                ("Draft In Progress", snapshot.PassdownStatus.LastSavedAt is { } savedAt ? $"Last saved {FormatTimestamp(savedAt)}" : null, true),
            DashboardPassdownStatus.Finalized =>
                ("Finalized", snapshot.PassdownStatus.FinalizedAt is { } finalizedAt ? $"Finalized {FormatTimestamp(finalizedAt)}" : null, false),
            _ => ("Unknown", null, true),
        };

        // Phase 17B: the Dashboard reads the same resolver-produced EffectiveErt every other screen
        // does (spec Part 17) — never park hours/North Gate. "Not configured" and "None" are kept
        // deliberately terse (spec §17 "do not redesign Dashboard") rather than the fuller
        // "Not configured for <Month Year>" wording the Associate screen/PDF use.
        var earlyRideTimeText = snapshot.EarlyRideTime.Kind switch
        {
            EffectiveErtKind.Configured => $"Starts {OperationalTimeFormatter.Format(snapshot.EarlyRideTime.StartTime!.Value)}",
            EffectiveErtKind.NoErt => "None",
            _ => "Not configured",
        };

        var dailyFeatureLabel = DailyFeatureTypeOption.For(snapshot.DailyFeatureType).Label;

        // "Not entered" matches AssociateView's own PlaceholderText for these same fields (Phase 10B Part F/G/I) — never a new empty-state vocabulary.
        var dailyFeatureText = string.IsNullOrWhiteSpace(snapshot.DailyFeatureText)
            ? "Not entered"
            : Truncate(snapshot.DailyFeatureText, MaxDailyFeaturePreviewLength);

        var notesSummaryText = snapshot.Notes.HasNotes
            ? $"{snapshot.Notes.TotalLineCount} note line{(snapshot.Notes.TotalLineCount == 1 ? string.Empty : "s")} entered"
            : "No operational notes entered";

        var notesPreviewLines = snapshot.Notes.PreviewLines
            .Select(line => Truncate(line, MaxNotesPreviewLineLength))
            .ToList();

        var activeOperationalNotes = DashboardOperationalNotesPresentation.Build(snapshot.ActiveOperationalNotes, snapshot.OperatingDate);
        var dataHealth = DashboardDataHealthPresentation.Build(snapshot.DataHealth);

        var latestFinalized = snapshot.LatestFinalized is { } summary
            ? PassdownHistorySummaryPresentation.Build(summary)
            : null;

        return new OperationsDashboardPresentation(
            dayLabel,
            parkHours,
            parkBadgeText,
            passdownText,
            passdownDetail,
            showOpenAction,
            snapshot.ProjectedAttendance?.ToString(CultureInfo.InvariantCulture) ?? "Not entered",
            snapshot.PreSolds?.ToString(CultureInfo.InvariantCulture) ?? "Not entered",
            earlyRideTimeText,
            dailyFeatureLabel,
            dailyFeatureText,
            notesSummaryText,
            notesPreviewLines,
            activeOperationalNotes,
            dataHealth,
            latestFinalized,
            $"Updated {FormatTimestamp(snapshot.GeneratedAt)}");
    }

    /// <summary>Shared timestamp formatting, also reused by <see cref="DashboardDataHealthPresentation"/> so "Last verified"/"Last checked" text matches this screen's other timestamps exactly.</summary>
    internal static string FormatTimestamp(DateTimeOffset value) => value.ToLocalTime().ToString("MMM d 'at' h:mm tt", CultureInfo.InvariantCulture);

    /// <summary>Bounds one display string to a maximum length with an ellipsis (Phase 10B Part K) — presentation-only; the underlying draft value is never modified.</summary>
    private static string Truncate(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : string.Concat(trimmed.AsSpan(0, maxLength).TrimEnd(), "…");
    }
}
