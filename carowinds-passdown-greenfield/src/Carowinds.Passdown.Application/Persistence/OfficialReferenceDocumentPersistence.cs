using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Persistence;

/// <summary>
/// Phase 31: one last-known-good official reference document per source (the event schedule, the
/// deals snapshot). The last successful payload and the last attempt are stored separately, so a
/// failed check records its error without ever replacing the verified payload (CLAUDE.md
/// "Persistence": a failed retrieval must not overwrite last-known-good data). Local cache only —
/// not part of Personal Sync.
/// </summary>
public sealed record OfficialReferenceDocumentRecord(
    string SourceId,
    DateTimeOffset? SuccessCheckedAt,
    Uri? SuccessSourceUrl,
    string? SuccessDetail,
    string? SuccessPayloadJson,
    RetrievalStatus LastAttemptStatus,
    DateTimeOffset LastAttemptCheckedAt,
    string LastAttemptDetail,
    string? LastAttemptTechnicalDetail,
    Uri LastAttemptSourceUrl)
{
    public bool LastAttemptFailed => LastAttemptStatus is not RetrievalStatus.Verified;
}

public interface IOfficialReferenceDocumentRepository
{
    Task<OfficialReferenceDocumentRecord?> GetAsync(string sourceId, CancellationToken cancellationToken = default);

    /// <summary>Stores a verified payload as the new last-known-good and as the last attempt.</summary>
    Task SaveSuccessAsync(string sourceId, SourceEvidence evidence, string payloadJson, CancellationToken cancellationToken = default);

    /// <summary>Records a failed/unverified attempt only; any stored successful payload is left untouched.</summary>
    Task SaveAttemptAsync(string sourceId, SourceEvidence evidence, CancellationToken cancellationToken = default);
}
