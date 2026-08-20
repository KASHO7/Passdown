using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Freshness;

/// <summary>
/// <see cref="IFreshnessPlanner"/> built from a persisted <see cref="ISourceStateRepository"/>
/// and a per-category <see cref="FreshnessPolicy"/> catalog (spec §6.1/§6.6/§6.7).
/// Depends only on Application-level abstractions (repository port, <see cref="IClock"/>),
/// so it needs no Infrastructure dependency and stays deterministically testable.
/// </summary>
public sealed class FreshnessPlanner(
    ISourceStateRepository sourceStateRepository,
    IClock clock,
    IReadOnlyDictionary<SourceCategory, FreshnessPolicy> policies) : IFreshnessPlanner
{
    public async Task<FreshnessDecision> EvaluateAsync(string sourceId, SourceCategory category, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        if (!policies.TryGetValue(category, out var policy))
        {
            throw new InvalidOperationException(
                $"No freshness policy is configured for category '{category}'. " +
                $"{nameof(FreshnessPolicyCatalog)} must cover every {nameof(SourceCategory)}.");
        }

        var record = await sourceStateRepository.GetAsync(sourceId, cancellationToken).ConfigureAwait(false);
        var lastSuccessAt = record?.LastSuccessAt;

        // Only "now" needs Eastern conversion for IsDue's day-boundary comparison
        // (DateTimeOffset comparisons against lastSuccessAt are offset-agnostic —
        // they compare the same absolute instant either way). ComputeNextDueAt,
        // in contrast, extracts a calendar date from whatever instant it's given,
        // so that instant must already be Eastern before it gets there.
        var nowEastern = EasternTimeZone.ConvertFromUtc(clock.UtcNow);
        var isDue = policy.IsDue(lastSuccessAt, nowEastern);

        DateTimeOffset? nextDueAt = lastSuccessAt is { } lastSuccess
            ? policy.ComputeNextDueAt(EasternTimeZone.ConvertFromUtc(lastSuccess))
            : null;

        return new FreshnessDecision(sourceId, category, isDue, clock.UtcNow, lastSuccessAt, nextDueAt);
    }
}
