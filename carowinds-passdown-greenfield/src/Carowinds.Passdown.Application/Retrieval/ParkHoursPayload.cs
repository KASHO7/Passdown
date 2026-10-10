namespace Carowinds.Passdown.Application.Retrieval;

/// <summary>
/// The official main-gate opening/closing time for one confirmed date (spec §8.1's "Official
/// park opening and closing time/status"). Only ever attached to a
/// <see cref="Domain.Retrieval.RetrievalStatus.Verified"/> <see cref="SourceResult{TPayload}"/> —
/// the confirmed date itself already lives on <see cref="Domain.Retrieval.SourceEvidence.ConfirmedDate"/>,
/// so it is not duplicated here. Deliberately omits buyout flags — spec §8.2's private-buyout
/// handling is a manual associate override, never sourced from this feed.
///
/// <see cref="WaterparkOpeningTime"/>/<see cref="WaterparkClosingTime"/> (Phase 13D Part D/E) carry
/// Carolina Harbor's own hours when the same official response publishes a second <c>"Waterpark"</c>
/// operating-type entry for the confirmed date (see <see cref="Infrastructure.Retrieval.Sources.ParkHoursSource"/>
/// and <c>docs/retrieval/park-hours-source.md</c>) — both null whenever the waterpark is not
/// separately published for that date (season not yet open/already ended, or simply not offered
/// that day), which is the normal, expected case for most of the year. Never fabricated from a
/// hardcoded season-end date — always read directly from whatever the source actually returned for
/// the confirmed date.
/// </summary>
public sealed record ParkHoursPayload(
    TimeOnly OpeningTime,
    TimeOnly ClosingTime,
    TimeOnly? WaterparkOpeningTime = null,
    TimeOnly? WaterparkClosingTime = null);
