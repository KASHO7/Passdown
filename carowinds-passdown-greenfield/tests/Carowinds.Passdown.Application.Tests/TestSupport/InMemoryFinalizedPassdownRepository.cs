using Carowinds.Passdown.Application.Persistence;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

/// <summary>In-memory <see cref="IFinalizedPassdownRepository"/> mirroring the SQLite implementation's insert-only, unique-per-date contract (rejects a duplicate <see cref="FinalizedPassdown.PassdownDate"/> in <see cref="AddAsync"/>, exactly as the real table's unique index would).</summary>
internal sealed class InMemoryFinalizedPassdownRepository : IFinalizedPassdownRepository
{
    private readonly Dictionary<DateOnly, FinalizedPassdown> _records = new();

    public Task<FinalizedPassdown?> GetByDateAsync(DateOnly passdownDate, CancellationToken cancellationToken = default) =>
        Task.FromResult(_records.GetValueOrDefault(passdownDate));

    public Task<bool> ExistsForDateAsync(DateOnly passdownDate, CancellationToken cancellationToken = default) =>
        Task.FromResult(_records.ContainsKey(passdownDate));

    public Task AddAsync(FinalizedPassdown finalizedPassdown, CancellationToken cancellationToken = default)
    {
        if (!_records.TryAdd(finalizedPassdown.PassdownDate, finalizedPassdown))
        {
            throw new InvalidOperationException($"A finalized passdown already exists for {finalizedPassdown.PassdownDate:yyyy-MM-dd}.");
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FinalizedPassdownSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FinalizedPassdownSummary> summaries = _records.Values
            .OrderByDescending(r => r.PassdownDate)
            .Select(r => new FinalizedPassdownSummary(r.PassdownDate, r.FinalizedAtUtc, r.Origin))
            .ToList();

        return Task.FromResult(summaries);
    }
}
