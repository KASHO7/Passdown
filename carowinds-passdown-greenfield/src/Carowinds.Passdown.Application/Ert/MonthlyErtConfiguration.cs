using System.Globalization;
using Carowinds.Passdown.Domain.Ert;

namespace Carowinds.Passdown.Application.Ert;

/// <summary>
/// A persistent, user-authored Early Ride Time default for one calendar month (Phase 17B spec Parts
/// 2/5) — the record real-world ERT actually tracks (it stays the same for a whole month, changes
/// month to month), replacing the old per-date-derived behavior. Identity is <see cref="Year"/> +
/// <see cref="Month"/> together (spec Part 5: "do not key by month number alone" — September 2026 and
/// September 2027 are different configurations). No <see cref="AssociateProductIdentity"/>-style stable
/// ride identity exists for attractions anywhere in this codebase (spec Part 10 — <c>ParticipatingAttractions</c>
/// is, and remains, free text); <see cref="Rides"/> follows that existing precedent rather than
/// inventing a catalog.
/// </summary>
public sealed record MonthlyErtConfiguration(
    int Year,
    int Month,
    ErtConfigurationState State,
    TimeOnly? StartTime,
    IReadOnlyList<string> Rides,
    string? Note,
    DateTimeOffset ModifiedUtc)
{
    /// <summary>Canonical <c>yyyy-MM</c> identity — the sync entity id and the SQLite primary key alike (spec Parts 5/21).</summary>
    public string YearMonthKey => FormatKey(Year, Month);

    public static string FormatKey(int year, int month) =>
        string.Create(CultureInfo.InvariantCulture, $"{year:D4}-{month:D2}");

    public MonthlyErtSnapshot ToSnapshot() => new(Year, Month, State, StartTime, Rides, Note);
}
