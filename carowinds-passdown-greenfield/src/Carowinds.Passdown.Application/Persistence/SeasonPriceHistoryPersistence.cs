using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Application.Persistence;

/// <summary>How a price-history interval came to exist.</summary>
public enum SeasonPriceHistoryOrigin
{
    /// <summary>Recorded from a successful official retrieval by Phase 20A or later.</summary>
    Observed,

    /// <summary>
    /// Reconstructed once, by the schema-16 migration, from <c>product_history</c> rows that were
    /// themselves written from successful official retrievals before Phase 20A existed.
    /// </summary>
    Backfilled,
}

/// <summary>
/// Phase 20A: one advertised price interval of a tracked season product (<c>season_price_history</c>).
/// <see cref="Sequence"/> is 1-based and strictly increasing per product, so adjacent intervals are
/// well defined and a duplicate concurrent insert collides on the (product, sequence) unique key.
///
/// <see cref="FirstObservedAt"/> is the first successful official check that showed this state;
/// <see cref="LastConfirmedAt"/> the most recent successful check that still showed it. The real change
/// happened at some unknown time between the previous interval's <see cref="LastConfirmedAt"/> and this
/// one's <see cref="FirstObservedAt"/>. Only <see cref="LastConfirmedAt"/> of the newest interval is
/// ever updated; amounts and kinds are never rewritten.
/// </summary>
public sealed record SeasonPriceHistoryInterval(
    string ProductKey,
    int Sequence,
    decimal Amount,
    PriceKind PriceKind,
    string? DisplayPrice,
    string? PriceQualifier,
    string DisplayName,
    string SourceName,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset LastConfirmedAt,
    SeasonPriceHistoryOrigin Origin)
{
    public SeasonPriceState State => new(Amount, PriceKind);
}

/// <summary>Counts for diagnostics — never product names, prices or history rows.</summary>
public sealed record SeasonPriceHistorySummary(
    int TrackedProductCount,
    int PriceStateCount,
    DateTimeOffset? MostRecentChangeAt);

public interface ISeasonPriceHistoryRepository
{
    /// <summary>The newest interval for <paramref name="productKey"/>, read inside <paramref name="unitOfWork"/> when given.</summary>
    Task<SeasonPriceHistoryInterval?> GetLatestAsync(string productKey, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default);

    /// <summary>Inserts a new interval. Throws if (product, sequence) already exists.</summary>
    Task InsertAsync(SeasonPriceHistoryInterval interval, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default);

    /// <summary>Moves the newest interval's last-confirmed time forward (never backward).</summary>
    Task ConfirmAsync(string productKey, int sequence, DateTimeOffset confirmedAt, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default);

    /// <summary>All intervals for one product, oldest first.</summary>
    Task<IReadOnlyList<SeasonPriceHistoryInterval>> GetHistoryAsync(string productKey, CancellationToken cancellationToken = default);

    /// <summary>Every product key that has at least one recorded interval.</summary>
    Task<IReadOnlySet<string>> GetTrackedProductKeysAsync(CancellationToken cancellationToken = default);

    Task<SeasonPriceHistorySummary> GetSummaryAsync(CancellationToken cancellationToken = default);
}
