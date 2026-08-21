namespace Carowinds.Passdown.Domain.Hours;

/// <summary>
/// Spec §8.2's effective-hours precedence, as one pure function so it is computed in exactly one
/// place (CLAUDE.md "Do not duplicate hours calculations"). Verified/manual-override/private-buyout
/// are mutually exclusive inputs to one calculation, not three separate calculators a caller has to
/// remember to invoke in the right order.
/// </summary>
public static class ParkHoursCalculator
{
    /// <summary>
    /// Precedence, per spec §8.2's exact wording:
    /// 1. An enabled private-buyout override always wins — it is spec's named exception to "closed"
    ///    ("show Closed unless the private-buyout override is enabled"), so it is checked first and
    ///    applies its own custom hours regardless of the official status.
    /// 2. Otherwise, an officially closed date shows <see cref="EffectiveHoursStatus.Closed"/> — a
    ///    manual hours override is for correcting a time on a day the park is otherwise open, not a
    ///    second mechanism for overriding "closed" (only buyout is named as doing that).
    /// 3. Otherwise, an enabled manual hours override wins ("the effective opening time ... is either
    ///    verified official time or an enabled manager override").
    /// 4. Otherwise, verified (or last-known-good/stale — still the best available) official hours apply.
    /// 5. Otherwise nothing has ever been confirmed for this date: <see cref="EffectiveHoursStatus.NotCheckedYet"/>.
    /// </summary>
    public static EffectiveHours Calculate(
        bool officialIsClosed,
        TimeOnly? officialOpening,
        TimeOnly? officialClosing,
        bool manualOverrideEnabled,
        TimeOnly? manualOpening,
        TimeOnly? manualClosing,
        bool privateBuyoutEnabled,
        TimeOnly? buyoutOpening,
        TimeOnly? buyoutClosing)
    {
        if (privateBuyoutEnabled)
        {
            return new EffectiveHours(EffectiveHoursStatus.Open, buyoutOpening, buyoutClosing, isManualOverrideApplied: false, isPrivateBuyoutApplied: true);
        }

        if (officialIsClosed)
        {
            return new EffectiveHours(EffectiveHoursStatus.Closed, null, null, isManualOverrideApplied: false, isPrivateBuyoutApplied: false);
        }

        if (manualOverrideEnabled)
        {
            return new EffectiveHours(EffectiveHoursStatus.Open, manualOpening, manualClosing, isManualOverrideApplied: true, isPrivateBuyoutApplied: false);
        }

        if (officialOpening is not null && officialClosing is not null)
        {
            return new EffectiveHours(EffectiveHoursStatus.Open, officialOpening, officialClosing, isManualOverrideApplied: false, isPrivateBuyoutApplied: false);
        }

        return new EffectiveHours(EffectiveHoursStatus.NotCheckedYet, null, null, isManualOverrideApplied: false, isPrivateBuyoutApplied: false);
    }
}
