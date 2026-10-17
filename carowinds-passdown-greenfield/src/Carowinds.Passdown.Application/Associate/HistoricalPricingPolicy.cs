using Carowinds.Passdown.Application.Finalization;

namespace Carowinds.Passdown.Application.Associate;

/// <summary>Whether a composed date's prices describe the current offering or a past date (Phase 21A).</summary>
public enum AssociatePricingTimeframe
{
    /// <summary>Today, a future date, or a date still inside its overnight passdown window: current and date-specific sources apply as before.</summary>
    Current,

    /// <summary>A past date: only prices with evidence for that exact date are shown; the current catalog is never substituted.</summary>
    Historical,
}

/// <summary>
/// Phase 21A historical selected-date pricing rule (docs/phase21a-pricing-historical-integrity.md).
/// The current catalog (season passes, add-ons, promotions, the Military "Starting From" card) says
/// what is offered now, not what was offered on an earlier date, so it is never evidence for a past
/// date. A past date shows only official prices recorded for that exact date — stored exact-date
/// results whose applicable date equals the selected date — and an honest empty state otherwise.
/// No month, season or product-name rule is involved.
///
/// A date becomes historical at the same instant it becomes eligible for finalization
/// (<see cref="FinalizationEligibility.EligibleForFinalizationAt"/>, 6:00 AM Eastern the next day),
/// so an overnight supervisor finishing the previous day's passdown still sees its normal pricing,
/// and the finalized History record — built by the scheduled finalizer from the local data present
/// when the date closes — is never affected by this rule.
/// </summary>
public static class HistoricalPricingPolicy
{
    public const string UnavailableMessage = "Historical pricing not available for this date.";

    public static AssociatePricingTimeframe TimeframeFor(DateOnly operatingDate, DateTimeOffset utcNow) =>
        utcNow >= FinalizationEligibility.EligibleForFinalizationAt(operatingDate)
            ? AssociatePricingTimeframe.Historical
            : AssociatePricingTimeframe.Current;

    /// <summary>
    /// True when <paramref name="product"/> carries its own evidence for <paramref name="operatingDate"/>:
    /// an official price confirmed for exactly that date. Custom products are the draft's own
    /// manual entries, not official pricing, and stay with their draft.
    /// </summary>
    public static bool HasEvidenceFor(AssociateProduct product, DateOnly operatingDate)
    {
        ArgumentNullException.ThrowIfNull(product);
        return product.IsCustom || (product.DateSpecific && product.ApplicableDate == operatingDate);
    }
}
