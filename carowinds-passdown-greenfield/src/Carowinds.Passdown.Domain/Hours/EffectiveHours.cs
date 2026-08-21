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
/// override precedence, plus the derived North Gate/Avenue opening times. Pure output of
/// <see cref="ParkHoursCalculator.Calculate"/> — never constructed directly by a caller that
/// wants to assert a status/time combination the calculator itself wouldn't produce.
/// </summary>
public sealed record EffectiveHours
{
    public EffectiveHoursStatus Status { get; }
    public TimeOnly? Opening { get; }
    public TimeOnly? Closing { get; }
    public TimeOnly? NorthGateOpening { get; }
    public TimeOnly? AvenueOpening { get; }
    public bool IsManualOverrideApplied { get; }
    public bool IsPrivateBuyoutApplied { get; }

    internal EffectiveHours(
        EffectiveHoursStatus status,
        TimeOnly? opening,
        TimeOnly? closing,
        bool isManualOverrideApplied,
        bool isPrivateBuyoutApplied)
    {
        Status = status;
        Opening = opening;
        Closing = closing;
        NorthGateOpening = opening?.AddMinutes(-30);
        AvenueOpening = opening?.AddMinutes(-45);
        IsManualOverrideApplied = isManualOverrideApplied;
        IsPrivateBuyoutApplied = isPrivateBuyoutApplied;
    }
}
