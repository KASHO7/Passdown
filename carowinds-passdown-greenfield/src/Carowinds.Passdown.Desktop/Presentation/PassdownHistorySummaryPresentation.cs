using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Time;

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
    public static PassdownHistorySummaryPresentation Build(FinalizedPassdownSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var finalizedEastern = EasternTimeZone.ConvertFromUtc(summary.FinalizedAtUtc);

        return new PassdownHistorySummaryPresentation(
            summary.PassdownDate,
            summary.PassdownDate.ToString("MMMM d, yyyy"),
            $"Finalized {finalizedEastern:MMM d, yyyy 'at' h:mm tt}",
            summary.Origin.ToString(),
            summary.HasFrozenPdf,
            summary.HasFrozenPdf ? "PDF available" : "Exact historical PDF unavailable");
    }
}
