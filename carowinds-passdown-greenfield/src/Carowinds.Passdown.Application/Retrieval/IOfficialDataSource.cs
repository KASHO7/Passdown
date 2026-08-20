using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Application.Retrieval;

/// <summary>
/// One official Carowinds/Six Flags source adapter (spec §16's suggested
/// <c>IOfficialDataSource</c> shape). Implementations live in Infrastructure — this interface is
/// the abstraction/port Application code depends on instead (CLAUDE.md "Architecture";
/// <c>docs/architecture.md</c>: "Interfaces/abstractions for external systems ... belong in
/// Application; Infrastructure supplies the concrete adapters").
///
/// Deliberately does not expose a <c>Freshness</c>/cadence member the way spec §16's sketch
/// does: refresh cadence is already owned per <see cref="SourceCategory"/> by
/// <see cref="Freshness.FreshnessPolicyCatalog"/> and evaluated per <see cref="SourceId"/> by
/// <see cref="Freshness.IFreshnessPlanner"/> (Phase 2). Restating a cadence here would duplicate
/// that scheduling state instead of reusing it.
/// </summary>
public interface IOfficialDataSource<TPayload>
{
    /// <summary>Stable identifier for this adapter — the same id persisted scheduling state (spec §6.2's <c>source-state</c>) is keyed by.</summary>
    string SourceId { get; }

    SourceCategory Category { get; }

    Task<SourceResult<TPayload>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken);
}
