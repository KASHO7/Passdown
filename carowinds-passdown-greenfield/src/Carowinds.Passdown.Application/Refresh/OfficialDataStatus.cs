using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Application.Refresh;

/// <summary>The aggregate, persisted state of the official sources (Phase 19A), independent of whether a check is running right now.</summary>
public enum OfficialDataCheckState
{
    /// <summary>No official source has ever been attempted on this computer (fresh install).</summary>
    NotCheckedYet,

    /// <summary>Every official source succeeded on its most recent attempt.</summary>
    Checked,

    /// <summary>At least one source succeeded and at least one failed (or was never checked) on its most recent attempt.</summary>
    PartiallyFailed,

    /// <summary>Every official source failed on its most recent attempt.</summary>
    Failed,
}

/// <summary>One official source whose most recent attempt failed or never happened. Category/id only — never URLs, payloads or exception text.</summary>
public sealed record OfficialSourceIssue(string SourceId, SourceCategory Category);

/// <summary>
/// Phase 19A: the app-wide official-data freshness the shared status indicator, the automatic
/// refresh policy and diagnostics all read — derived entirely from the persisted
/// <see cref="SourceStateRecord"/> rows the orchestrator already writes, so the scheduled task's
/// work, a manual Deep Refresh and a previous app session are all reflected without any extra
/// bookkeeping (and without a schema change). A date-specific Check Online never writes those rows,
/// so it can never make this claim every source was checked.
/// </summary>
/// <param name="LastCheckedAt">The most recent attempt of any official source.</param>
/// <param name="LastSuccessfulCheckAt">When <see cref="State"/> is <see cref="OfficialDataCheckState.Checked"/>: the oldest of every source's latest success, i.e. every source has been verified at least since then. Otherwise null.</param>
/// <param name="LastComprehensiveCheckAt">The oldest of every source's latest attempt — every source has been attempted at least since then (null until every source has been attempted once). The automatic four-hour clock runs from here.</param>
/// <param name="IsChecking">True while a comprehensive check is running inside this app.</param>
public sealed record OfficialDataStatus(
    OfficialDataCheckState State,
    DateTimeOffset? LastCheckedAt,
    DateTimeOffset? LastSuccessfulCheckAt,
    DateTimeOffset? LastComprehensiveCheckAt,
    IReadOnlyList<OfficialSourceIssue> FailedSources,
    bool IsChecking = false)
{
    public static OfficialDataStatus NotCheckedYet { get; } = new(OfficialDataCheckState.NotCheckedYet, null, null, null, []);

    /// <summary>Value comparison including <see cref="FailedSources"/>' contents (record equality compares that list by reference).</summary>
    public bool HasSameContentAs(OfficialDataStatus other) =>
        other is not null
        && State == other.State
        && LastCheckedAt == other.LastCheckedAt
        && LastSuccessfulCheckAt == other.LastSuccessfulCheckAt
        && LastComprehensiveCheckAt == other.LastComprehensiveCheckAt
        && IsChecking == other.IsChecking
        && FailedSources.SequenceEqual(other.FailedSources);

    /// <summary>When the next automatic comprehensive check is expected (null = due as soon as possible).</summary>
    public DateTimeOffset? NextAutomaticCheckAt(DateTimeOffset nowUtc) => OfficialRefreshPolicy.NextAutomaticCheckAt(this, nowUtc);

    /// <summary>Pure aggregation over one (registration, persisted state) pair per registered source.</summary>
    public static OfficialDataStatus Compute(IReadOnlyList<(SourceRegistration Registration, SourceStateRecord? State)> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var attempted = sources.Where(s => s.State?.LastAttemptAt is not null).ToList();
        if (sources.Count == 0 || attempted.Count == 0)
        {
            return NotCheckedYet;
        }

        var issues = sources
            .Where(s => s.State?.LastAttemptAt is null || s.State.ConsecutiveFailureCount > 0 || s.State.LastSuccessAt is null)
            .Select(s => new OfficialSourceIssue(s.Registration.SourceId, s.Registration.Category))
            .ToList();

        var state = issues.Count == 0
            ? OfficialDataCheckState.Checked
            : issues.Count == sources.Count ? OfficialDataCheckState.Failed : OfficialDataCheckState.PartiallyFailed;

        var lastCheckedAt = attempted.Max(s => s.State!.LastAttemptAt!.Value);
        DateTimeOffset? lastComprehensive = attempted.Count == sources.Count ? attempted.Min(s => s.State!.LastAttemptAt!.Value) : null;
        DateTimeOffset? lastSuccessful = state == OfficialDataCheckState.Checked ? sources.Min(s => s.State!.LastSuccessAt!.Value) : null;

        return new OfficialDataStatus(state, lastCheckedAt, lastSuccessful, lastComprehensive, issues);
    }

    /// <summary>Reads every registered source's persisted state and aggregates it — a handful of local reads, never a network call.</summary>
    public static async Task<OfficialDataStatus> ReadAsync(
        IReadOnlyList<SourceRegistration> registrations,
        ISourceStateRepository sourceStateRepository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(sourceStateRepository);

        var sources = new List<(SourceRegistration, SourceStateRecord?)>(registrations.Count);
        foreach (var registration in registrations)
        {
            var state = await sourceStateRepository.GetAsync(registration.SourceId, cancellationToken).ConfigureAwait(false);
            sources.Add((registration, state));
        }

        return Compute(sources);
    }
}
