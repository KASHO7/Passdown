using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

internal sealed class FakeSeasonPriceHistoryService : ISeasonPriceHistoryService
{
    public List<PriceHistoryProduct> Products { get; } = [];

    public Dictionary<string, List<SeasonPriceHistoryInterval>> Intervals { get; } = [];

    public bool FailList { get; set; }

    public Task<IReadOnlyList<PriceHistoryProduct>> ListTrackedProductsAsync(CancellationToken cancellationToken = default) =>
        FailList
            ? Task.FromException<IReadOnlyList<PriceHistoryProduct>>(new InvalidOperationException("Simulated failure."))
            : Task.FromResult<IReadOnlyList<PriceHistoryProduct>>(Products.ToList());

    public Task<IReadOnlyList<PriceHistoryEntry>> GetHistoryAsync(string productKey, CancellationToken cancellationToken = default)
    {
        var intervals = Intervals.GetValueOrDefault(productKey) ?? [];
        var entries = new List<PriceHistoryEntry>();
        SeasonPriceHistoryInterval? previous = null;
        foreach (var interval in intervals.OrderBy(i => i.Sequence))
        {
            entries.Add(new PriceHistoryEntry(interval, previous is null ? null : SeasonPriceChange.Between(previous.State, interval.State)));
            previous = interval;
        }

        return Task.FromResult<IReadOnlyList<PriceHistoryEntry>>(entries);
    }

    public FakeSeasonPriceHistoryService With(string key, string name, bool active, PriceHistoryFamily family, params (decimal Amount, PriceKind Kind, DateTimeOffset At)[] states)
    {
        Products.Add(new PriceHistoryProduct(key, name, family, active, states.Length));
        Intervals[key] = states.Select((s, i) => new SeasonPriceHistoryInterval(
            key, i + 1, s.Amount, s.Kind, null, null, name, "Carowinds Season Passes", s.At, s.At, SeasonPriceHistoryOrigin.Observed)).ToList();
        return this;
    }
}
