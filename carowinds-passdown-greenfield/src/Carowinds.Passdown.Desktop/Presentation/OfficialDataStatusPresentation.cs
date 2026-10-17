using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Phase 19A: the subtle, shared official-data freshness line ("Official data checked 12:41 PM").
/// Says "checked", never "updated": the time is when every source was last verified, whether or not
/// any value changed. Times are Eastern (operational time) in US format; a check from an earlier day
/// also shows its date. Never shows a timestamp that did not happen, and never claims success while
/// any source failed. Source names only in the detail — no URLs, exception text, or stack traces.
/// </summary>
public sealed record OfficialDataStatusPresentation(
    string Text,
    bool HasIssue,
    bool IsChecking,
    string? DetailText,
    string AutomationText)
{
    public const string NotCheckedYetText = "Official data not checked yet";
    public const string CheckingText = "Checking official data…";

    public static OfficialDataStatusPresentation Build(OfficialDataStatus status, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(status);

        if (status.IsChecking)
        {
            return new OfficialDataStatusPresentation(CheckingText, HasIssue: false, IsChecking: true, DetailText: null, "Checking official data");
        }

        var (text, hasIssue) = status.State switch
        {
            OfficialDataCheckState.Checked when status.LastSuccessfulCheckAt is { } checkedAt =>
                ($"Official data checked {FormatTime(checkedAt, nowUtc)}", false),
            OfficialDataCheckState.PartiallyFailed when status.LastCheckedAt is { } lastChecked =>
                ($"Some official data couldn't be updated · Last checked {FormatTime(lastChecked, nowUtc)}", true),
            OfficialDataCheckState.Failed when status.LastCheckedAt is { } lastChecked =>
                ($"Official data couldn't be updated · Last checked {FormatTime(lastChecked, nowUtc)}", true),
            _ => (NotCheckedYetText, false),
        };

        string? detail = hasIssue && status.FailedSources.Count > 0
            ? "Not updated: " + string.Join(", ", status.FailedSources.Select(s => SourceCategoryDisplay.FriendlyName(s.Category)).Distinct(StringComparer.Ordinal))
            : null;

        var automation = detail is null ? text : $"{text}. {detail}";
        return new OfficialDataStatusPresentation(text, hasIssue, IsChecking: false, detail, automation);
    }

    /// <summary>"12:41 PM" for today (Eastern); "Oct 14, 12:41 PM" for an earlier day.</summary>
    private static string FormatTime(DateTimeOffset instantUtc, DateTimeOffset nowUtc)
    {
        return EasternTimestampFormat.Relative(instantUtc, nowUtc);
    }
}
