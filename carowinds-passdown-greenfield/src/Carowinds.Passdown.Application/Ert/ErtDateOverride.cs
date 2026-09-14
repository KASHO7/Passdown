using Carowinds.Passdown.Domain.Ert;

namespace Carowinds.Passdown.Application.Ert;

/// <summary>
/// A persistent, user-authored Early Ride Time exception for one specific operating date (Phase 17B
/// spec Part 3) — maintenance, a delayed opening, a temporary attraction swap. Wins over the month's
/// default for that date only (spec Part 4); never required for normal daily use (spec Part 3), and
/// deliberately allowed to exist even when no <see cref="MonthlyErtConfiguration"/> exists for its
/// month at all (spec Part 38.I — an exceptional date can be known before the rest of the month is
/// configured).
/// </summary>
public sealed record ErtDateOverride(
    DateOnly OperatingDate,
    ErtConfigurationState State,
    TimeOnly? StartTime,
    IReadOnlyList<string> Rides,
    string? Reason,
    DateTimeOffset ModifiedUtc)
{
    public ErtOverrideSnapshot ToSnapshot() => new(OperatingDate, State, StartTime, Rides, Reason);
}
