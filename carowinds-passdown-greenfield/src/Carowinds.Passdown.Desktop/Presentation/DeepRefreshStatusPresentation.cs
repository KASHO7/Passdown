using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Turns a <see cref="DeepRefreshStatusView"/> into the compact Dashboard → Data Status display text
/// (Phase 13D Part U/V) — never recomputes <see cref="DeepRefreshCoverage.NeedsDeepRefresh"/> or any
/// other business rule; purely formatting over already-composed values.
/// </summary>
public sealed record DeepRefreshStatusPresentation(
    string HeadlineText,
    bool WasSuccessful,
    string? DetailText,
    string? CoverageText,
    string AutomationText)
{
    public static DeepRefreshStatusPresentation Build(DeepRefreshStatusView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (view.Status is not { } status)
        {
            const string never = "Deep Refresh has not run yet";
            return new DeepRefreshStatusPresentation(never, WasSuccessful: false, DetailText: null, CoverageText: null, never);
        }

        var (headline, wasSuccessful) = status.Outcome switch
        {
            DeepRefreshOutcome.Success => ($"✓ {FormatTimestamp(status.LastSuccessAtUtc ?? status.LastAttemptAtUtc)}", true),
            DeepRefreshOutcome.Partial => ($"Incomplete · {FormatTimestamp(status.LastAttemptAtUtc)}", false),
            _ => ($"Failed · {FormatTimestamp(status.LastAttemptAtUtc)}", false),
        };

        string? detail = status.Outcome switch
        {
            DeepRefreshOutcome.Success => null,
            DeepRefreshOutcome.Partial => $"{status.SourceFailureCount} source(s) could not be updated",
            _ => "Deep Refresh could not complete",
        };

        string? coverageText = status.CoverageThrough is { } through
            ? string.Create(PresentationCulture.UnitedStates, $"Park hours available through {through:MMM d}")
            : null;

        var automationParts = new List<string> { headline };
        if (detail is not null)
        {
            automationParts.Add(detail);
        }
        if (coverageText is not null)
        {
            automationParts.Add(coverageText);
        }

        return new DeepRefreshStatusPresentation(headline, wasSuccessful, detail, coverageText, string.Join(". ", automationParts));
    }

    private static string FormatTimestamp(DateTimeOffset value) => EasternTimestampFormat.DateAndTime(value);
}
