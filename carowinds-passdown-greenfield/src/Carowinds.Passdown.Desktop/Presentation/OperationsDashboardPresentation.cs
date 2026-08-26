using System.Globalization;
using Carowinds.Passdown.Application.Dashboard;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Turns an already-composed <see cref="OperationsDashboardSnapshot"/> into display-ready strings
/// (Phase 10A Part M/T), mirroring how <see cref="ParkHoursPresentation"/> already formats
/// <c>AssociateParkHoursView</c> — nothing here recalculates hours, freshness, or draft state;
/// <see cref="ParkHours"/> is built by reusing <see cref="ParkHoursPresentation.Build"/> directly on
/// the snapshot's own <see cref="OperationsDashboardSnapshot.ParkStatus"/> hours view.
/// </summary>
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
    string SourceHealthText,
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

        // "Not set" mirrors DailyFeatureTypeOption's own wording for an unset choice (Phase 10B Part H)
        // rather than inventing a second "off" label.
        var earlyRideTimeText = snapshot.EarlyRideTimeEnabled
            ? (string.IsNullOrWhiteSpace(snapshot.EarlyRideTimeDetails) ? "Enabled" : snapshot.EarlyRideTimeDetails)
            : "Not set";

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

        var sourceHealthText = snapshot.SourceHealth.Health switch
        {
            DashboardSourceHealth.AllCurrent => "All source data current",
            DashboardSourceHealth.SomeStale => $"{snapshot.SourceHealth.StaleCount} source(s) showing older information",
            DashboardSourceHealth.SomeUnavailable => $"{snapshot.SourceHealth.UnavailableCount} source(s) unavailable",
            _ => "Source status unknown",
        };

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
            sourceHealthText,
            $"Updated {FormatTimestamp(snapshot.GeneratedAt)}");
    }

    private static string FormatTimestamp(DateTimeOffset value) => value.ToLocalTime().ToString("MMM d 'at' h:mm tt", CultureInfo.InvariantCulture);

    /// <summary>Bounds one display string to a maximum length with an ellipsis (Phase 10B Part K) — presentation-only; the underlying draft value is never modified.</summary>
    private static string Truncate(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : string.Concat(trimmed.AsSpan(0, maxLength).TrimEnd(), "…");
    }
}
