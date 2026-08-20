using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Application.Persistence;

/// <summary>
/// Persisted scheduling state for one source adapter, per spec §6.2. Plain data —
/// not <c>Carowinds.Passdown.Domain.Retrieval.SourceState</c> — because a source id
/// is a persistence-layer concern the Domain type doesn't carry, and because this
/// is a read/write shape for storage, not a business aggregate with transition
/// rules. Reconciling this with freshness/scheduling decisions belongs to a later
/// phase.
/// </summary>
public sealed record SourceStateRecord(
    string SourceId,
    SourceCategory Category,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? NextDueAt,
    string? SourceFingerprint,
    int ConsecutiveFailureCount);

public interface ISourceStateRepository
{
    Task<SourceStateRecord?> GetAsync(string sourceId, CancellationToken cancellationToken = default);

    /// <summary>Inserts or fully replaces the stored state for <paramref name="state"/>'s source id.</summary>
    Task SaveAsync(SourceStateRecord state, CancellationToken cancellationToken = default);
}
