using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Application.Associate;

/// <summary>Phase 21A audit classes for whether an official pricing source can describe a past date.</summary>
public enum HistoricalPricingCapability
{
    /// <summary>A — the official source returns past dates authoritatively.</summary>
    RetrievesPastDates,

    /// <summary>B — the source cannot return past dates, but exact-date results the app already recorded remain usable.</summary>
    StoredObservationsOnly,

    /// <summary>C — the source describes only the current offering; never evidence for a past date.</summary>
    CurrentOnly,

    /// <summary>D — not yet established.</summary>
    Unknown,
}

public sealed record HistoricalPricingSourceCapability(SourceCategory Category, HistoricalPricingCapability Capability, string Finding);

/// <summary>
/// Phase 21A historical pricing source audit (docs/phase21a-pricing-historical-integrity.md),
/// live-checked 2026-10-16: every accesso dated keyword answered "No dates found" for past open
/// dates, and every catalog page lists only today's offering. No official source of past ticket
/// prices was found, so no historical pricing backfill exists — past dates use only what was recorded.
/// Diagnostics print this list; nothing branches on it.
/// </summary>
public static class HistoricalPricingSourceCapabilities
{
    private const string NoPastDates = "Official calendar returns no past dates; recorded exact-date results are used.";
    private const string CurrentCatalog = "Current catalog page only; never shown for past dates.";

    public static IReadOnlyList<HistoricalPricingSourceCapability> All { get; } =
    [
        new(SourceCategory.DatedAdmissionTickets, HistoricalPricingCapability.StoredObservationsOnly, NoPastDates),
        new(SourceCategory.DatedFastLane, HistoricalPricingCapability.StoredObservationsOnly, NoPastDates),
        new(SourceCategory.HauntedAttractions, HistoricalPricingCapability.StoredObservationsOnly, NoPastDates),
        new(SourceCategory.DatedBringAFriend, HistoricalPricingCapability.StoredObservationsOnly, NoPastDates),
        new(SourceCategory.BringAFriendDuo, HistoricalPricingCapability.StoredObservationsOnly, NoPastDates),
        new(SourceCategory.DatedMilitaryFirstResponder, HistoricalPricingCapability.StoredObservationsOnly, NoPastDates),
        new(SourceCategory.Promotions, HistoricalPricingCapability.CurrentOnly, CurrentCatalog),
        new(SourceCategory.MilitaryFirstResponder, HistoricalPricingCapability.CurrentOnly, CurrentCatalog),
        new(SourceCategory.SeasonPasses, HistoricalPricingCapability.CurrentOnly, CurrentCatalog + " Advertised pass price changes are kept in Season Price History."),
        new(SourceCategory.AddOns, HistoricalPricingCapability.CurrentOnly, CurrentCatalog + " Season pass add-on price changes are kept in Season Price History."),
    ];
}
