namespace Carowinds.Passdown.Domain.Ert;

/// <summary>
/// Which state an Early Ride Time resolution landed in for one operating date (Phase 17B). Distinct
/// from <see cref="ErtConfigurationState"/> — that is what a supervisor/associate persisted for a
/// month or a date; this is what <see cref="ErtPrecedenceResolver.Resolve"/> produced after applying
/// override-over-monthly precedence, including the case where nothing was ever persisted at all.
/// </summary>
public enum EffectiveErtKind
{
    /// <summary>Neither a date override nor a monthly configuration exists for this date. Never fabricate a time for this state — spec Part 6/34/35.</summary>
    NotConfigured,

    /// <summary>A month (or a date override) was explicitly confirmed to have no Early Ride Time — distinct from <see cref="NotConfigured"/> (spec Part 11).</summary>
    NoErt,

    /// <summary>A start time and ride list are in effect, sourced per <see cref="EffectiveErt.Source"/>.</summary>
    Configured,
}

/// <summary>Which persisted record produced a <see cref="EffectiveErtKind.Configured"/> or <see cref="EffectiveErtKind.NoErt"/> resolution.</summary>
public enum EffectiveErtSource
{
    Monthly,
    Override,
}

/// <summary>Whether a persisted monthly configuration or date override represents an active Early Ride Time, or an explicit "none" for that scope (spec Part 11/38.H).</summary>
public enum ErtConfigurationState
{
    Configured,
    NoErt,
}

/// <summary>
/// A persisted monthly Early Ride Time default, reduced to the shape <see cref="ErtPrecedenceResolver"/>
/// needs — pure input, no identity/audit fields (those live on the Application-layer
/// <c>MonthlyErtConfiguration</c> this is projected from).
/// </summary>
public sealed record MonthlyErtSnapshot(
    int Year,
    int Month,
    ErtConfigurationState State,
    TimeOnly? StartTime,
    IReadOnlyList<string> Rides,
    string? Note);

/// <summary>A persisted per-date Early Ride Time override, reduced the same way as <see cref="MonthlyErtSnapshot"/>.</summary>
public sealed record ErtOverrideSnapshot(
    DateOnly OperatingDate,
    ErtConfigurationState State,
    TimeOnly? StartTime,
    IReadOnlyList<string> Rides,
    string? Reason);

/// <summary>
/// The one resolved Early Ride Time value for an operating date (Phase 17B) — the single shape the
/// Associate screen, Dashboard, PDF, and finalized snapshot all render from, so no consumer ever
/// re-derives ERT independently. <see cref="Note"/> carries the monthly configuration's note or the
/// date override's reason, whichever produced this result.
/// </summary>
public sealed record EffectiveErt(
    EffectiveErtKind Kind,
    EffectiveErtSource? Source,
    TimeOnly? StartTime,
    IReadOnlyList<string> Rides,
    string? Note)
{
    public static EffectiveErt NotConfigured { get; } = new(EffectiveErtKind.NotConfigured, null, null, [], null);
}
