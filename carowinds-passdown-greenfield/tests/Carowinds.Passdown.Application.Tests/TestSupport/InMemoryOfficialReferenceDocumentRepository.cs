using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

/// <summary>Phase 31: in-memory <see cref="IOfficialReferenceDocumentRepository"/> with the same success/attempt separation as SQLite.</summary>
internal sealed class InMemoryOfficialReferenceDocumentRepository : IOfficialReferenceDocumentRepository
{
    private readonly Dictionary<string, OfficialReferenceDocumentRecord> _records = new(StringComparer.Ordinal);

    public int SaveCount { get; private set; }

    public Task<OfficialReferenceDocumentRecord?> GetAsync(string sourceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_records.GetValueOrDefault(sourceId));

    public Task SaveSuccessAsync(string sourceId, SourceEvidence evidence, string payloadJson, CancellationToken cancellationToken = default)
    {
        if (evidence.Status != RetrievalStatus.Verified)
        {
            throw new ArgumentException("Only a verified check can replace the last-known-good document.", nameof(evidence));
        }

        SaveCount++;
        _records[sourceId] = new OfficialReferenceDocumentRecord(
            sourceId, evidence.TimeChecked, evidence.SourceUrl, evidence.Detail, payloadJson,
            evidence.Status, evidence.TimeChecked, evidence.Detail, evidence.TechnicalDetail, evidence.SourceUrl);
        return Task.CompletedTask;
    }

    public Task SaveAttemptAsync(string sourceId, SourceEvidence evidence, CancellationToken cancellationToken = default)
    {
        SaveCount++;
        var previous = _records.GetValueOrDefault(sourceId);
        _records[sourceId] = new OfficialReferenceDocumentRecord(
            sourceId, previous?.SuccessCheckedAt, previous?.SuccessSourceUrl, previous?.SuccessDetail, previous?.SuccessPayloadJson,
            evidence.Status, evidence.TimeChecked, evidence.Detail, evidence.TechnicalDetail, evidence.SourceUrl);
        return Task.CompletedTask;
    }
}
