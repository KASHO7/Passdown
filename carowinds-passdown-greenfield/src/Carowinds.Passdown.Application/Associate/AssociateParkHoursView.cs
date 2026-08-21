using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Application.Associate;

/// <summary>
/// The composed park-hours picture for one operating date: the computed effective hours (spec
/// §8.2) plus how trustworthy the underlying official data currently is (spec §5.3/§6.7).
/// <see cref="OfficialIsClosed"/>/<see cref="OfficialOpening"/>/<see cref="OfficialClosing"/> are
/// the same raw inputs <see cref="ParkHoursCalculator.Calculate"/> was composed from — carried
/// alongside the already-computed <see cref="Effective"/> so a Phase 4C screen can re-run the one
/// canonical calculator locally for instant "recalculate immediately when the override changes"
/// feedback (spec §8.2) without a save round-trip, instead of duplicating the -30/-45 minute
/// arithmetic itself.
/// </summary>
public sealed record AssociateParkHoursView(
    EffectiveHours Effective,
    AssociateVerificationInfo Verification,
    bool OfficialIsClosed,
    TimeOnly? OfficialOpening,
    TimeOnly? OfficialClosing);
