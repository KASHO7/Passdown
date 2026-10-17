using Carowinds.Passdown.Application.Persistence;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

internal sealed class InMemorySeasonPriceHistoryRepository : ISeasonPriceHistoryRepository
{
    private readonly List<SeasonPriceHistoryInterval> _intervals = [];

    public IReadOnlyList<SeasonPriceHistoryInterval> All => _intervals;

    /// <summary>When set, <see cref="InsertAsync"/> throws (simulates a failed history write).</summary>
    public bool FailInserts { get; set; }

    public bool FailReads { get; set; }

    public Task<SeasonPriceHistoryInterval?> GetLatestAsync(string productKey, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(_intervals.Where(i => i.ProductKey == productKey).OrderByDescending(i => i.Sequence).FirstOrDefault());

    public Task InsertAsync(SeasonPriceHistoryInterval interval, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        if (FailInserts)
        {
            throw new InvalidOperationException("Simulated price-history write failure.");
        }

        if (_intervals.Any(i => i.ProductKey == interval.ProductKey && i.Sequence == interval.Sequence))
        {
            throw new InvalidOperationException("Duplicate (product, sequence).");
        }

        _intervals.Add(interval);
        return Task.CompletedTask;
    }

    public Task ConfirmAsync(string productKey, int sequence, DateTimeOffset confirmedAt, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        var index = _intervals.FindIndex(i => i.ProductKey == productKey && i.Sequence == sequence);
        if (index >= 0 && confirmedAt > _intervals[index].LastConfirmedAt)
        {
            _intervals[index] = _intervals[index] with { LastConfirmedAt = confirmedAt };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SeasonPriceHistoryInterval>> GetHistoryAsync(string productKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SeasonPriceHistoryInterval>>(_intervals.Where(i => i.ProductKey == productKey).OrderBy(i => i.Sequence).ToList());

    public Task<IReadOnlySet<string>> GetTrackedProductKeysAsync(CancellationToken cancellationToken = default)
    {
        if (FailReads)
        {
            throw new InvalidOperationException("Simulated price-history read failure.");
        }

        return Task.FromResult<IReadOnlySet<string>>(_intervals.Select(i => i.ProductKey).ToHashSet());
    }

    public Task<SeasonPriceHistorySummary> GetSummaryAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new SeasonPriceHistorySummary(
            _intervals.Select(i => i.ProductKey).Distinct().Count(),
            _intervals.Count,
            _intervals.Where(i => i.Sequence > 1).Select(i => (DateTimeOffset?)i.FirstObservedAt).Max()));
}
