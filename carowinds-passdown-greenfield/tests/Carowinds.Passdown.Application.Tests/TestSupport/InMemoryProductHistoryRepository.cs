using Carowinds.Passdown.Application.Persistence;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

internal sealed class InMemoryProductHistoryRepository : IProductHistoryRepository
{
    private readonly List<ProductHistoryEntry> _entries = [];

    public IReadOnlyList<ProductHistoryEntry> AllEntries => _entries;

    public Task AppendAsync(ProductHistoryEntry entry, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProductHistoryEntry>> GetHistoryAsync(string productKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProductHistoryEntry>>(
            _entries.Where(e => e.ProductKey == productKey).OrderBy(e => e.RecordedAt).ToList());
}
