using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Application.Associate;

/// <summary>The composed park-hours picture for one operating date: the computed effective hours (spec §8.2) plus how trustworthy the underlying official data currently is (spec §5.3/§6.7).</summary>
public sealed record AssociateParkHoursView(EffectiveHours Effective, AssociateVerificationInfo Verification);
