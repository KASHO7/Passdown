using Carowinds.Passdown.Application.HistoricalBackfill;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Phase 20C: the restrained historical-data line under the Operations Calendar's month title. Separate
/// from the operational "Official data checked …" line on purpose — history is enrichment, so its
/// failures never make current operations look unhealthy. <see cref="Text"/> is null when there is
/// nothing worth saying (not started, or January 1 with no past dates yet).
/// </summary>
public sealed record HistoricalBackfillStatusPresentation(string? Text, string? DetailText, string? AutomationText)
{
    public const string LoadingText = "Loading this year's history…";
    public const string UpToDateText = "Historical data up to date";
    public const string PartialText = "Historical data partially available";

    public static readonly HistoricalBackfillStatusPresentation Hidden = new(null, null, null);

    public static HistoricalBackfillStatusPresentation Build(HistoricalBackfillStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        var (text, detail) = status.State switch
        {
            // The denominator is the dates this session set out to check — fixed for the session, so the
            // count never jumps; failures are not "checked".
            HistoricalBackfillState.Running when status.SessionChecked > 0 =>
                ($"Historical data: {status.SessionChecked} of {status.SessionQueued} dates checked", (string?)null),
            HistoricalBackfillState.Running => (LoadingText, null),
            HistoricalBackfillState.UpToDate => (UpToDateText, null),
            HistoricalBackfillState.PartiallyAvailable => (PartialText, PartialDetail(status)),
            _ => ((string?)null, (string?)null),
        };

        if (text is null)
        {
            return Hidden;
        }

        return new HistoricalBackfillStatusPresentation(text, detail, detail is null ? text : $"{text}. {detail}");
    }

    private static string PartialDetail(HistoricalBackfillStatus status)
    {
        var parts = new List<string>();
        if (status.OldestNotRetainedDate is { } boundary)
        {
            parts.Add($"Official park hours on or before {boundary.ToString("MMM d", PresentationCulture.UnitedStates)} are no longer published.");
        }

        if (status.SessionFailed > 0 || status.OldestNotRetainedDate is null)
        {
            parts.Add("Some dates couldn't be checked; they'll be tried again the next time the app starts.");
        }

        return string.Join(" ", parts);
    }
}
