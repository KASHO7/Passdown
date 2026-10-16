using Carowinds.Passdown.Application.Retrieval;

namespace Carowinds.Passdown.Application.HistoricalBackfill;

/// <summary>
/// Phase 20C: one past date's official park hours, retrieved through the same official adapter the
/// operational refresh uses. <see cref="NotRetainedBySource"/> is true only when the official feed
/// explicitly answered "Not Found" for a date that is already in the past — the feed keeps a limited
/// window of history, and dates older than it are simply no longer published. That answer is never
/// stored as a value (the adapter still reports it as an error, so existing last-known-good data can
/// never be displaced by it); it only tells the backfill where official history ends.
/// </summary>
public sealed record HistoricalParkHoursResult(SourceResult<ParkHoursPayload> Result, bool NotRetainedBySource);

/// <summary>Phase 20C: the past-date park-hours retrieval the current-year backfill uses (Infrastructure: <c>ParkHoursSource</c>).</summary>
public interface IParkHoursHistorySource
{
    string SourceId { get; }

    Task<HistoricalParkHoursResult> RetrieveHistoricalAsync(DateOnly date, CancellationToken cancellationToken);
}
