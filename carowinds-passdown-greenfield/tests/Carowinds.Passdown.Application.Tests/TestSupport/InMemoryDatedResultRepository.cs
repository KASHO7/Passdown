using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

internal sealed class InMemoryDatedResultRepository : IDatedResultRepository
{
    private readonly Dictionary<DatedResultKey, DatedResultRecord> _records = new();

    public Task<DatedResultRecord?> GetAsync(DatedResultKey key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_records.GetValueOrDefault(key));

    public Task<IReadOnlyList<DatedResultRecord>> GetByRequestedDateAsync(DateOnly requestedDate, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DatedResultRecord>>(
            _records.Values.Where(r => r.Key.Date == requestedDate).ToList());

    public Task RecordSuccessAsync(DatedResultKey key, SourceEvidence evidence, string? payloadJson, CancellationToken cancellationToken = default)
    {
        _records[key] = new DatedResultRecord(key, evidence, payloadJson, evidence);
        return Task.CompletedTask;
    }

    public Task RecordFailedAttemptAsync(DatedResultKey key, SourceEvidence evidence, CancellationToken cancellationToken = default)
    {
        var existing = _records.GetValueOrDefault(key);
        _records[key] = new DatedResultRecord(key, existing?.SuccessEvidence, existing?.SuccessPayloadJson, evidence);
        return Task.CompletedTask;
    }
}
