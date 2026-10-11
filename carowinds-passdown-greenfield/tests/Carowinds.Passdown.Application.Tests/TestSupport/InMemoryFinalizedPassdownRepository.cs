using Carowinds.Passdown.Application.Persistence;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

/// <summary>In-memory <see cref="IFinalizedPassdownRepository"/> mirroring the SQLite implementation's insert-only, unique-per-date contract (rejects a duplicate <see cref="FinalizedPassdown.PassdownDate"/> in <see cref="AddAsync"/>, exactly as the real table's unique index would).</summary>
internal sealed class InMemoryFinalizedPassdownRepository : IFinalizedPassdownRepository
{
    private readonly Dictionary<DateOnly, FinalizedPassdown> _records = new();

    public Task<FinalizedPassdown?> GetByDateAsync(DateOnly passdownDate, CancellationToken cancellationToken = default) =>
        Task.FromResult(_records.GetValueOrDefault(passdownDate));

    public Task<FinalizedPassdown?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_records.Values.FirstOrDefault(r => r.Id == id));

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
            .Select(r => new FinalizedPassdownSummary(r.PassdownDate, r.FinalizedAtUtc, r.Origin, r.HasFrozenPdf, r.PdfSizeBytes))
            .ToList();

        return Task.FromResult(summaries);
    }

    public Task<IReadOnlyList<FinalizedPassdownSyncMetadata>> ListForSyncAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FinalizedPassdownSyncMetadata> metadata = _records.Values
            .Select(r => new FinalizedPassdownSyncMetadata(r.Id, r.PassdownDate, r.ContentHash, r.PdfSha256, r.PdfSizeBytes))
            .ToList();

        return Task.FromResult(metadata);
    }
}
