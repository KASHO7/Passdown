using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Freshness;

/// <summary>
/// The outcome of evaluating one source's due-ness (spec §6.1/§6.6). Read-only:
/// deciding what to do about a due source (retrieve, skip, retry) is a later
/// phase's concern — this only reports the scheduling facts.
/// </summary>
public sealed record FreshnessDecision(
    string SourceId,
    SourceCategory Category,
    bool IsDue,
    DateTimeOffset EvaluatedAt,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? NextDueAt);

/// <summary>
/// Determines whether a source is due for refresh, per its category's
/// <see cref="FreshnessPolicy"/> and its persisted
/// <see cref="SourceStateRecord"/> (spec §6.1/§6.6/§6.7). Never
/// triggered by a selected-date change (spec §6.3) — this only reasons about a
/// source's own cadence and last-success history, independent of any UI state.
/// </summary>
public interface IFreshnessPlanner
{
    Task<FreshnessDecision> EvaluateAsync(string sourceId, SourceCategory category, CancellationToken cancellationToken = default);
}
