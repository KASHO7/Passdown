namespace Carowinds.Passdown.Domain.Hours;

/// <summary>Whether the park (or the effective override in play) is open or closed for gate-time purposes, per spec §8.2.</summary>
public enum EffectiveHoursStatus
{
    /// <summary>No verified official hours, no override enabled — nothing to compute from yet.</summary>
    NotCheckedYet,

    /// <summary>Officially closed and no private-buyout override is enabled.</summary>
    Closed,

    /// <summary>Open, from whichever source (official/manual override/private buyout) took precedence.</summary>
    Open,
}

/// <summary>
/// The effective opening/closing time for one operating date, after applying spec §8.2's
/// override precedence, plus the North Gate/Avenue opening times. Each entrance has a calculated
/// default (opening −30/−45 minutes), an optional per-date manual override (Phase 18D), and the
/// effective value every consumer reads: <c>override ?? default</c>. Pure output of
/// <see cref="ParkHoursCalculator.Calculate"/> — never constructed directly by a caller that
/// wants to assert a status/time combination the calculator itself wouldn't produce.
/// </summary>
public sealed record EffectiveHours
{
    public EffectiveHoursStatus Status { get; }
    public TimeOnly? Opening { get; }
    public TimeOnly? Closing { get; }

    /// <summary>Effective North Gate opening: <see cref="NorthGateOverride"/> when one applies, otherwise <see cref="DefaultNorthGateOpening"/>.</summary>
    public TimeOnly? NorthGateOpening { get; }

    /// <summary>Effective Avenue opening: <see cref="AvenueOverride"/> when one applies, otherwise <see cref="DefaultAvenueOpening"/>.</summary>
    public TimeOnly? AvenueOpening { get; }

    /// <summary>The calculated North Gate opening (opening −30 minutes) — what "Reset to default" returns to.</summary>
    public TimeOnly? DefaultNorthGateOpening { get; }

    /// <summary>The calculated Avenue opening (opening −45 minutes) — what "Reset to default" returns to.</summary>
    public TimeOnly? DefaultAvenueOpening { get; }

    /// <summary>The draft's North Gate override when it is in effect (never on a <see cref="EffectiveHoursStatus.Closed"/> date); null otherwise.</summary>
    public TimeOnly? NorthGateOverride { get; }

    /// <summary>The draft's Avenue override when it is in effect (never on a <see cref="EffectiveHoursStatus.Closed"/> date); null otherwise.</summary>
    public TimeOnly? AvenueOverride { get; }

    public bool IsNorthGateOverridden => NorthGateOverride is not null;

    public bool IsAvenueOverridden => AvenueOverride is not null;

    public bool IsManualOverrideApplied { get; }
    public bool IsPrivateBuyoutApplied { get; }

    internal EffectiveHours(
        EffectiveHoursStatus status,
        TimeOnly? opening,
        TimeOnly? closing,
        bool isManualOverrideApplied,
        bool isPrivateBuyoutApplied,
        TimeOnly? northGateOverride = null,
        TimeOnly? avenueOverride = null)
    {
        Status = status;
        Opening = opening;
        Closing = closing;
        DefaultNorthGateOpening = opening?.AddMinutes(-30);
        DefaultAvenueOpening = opening?.AddMinutes(-45);

        // Phase 18D: an entrance override never makes a closed park look open — on a Closed date it
        // stays stored in the draft but is not applied. Otherwise it is authoritative for that date,
        // independent of the park hours and of the other entrance, with no ordering rule imposed.
        var overridesApply = status != EffectiveHoursStatus.Closed;
        NorthGateOverride = overridesApply ? northGateOverride : null;
        AvenueOverride = overridesApply ? avenueOverride : null;
        NorthGateOpening = NorthGateOverride ?? DefaultNorthGateOpening;
        AvenueOpening = AvenueOverride ?? DefaultAvenueOpening;
        IsManualOverrideApplied = isManualOverrideApplied;
        IsPrivateBuyoutApplied = isPrivateBuyoutApplied;
    }
}
