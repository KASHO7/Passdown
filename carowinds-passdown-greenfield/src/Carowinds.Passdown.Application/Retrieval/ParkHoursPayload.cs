namespace Carowinds.Passdown.Application.Retrieval;

/// <summary>
/// The official main-gate opening/closing time for one confirmed date (spec §8.1's "Official
/// park opening and closing time/status"). Only ever attached to a
/// <see cref="Domain.Retrieval.RetrievalStatus.Verified"/> <see cref="SourceResult{TPayload}"/> —
/// the confirmed date itself already lives on <see cref="Domain.Retrieval.SourceEvidence.ConfirmedDate"/>,
/// so it is not duplicated here. Deliberately omits waterpark hours, buyout flags, and every
/// other field the source's response carries — spec §8.1 only calls for the main gate's
/// open/close, and later application logic (§8.2's North Gate/Avenue offset math) needs nothing
/// else.
/// </summary>
public sealed record ParkHoursPayload(TimeOnly OpeningTime, TimeOnly ClosingTime);
