using System.Globalization;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Domain;

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
    PassdownHistorySummaryPresentation? LatestFinalized)
{
    /// <summary>Phase 22C: today's official admission reference for the Operations Overview (product-identified, with its own freshness).</summary>
    public DashboardAdmissionPresentation Admission { get; init; } = DashboardAdmissionPresentation.Build(DashboardAdmissionSummary.NotChecked, DateTimeOffset.UtcNow);

    /// <summary>
    /// Phase 22C: the Operations Overview's park-hours headline. Open/closed use the shared hours
    /// headline unchanged; otherwise it states the actual status (not published / unavailable / not
    /// checked) instead of the shared formatter's generic "Hours not checked yet", so NotPublished and
    /// Unavailable are never shown as "not checked".
    /// </summary>
    public string ParkHoursHeadlineText => ParkStatus switch
    {
        DashboardParkStatus.NotPublished => "Hours not published",
        DashboardParkStatus.Unavailable => "Hours unavailable",
        _ => ParkHours.HeadlineText,
    };

    /// <summary>The snapshot's park-status classification (kept so the headline above never re-derives it).</summary>
    public DashboardParkStatus ParkStatus { get; init; }

    /// <summary>Phase 35E Dashboard: the line under the park-hours value — which kind of hours these are, never a promise the park will operate.</summary>
    public string? ParkHoursSupportText => ParkStatus switch
    {
        DashboardParkStatus.Open => "Published park schedule",
        DashboardParkStatus.Closed => "Published park schedule",
        DashboardParkStatus.CustomHours => "Manual hours for today",
        DashboardParkStatus.PrivateBuyout => "Private buyout",
        _ => null,
    };

    public bool HasParkHoursSupportText => ParkHoursSupportText is not null;

    /// <summary>Phase 35E Dashboard: Avenue Entrance opening (the existing effective value: override, else opening −45 min).</summary>
    public string AvenueText { get; init; } = NoGateTimeText;

    public string AvenueSupportText { get; init; } = string.Empty;

    /// <summary>Phase 35E Dashboard: North Gate opening (the existing effective value: override, else opening −30 min).</summary>
    public string NorthGateText { get; init; } = NoGateTimeText;

    public string NorthGateSupportText { get; init; } = string.Empty;

    /// <summary>Shown instead of a gate time when there are no opening hours to calculate it from.</summary>
    public const string NoGateTimeText = "Not available";

    /// <summary>Phase 35E Dashboard: "Nov 22, 2026" — the operating date the whole page describes (gate footer).</summary>
    public string OperatingDateShortText { get; init; } = string.Empty;

    /// <summary>Phase 35E Dashboard: "Last saved locally 8:16 AM ET" — the draft's local autosave time; never implies Personal Sync.</summary>
    public string? LastSavedLocallyText { get; init; }

    public bool HasLastSavedLocallyText => LastSavedLocallyText is not null;

    /// <summary>Phase 35E Dashboard: today's passdown lifecycle state (drives which detail line the card shows).</summary>
    public DashboardPassdownStatus PassdownStatus { get; init; }

    /// <summary>
    /// Phase 35E Dashboard: the passdown card's detail line. A draft's save time is already the card's
    /// "Last saved locally" line, so only No Draft / Finalized carry one here.
    /// </summary>
    public string? PassdownCardDetailText => PassdownStatus == DashboardPassdownStatus.DraftInProgress ? null : PassdownStatusDetailText;

    public bool HasPassdownCardDetailText => PassdownCardDetailText is not null;

    /// <summary>Phase 35E Dashboard: the neutral note beside the passdown status.</summary>
    public string PassdownStatusNoteText { get; init; } = string.Empty;

    /// <summary>Phase 35E Dashboard: "4 scheduled shows · 22 performances" from today's recorded official schedule, or null.</summary>
    public string? ShowsSummaryText { get; init; }

    public bool HasShowsSummaryText => ShowsSummaryText is not null;

    /// <summary>Phase 35E Dashboard: today's official dated ticket and Fast Lane prices.</summary>
    public DashboardPricingPresentation Pricing { get; init; } = DashboardPricingPresentation.Empty;

    /// <summary>Phase 35E Dashboard: the operating month's advertised Pass Perks from the cached official page.</summary>
    public DashboardPassPerksPresentation? PassPerks { get; init; }

    /// <summary>Phase 22C: the compact date label above the Operations Overview heading (e.g. "FRIDAY, OCTOBER 16, 2026").</summary>
    public string OperatingDayCaption => OperatingDayLabel.ToUpperInvariant();

    /// <summary>Presentation-only bound (spec Phase 10B Part K) — never applied to persisted draft data, only to the Dashboard's own read-only display strings.</summary>
    private const int MaxDailyFeaturePreviewLength = 140;

    private const int MaxNotesPreviewLineLength = 100;

    public static OperationsDashboardPresentation Build(OperationsDashboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var dayLabel = snapshot.OperatingDate.ToDateTime(TimeOnly.MinValue)
            .ToString("dddd, MMMM d, yyyy", PresentationCulture.UnitedStates);

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
        // Phase 17D+ saves Configured ERT with no start time (rides only), so StartTime is optional
        // here: a legacy record's start time keeps its existing text, otherwise the ride list is
        // shown with the same rule as AssociateViewModel.EarlyRideTimeStatusText.
        var earlyRideTimeText = snapshot.EarlyRideTime switch
        {
            { Kind: EffectiveErtKind.Configured, StartTime: { } startTime } => $"Starts {OperationalTimeFormatter.Format(startTime)}",
            { Kind: EffectiveErtKind.Configured, Rides.Count: > 0 } => string.Join(", ", snapshot.EarlyRideTime.Rides),
            { Kind: EffectiveErtKind.Configured } => "Configured",
            { Kind: EffectiveErtKind.NoErt } => "None",
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

        var effective = snapshot.ParkStatus.Hours.Effective;
        var hasOpening = effective.Status == EffectiveHoursStatus.Open;
        string GateSupport(bool overridden, int minutes, string? time) => overridden
            ? "Manual time for today"
            : time is not null ? $"{minutes} min before opening" : hasOpening ? string.Empty : "Needs published park hours";

        string? showsSummary = null;
        if (snapshot.TodayShows.State == Application.Shows.ShowScheduleState.Scheduled && snapshot.TodayShows.Shows.Count > 0)
        {
            var shows = snapshot.TodayShows.Shows.Count;
            var performances = snapshot.TodayShows.Shows.Sum(show => show.Times.Count);
            showsSummary = $"{shows} scheduled show{(shows == 1 ? string.Empty : "s")} · {performances} performance{(performances == 1 ? string.Empty : "s")}";
        }

        var passdownNote = snapshot.PassdownStatus.Status == DashboardPassdownStatus.Finalized
            ? "Finalized record is read-only in History"
            : "Autosaves on this computer as you edit";

        string? lastSavedLocally = snapshot.PassdownStatus.Status == DashboardPassdownStatus.DraftInProgress && snapshot.PassdownStatus.LastSavedAt is { } lastSaved
            ? $"Last saved locally {EasternTimestampFormat.Relative(lastSaved, snapshot.GeneratedAt)}"
            : null;

        return new OperationsDashboardPresentation(
            dayLabel,
            parkHours,
            parkBadgeText,
            passdownText,
            passdownDetail,
            showOpenAction,
            OperationalCount.Format(snapshot.ProjectedAttendance, "Not entered"),
            OperationalCount.Format(snapshot.PreSolds, "Not entered"),
            earlyRideTimeText,
            dailyFeatureLabel,
            dailyFeatureText,
            notesSummaryText,
            notesPreviewLines,
            activeOperationalNotes,
            dataHealth,
            latestFinalized)
        {
            Admission = DashboardAdmissionPresentation.Build(snapshot.Admission, snapshot.GeneratedAt),
            ParkStatus = snapshot.ParkStatus.Status,
            PassdownStatus = snapshot.PassdownStatus.Status,
            AvenueText = parkHours.AvenueText ?? NoGateTimeText,
            AvenueSupportText = GateSupport(effective.IsAvenueOverridden, 45, parkHours.AvenueText),
            NorthGateText = parkHours.NorthGateText ?? NoGateTimeText,
            NorthGateSupportText = GateSupport(effective.IsNorthGateOverridden, 30, parkHours.NorthGateText),
            OperatingDateShortText = snapshot.OperatingDate.ToString("MMM d, yyyy", PresentationCulture.UnitedStates),
            LastSavedLocallyText = lastSavedLocally,
            PassdownStatusNoteText = passdownNote,
            ShowsSummaryText = showsSummary,
            Pricing = DashboardPricingPresentation.Build(snapshot.Pricing, snapshot.GeneratedAt),
            PassPerks = DashboardPassPerksPresentation.Build(snapshot.PassPerks, snapshot.PassPerksLoadFailed, snapshot.OperatingDate),
        };
    }

    /// <summary>Shared timestamp formatting, also reused by <see cref="DashboardDataHealthPresentation"/> so "Last verified"/"Last checked" text matches this screen's other timestamps exactly.</summary>
    internal static string FormatTimestamp(DateTimeOffset value) => EasternTimestampFormat.DateAndTime(value);

    /// <summary>Bounds one display string to a maximum length with an ellipsis (Phase 10B Part K) — presentation-only; the underlying draft value is never modified.</summary>
    private static string Truncate(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : string.Concat(trimmed.AsSpan(0, maxLength).TrimEnd(), "…");
    }
}
