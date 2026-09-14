namespace Carowinds.Passdown.Domain.Ert;

/// <summary>
/// The one authoritative Early Ride Time precedence rule (Phase 17B spec Part 4): a date override, if
/// one exists for the date, wins outright — even when no monthly configuration exists for that
/// date's month at all (spec Part 38.I, a known exceptional-date-before-the-month-is-set-up case).
/// Otherwise the month's default applies. Otherwise nothing is configured. Pure and I/O-free so every
/// consumer (Associate screen, Dashboard, PDF, finalized snapshot) shares exactly this logic instead
/// of re-deriving it — see <c>Application.Ert.IEffectiveErtResolver</c>, the only caller that loads
/// the two snapshots this takes as input.
/// </summary>
public static class ErtPrecedenceResolver
{
    public static EffectiveErt Resolve(MonthlyErtSnapshot? monthly, ErtOverrideSnapshot? dateOverride)
    {
        if (dateOverride is not null)
        {
            return dateOverride.State == ErtConfigurationState.NoErt
                ? new EffectiveErt(EffectiveErtKind.NoErt, EffectiveErtSource.Override, null, [], dateOverride.Reason)
                : new EffectiveErt(EffectiveErtKind.Configured, EffectiveErtSource.Override, dateOverride.StartTime, dateOverride.Rides, dateOverride.Reason);
        }

        if (monthly is not null)
        {
            return monthly.State == ErtConfigurationState.NoErt
                ? new EffectiveErt(EffectiveErtKind.NoErt, EffectiveErtSource.Monthly, null, [], monthly.Note)
                : new EffectiveErt(EffectiveErtKind.Configured, EffectiveErtSource.Monthly, monthly.StartTime, monthly.Rides, monthly.Note);
        }

        return EffectiveErt.NotConfigured;
    }
}
