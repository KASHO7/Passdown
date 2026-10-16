namespace Carowinds.Passdown.Domain.Catalog;

/// <summary>
/// Phase 20A: one advertised price state of a tracked season product — the amount <b>and</b> its
/// semantic kind. Record equality covers both, so "$99.00" (<see cref="PriceKind.StandardCatalog"/>)
/// and "From $99.00" (<see cref="PriceKind.StartingFrom"/>) are different states even though the
/// numeric amount is the same.
/// </summary>
public sealed record SeasonPriceState(decimal Amount, PriceKind Kind);

/// <summary>What one successful official observation of a tracked product means for its price history.</summary>
public enum SeasonPriceObservationOutcome
{
    /// <summary>First state ever recorded for the product: a baseline, not a change.</summary>
    Baseline,

    /// <summary>Same state as the current interval: only its last-confirmed time moves.</summary>
    Confirmed,

    /// <summary>The amount changed; the kind did not.</summary>
    PriceChanged,

    /// <summary>The kind changed (e.g. "$105.00" → "From $105.00"); the amount did not.</summary>
    SemanticsChanged,

    /// <summary>Both the amount and the kind changed.</summary>
    PriceAndSemanticsChanged,

    /// <summary>
    /// The product came back after being retired with the same state it had before. A new interval
    /// starts (the product became available again at a new time) even though nothing about the price
    /// itself differs.
    /// </summary>
    Reappeared,
}

/// <summary>
/// Phase 20A: the pure interval rules for season-product price history. Repeated identical
/// observations extend one interval (first observed / last confirmed); any difference in amount or
/// kind closes it and opens a new one, so a temporary sale ($105 → $95 → $105) is three intervals.
/// Retirement is never a price event — it only means the next observation opens a new interval.
/// </summary>
public static class SeasonPriceTimeline
{
    public static SeasonPriceObservationOutcome Classify(
        SeasonPriceState? current,
        SeasonPriceState observed,
        bool resumedAfterRetirement)
    {
        ArgumentNullException.ThrowIfNull(observed);

        if (current is null)
        {
            return SeasonPriceObservationOutcome.Baseline;
        }

        var amountChanged = current.Amount != observed.Amount;
        var kindChanged = current.Kind != observed.Kind;

        return (amountChanged, kindChanged) switch
        {
            (true, true) => SeasonPriceObservationOutcome.PriceAndSemanticsChanged,
            (true, false) => SeasonPriceObservationOutcome.PriceChanged,
            (false, true) => SeasonPriceObservationOutcome.SemanticsChanged,
            _ => resumedAfterRetirement ? SeasonPriceObservationOutcome.Reappeared : SeasonPriceObservationOutcome.Confirmed,
        };
    }

    /// <summary>True when <paramref name="outcome"/> opens a new interval rather than extending the current one.</summary>
    public static bool StartsNewInterval(SeasonPriceObservationOutcome outcome) =>
        outcome != SeasonPriceObservationOutcome.Confirmed;
}

/// <summary>
/// Phase 20A: the difference between two adjacent price states — always derived, never stored.
/// <see cref="PercentChange"/> uses the previous amount as the denominator and is
/// <see langword="null"/> when the previous amount is zero (no meaningful percentage exists).
/// </summary>
public sealed record SeasonPriceChange(decimal AmountDelta, decimal? PercentChange, bool AmountChanged, bool KindChanged)
{
    public static SeasonPriceChange Between(SeasonPriceState previous, SeasonPriceState current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        var delta = current.Amount - previous.Amount;
        decimal? percent = previous.Amount == 0m ? null : delta / previous.Amount * 100m;
        return new SeasonPriceChange(delta, percent, delta != 0m, previous.Kind != current.Kind);
    }
}
