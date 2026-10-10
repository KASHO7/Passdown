using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>Which visual treatment a park-hours badge gets (spec §10.4: text always carries the meaning — this is a supplement, never the only signal).</summary>
public enum ParkHoursBadgeKind
{
    NotChecked,
    Closed,
    Open,
    ManualOverride,
    PrivateBuyout,
}

/// <summary>
/// Turns an already-composed <see cref="AssociateParkHoursView"/> into display-ready strings
/// (spec Phase 4B Part D: "do not duplicate calculations in the ViewModel/XAML — use values
/// already composed by the application layer"). Every time/opening value here is read straight off
/// <see cref="EffectiveHours"/> — nothing is recomputed.
/// </summary>
public sealed record ParkHoursPresentation(
    string HeadlineText,
    ParkHoursBadgeKind BadgeKind,
    string BadgeText,
    string? NorthGateText,
    string? AvenueText,
    string? VerificationText,
    string? StaleNoteText,
    string? WaterparkHeadlineText = null)
{
    /// <summary>Phase 13D Part E/G/H: only true when the source published Carolina Harbor's hours for this date — omitted cleanly (no placeholder row) otherwise.</summary>
    public bool HasWaterparkHours => WaterparkHeadlineText is not null;


    /// <summary>
    /// True when there are no actual hours to show yet (spec Part D: "Not checked yet" must not
    /// appear as a giant headline) — used purely to pick a quieter headline style, never to change
    /// what text is shown.
    /// </summary>
    public bool IsHoursUnknown => BadgeKind == ParkHoursBadgeKind.NotChecked;


    public static ParkHoursPresentation Build(AssociateParkHoursView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var effective = view.Effective;

        var (badgeKind, badgeText) = effective.Status switch
        {
            EffectiveHoursStatus.NotCheckedYet => (ParkHoursBadgeKind.NotChecked, "Not checked yet"),
            EffectiveHoursStatus.Closed => (ParkHoursBadgeKind.Closed, "Closed"),
            EffectiveHoursStatus.Open when effective.IsPrivateBuyoutApplied => (ParkHoursBadgeKind.PrivateBuyout, "Open — Private Buyout"),
            EffectiveHoursStatus.Open when effective.IsManualOverrideApplied => (ParkHoursBadgeKind.ManualOverride, "Open — Manual Override"),
            EffectiveHoursStatus.Open => (ParkHoursBadgeKind.Open, "Open"),
            _ => (ParkHoursBadgeKind.NotChecked, "Not checked yet"),
        };

        var headline = effective.Status switch
        {
            EffectiveHoursStatus.Open => $"{FormatTime(effective.Opening)} – {FormatTime(effective.Closing)}",
            EffectiveHoursStatus.Closed => "Park is closed",
            _ => "Hours not checked yet",
        };

        var verificationText = view.Verification.State switch
        {
            AssociateVerificationState.NotCheckedYet => "Official hours have not been checked yet.",
            AssociateVerificationState.Verified => view.Verification.LastVerifiedAt is { } verifiedAt
                ? $"Verified · Last checked {FormatTimestamp(verifiedAt)}"
                : "Verified",
            AssociateVerificationState.Stale => view.Verification.LastVerifiedAt is { } staleAt
                ? $"Using information from {FormatTimestamp(staleAt)} — may be out of date"
                : "Using older information — may be out of date",
            AssociateVerificationState.Closed => view.Verification.LastVerifiedAt is { } closedAt
                ? $"Verified closed · Last checked {FormatTimestamp(closedAt)}"
                : "Verified closed",
            AssociateVerificationState.Error => "Official hours could not be checked.",
            AssociateVerificationState.NotPublished => "Official hours are not published yet for this date.",
            AssociateVerificationState.Unavailable => "Official hours are unavailable for this date.",
            _ => (string?)null,
        };

        // Only worth flagging when the associate is currently relying on the (possibly old)
        // official numbers themselves — an active manual/buyout override is the associate's own
        // authoritative input, not something "stale" official data undermines.
        string? staleNote = !effective.IsManualOverrideApplied && !effective.IsPrivateBuyoutApplied
            && view.Verification.State == AssociateVerificationState.Stale
                ? "Official hours could not be rechecked. Showing the last verified information."
                : null;

        string? waterparkHeadline = view.HasWaterparkHours
            ? $"{FormatTime(view.WaterparkOpening)} – {FormatTime(view.WaterparkClosing)}"
            : null;

        return new ParkHoursPresentation(
            headline,
            badgeKind,
            badgeText,
            FormatTime(effective.NorthGateOpening),
            FormatTime(effective.AvenueOpening),
            verificationText,
            staleNote,
            waterparkHeadline);
    }

    private static string? FormatTime(TimeOnly? time) => time?.ToString("h:mm tt");

    private static string FormatTimestamp(DateTimeOffset value) => value.ToLocalTime().ToString("MMM d 'at' h:mm tt");
}
