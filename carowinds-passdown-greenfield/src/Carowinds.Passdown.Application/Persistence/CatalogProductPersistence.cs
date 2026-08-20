using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Persistence;

/// <summary>
/// What a caller supplies to persist a catalog product, per spec §7.2. Keyed by the
/// real <see cref="CatalogProductKey"/> because a write always originates from a
/// caller that already built or reconstructed one (e.g. from a retrieval adapter or
/// a lifecycle transition) — never from stored text alone.
/// </summary>
public sealed record CatalogProductUpsert(
    CatalogProductKey Key,
    string DisplayName,
    ProductLifecycleState LifecycleState,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? LastVerifiedAt,
    int ConsecutiveConfirmedAbsenceCount,
    DateTimeOffset? ArchivedAt,
    string? ArchivedReason,
    decimal? CurrentPriceAmount,
    string? CurrentDisplayPrice,
    decimal? ComparisonPriceAmount,
    string? PriceQualifier,
    bool DateSpecific,
    DateOnly? ApplicableDate,
    SourceEvidence LastEvidence);

/// <summary>
/// A persisted catalog product read back from storage. <see cref="ProductKey"/> is
/// the raw stored identity text (<c>CatalogProductKey.ToString()</c> at write time),
/// not a reconstructed <see cref="CatalogProductKey"/> — turning stored text back
/// into a rich key belongs to whichever later phase actually needs to (product
/// reconciliation is explicitly out of scope here).
/// </summary>
public sealed record CatalogProductRecord(
    string ProductKey,
    SourceCategory Category,
    string DisplayName,
    ProductLifecycleState LifecycleState,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? LastVerifiedAt,
    int ConsecutiveConfirmedAbsenceCount,
    DateTimeOffset? ArchivedAt,
    string? ArchivedReason,
    decimal? CurrentPriceAmount,
    string? CurrentDisplayPrice,
    decimal? ComparisonPriceAmount,
    string? PriceQualifier,
    bool DateSpecific,
    DateOnly? ApplicableDate,
    SourceEvidence LastEvidence);

public interface ICatalogProductRepository
{
    Task<CatalogProductRecord?> GetAsync(CatalogProductKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or fully replaces the stored row for <paramref name="product"/>'s
    /// key. Callers decide add/update/archive semantics (spec §7.3); this only
    /// persists the resulting state. Pass <paramref name="unitOfWork"/> to commit
    /// this write atomically together with a related history append.
    /// </summary>
    Task UpsertAsync(CatalogProductUpsert product, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CatalogProductRecord>> GetByCategoryAsync(
        SourceCategory category,
        ProductLifecycleState? lifecycleState = null,
        CancellationToken cancellationToken = default);
}
