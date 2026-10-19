using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Retrieval;

/// <summary>
/// Phase 22E.1: one calendar month of the official published park schedule, read with a single
/// request. <see cref="Dates"/> holds one result per date the official response actually listed
/// (Verified, Closed, NotPublished, or an Error for an unreadable entry) — a date the response did
/// not list is simply absent, never inferred. <see cref="Failure"/> is set when the month request
/// itself failed (network, HTTP error, unreadable or empty response); <see cref="Dates"/> is then
/// empty and nothing may be stored from it.
/// </summary>
public sealed record PublishedParkHoursMonth(
    int Year,
    int Month,
    IReadOnlyList<SourceResult<ParkHoursPayload>> Dates,
    SourceEvidence? Failure);

/// <summary>
/// Phase 22E.1: the official park-hours feed's month form (Infrastructure: <c>ParkHoursSource</c>),
/// used only for forward published-schedule discovery — never for history (the feed's month form
/// returns only today-forward dates; see docs/phase22e1-published-park-schedule-coverage.md).
/// </summary>
public interface IParkHoursScheduleSource
{
    string SourceId { get; }

    Task<PublishedParkHoursMonth> RetrieveMonthAsync(int year, int month, CancellationToken cancellationToken);
}
