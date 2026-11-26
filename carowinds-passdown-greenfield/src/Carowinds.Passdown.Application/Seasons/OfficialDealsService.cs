using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain.Promotions;

namespace Carowinds.Passdown.Application.Seasons;

public sealed record DealEntry(OfficialPromotion Promotion, PromotionStatusAssessment Assessment);

/// <summary>
/// Phase 31: the official deals, grouped for display. Grouping is recomputed from the cached document
/// on every read (status depends on "today"), so an offer whose deadline passes moves out of
/// <see cref="Current"/> even when no refresh has run.
/// </summary>
public sealed record DealsOverview(
    IReadOnlyList<DealEntry> Current,
    IReadOnlyList<DealEntry> Upcoming,
    IReadOnlyList<DealEntry> PreviouslyVerified,
    IReadOnlyList<DealEntry> Expired,
    OfficialDocumentStatus Status)
{
    public static DealsOverview NotChecked { get; } = new([], [], [], [], OfficialDocumentStatus.NotChecked);

    public bool HasAnyOffers => Current.Count + Upcoming.Count + PreviouslyVerified.Count + Expired.Count > 0;
}

public interface IOfficialDealsService
{
    /// <summary>Reads the cached official deals only — never retrieves online.</summary>
    Task<DealsOverview> GetOverviewAsync(CancellationToken cancellationToken = default);
}

public sealed class OfficialDealsService(IOfficialReferenceDocumentRepository documents, IClock clock) : IOfficialDealsService
{
    public async Task<DealsOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var record = await documents.GetAsync(OfficialReferenceDocumentIds.Deals, cancellationToken).ConfigureAwait(false);
        var status = OfficialDocumentStatus.From(record);
        var document = OfficialSeasonJson.TryDeserialize<OfficialDealsDocument>(record?.SuccessPayloadJson);
        if (document is null)
        {
            return DealsOverview.NotChecked with { Status = status };
        }

        var now = EasternTimeZone.ConvertFromUtc(clock.UtcNow);
        var today = DateOnly.FromDateTime(now.DateTime);
        return Group(document, status, today, now);
    }

    public static DealsOverview Group(OfficialDealsDocument document, OfficialDocumentStatus status, DateOnly today, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(document);

        var entries = document.Promotions
            .Select(p => new DealEntry(p, PromotionStatusEvaluator.Evaluate(p, today, status.LastSuccessAt, now)))
            .ToList();

        IReadOnlyList<DealEntry> Of(PromotionStatus s) => entries
            .Where(e => e.Assessment.Status == s)
            .OrderBy(e => e.Promotion.PurchaseDeadline?.Date ?? DateOnly.MaxValue)
            .ThenBy(e => e.Promotion.Title, StringComparer.Ordinal)
            .ToList();

        var expired = entries
            .Where(e => e.Assessment.Status == PromotionStatus.Expired)
            .OrderByDescending(e => e.Promotion.LastVerifiedAt)
            .ToList();

        return new DealsOverview(Of(PromotionStatus.Current), Of(PromotionStatus.Upcoming), Of(PromotionStatus.Unknown), expired, status);
    }
}
