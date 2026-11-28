using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Refresh;

/// <summary>
/// Pre-RC.15 post-closing pricing retention (docs/pre-rc15-post-closing-pricing-retention.md). The
/// official accesso calendar stops listing the current date once its sales window ends (live-observed
/// 2026-11-27 11:12 PM ET: a window starting that day listed only later dates), so the next automatic
/// refresh answers a successful <see cref="RetrievalStatus.Unavailable"/> with no products. That answer
/// says the date is no longer offered; it is not evidence that the prices verified earlier for that
/// exact date were wrong. For the dated pricing sources whose recorded exact-date results are the only
/// record of a date's prices (<see cref="HistoricalPricingCapability.StoredObservationsOnly"/>, Phase
/// 21A), such an answer is recorded as the latest attempt only, and the earlier verified payload and its
/// original evidence stay as the last-known-good value. The presentation then marks those prices
/// "previously verified" (<see cref="AssociateVerificationState.PreviouslyVerified"/>), never current.
/// <para>
/// Bounded on purpose: only the same category and exact date, only when the stored value is a
/// <see cref="RetrievalStatus.Verified"/> payload, and only for payload-less
/// <see cref="RetrievalStatus.Unavailable"/>/<see cref="RetrievalStatus.NotPublished"/> answers. A new
/// verified payload — a price change or a product removed while the date is still on sale — always
/// replaces the stored one. Park hours, shows and catalog sources are unaffected.
/// </para>
/// </summary>
public static class VerifiedDatedPriceRetention
{
    private static readonly IReadOnlySet<SourceCategory> RetainingCategories = HistoricalPricingSourceCapabilities.All
        .Where(c => c.Capability == HistoricalPricingCapability.StoredObservationsOnly)
        .Select(c => c.Category)
        .ToHashSet();

    /// <summary>The dated pricing categories this rule covers (the Phase 21A stored-observations-only set).</summary>
    public static bool AppliesTo(SourceCategory category) => RetainingCategories.Contains(category);

    /// <summary>True for a successful answer that carries no products and says only that the date is not (or no longer) offered.</summary>
    public static bool IsPayloadlessNoOfferAnswer(RetrievalStatus status, bool hasPayload) =>
        !hasPayload && status is RetrievalStatus.Unavailable or RetrievalStatus.NotPublished;

    /// <summary>True when <paramref name="existing"/> holds verified prices that a payload-less no-offer answer must not erase.</summary>
    public static bool ShouldRetain(SourceCategory category, DatedResultRecord? existing, RetrievalStatus newStatus, bool newHasPayload) =>
        AppliesTo(category)
        && IsPayloadlessNoOfferAnswer(newStatus, newHasPayload)
        && existing is { SuccessEvidence.Status: RetrievalStatus.Verified, SuccessPayloadJson: not null };
}
