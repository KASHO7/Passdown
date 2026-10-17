using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Friendly display text for one finalized-passdown row in the History list (spec Phase 9D Part B) —
/// "August 25, 2026" / "Finalized Aug 26, 2026 at 6:02 AM" / "Scheduled" / "PDF available". Built once
/// from <see cref="FinalizedPassdownSummary"/> metadata only — never from the frozen snapshot, so
/// rendering the list never has to deserialize every row (spec Part L). <see cref="FinalizedText"/>
/// uses Eastern wall-clock time, matching the app's existing "today" convention (spec §6.5/§6.6) and
/// the 6:00 AM Eastern finalization cutoff itself.
/// </summary>
public sealed record PassdownHistorySummaryPresentation(
    DateOnly PassdownDate,
    string PassdownDateText,
    string FinalizedText,
    string OriginText,
    bool HasFrozenPdf,
    string PdfAvailabilityText)
{
    /// <summary>Phase 22E: the list row's title with its weekday — "Friday, October 16, 2026" — so a date is easy to find by day.</summary>
    public string ListTitleText { get; init; } = PassdownDateText;

    /// <summary>Phase 22E: the list row's metadata line — "Finalized Oct 17, 6:02 AM · Scheduled" (the finalized year only when it differs from the passdown's).</summary>
    public string ListDetailText { get; init; } = FinalizedText;

    /// <summary>Phase 22E: the compact PDF tag at the row's right edge — "PDF" when the exact frozen PDF is stored, "No PDF" otherwise (with <see cref="PdfAvailabilityText"/> as its full wording).</summary>
    public string PdfTagText => HasFrozenPdf ? "PDF" : "No PDF";

    /// <summary>"Friday, October 16, 2026, finalized Oct 17, 6:02 AM ET, Scheduled, PDF available" for screen readers.</summary>
    public string AccessibleSummary => $"{ListTitleText}, {char.ToLowerInvariant(ListDetailText[0])}{ListDetailText[1..].Replace(" · ", ", ", StringComparison.Ordinal)}, {PdfAvailabilityText}";

    public static PassdownHistorySummaryPresentation Build(FinalizedPassdownSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var finalizedEastern = EasternTimeZone.ConvertFromUtc(summary.FinalizedAtUtc);

        var finalizedShortFormat = finalizedEastern.Year == summary.PassdownDate.Year ? "MMM d, h:mm tt" : "MMM d, yyyy, h:mm tt";

        return new PassdownHistorySummaryPresentation(
            summary.PassdownDate,
            summary.PassdownDate.ToString("MMMM d, yyyy", PresentationCulture.UnitedStates),
            $"Finalized {EasternTimestampFormat.DateYearAndTime(summary.FinalizedAtUtc)}",
            summary.Origin.ToString(),
            summary.HasFrozenPdf,
            summary.HasFrozenPdf ? "PDF available" : "Exact historical PDF unavailable")
        {
            ListTitleText = summary.PassdownDate.ToString("dddd, MMMM d, yyyy", PresentationCulture.UnitedStates),
            ListDetailText = $"Finalized {EasternTimestampFormat.Format(summary.FinalizedAtUtc, finalizedShortFormat)} · {summary.Origin}",
        };
    }
}
