using Carowinds.Passdown.Application.Refresh;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

/// <summary>
/// Records every call so a test can assert whether a refresh happened at all (spec §6.3's "date
/// change never triggers retrieval"), and lets a test simulate what a real refresh would have
/// written via <see cref="OnManualDateRefresh"/> — e.g. seeding a new dated-result record — without
/// needing the real orchestrator, source adapters, or any network access.
/// </summary>
internal sealed class FakeRefreshOrchestrator : IRefreshOrchestrator
{
    public int ScheduledRefreshCallCount { get; private set; }

    public List<DateOnly> ManualDateRefreshCalls { get; } = [];

    public Func<DateOnly, CancellationToken, Task>? OnManualDateRefresh { get; set; }

    /// <summary>When set, returned by <see cref="RunScheduledRefreshAsync"/> instead of an empty summary — lets a test script a specific success/failure/cancelled mix.</summary>
    public RefreshSummary? ScheduledRefreshSummaryOverride { get; set; }

    public Task<RefreshSummary> RunScheduledRefreshAsync(IProgress<RefreshProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ScheduledRefreshCallCount++;
        return Task.FromResult(ScheduledRefreshSummaryOverride ?? EmptySummary(RefreshJobType.ScheduledRollingWindow));
    }

    public async Task<RefreshSummary> RunManualDateRefreshAsync(DateOnly date, IProgress<RefreshProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ManualDateRefreshCalls.Add(date);

        if (OnManualDateRefresh is not null)
        {
            await OnManualDateRefresh(date, cancellationToken).ConfigureAwait(false);
        }

        return EmptySummary(RefreshJobType.ManualSingleDate);
    }

    private static RefreshSummary EmptySummary(RefreshJobType jobType) =>
        new(jobType, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, OperationsAttempted: 0, Succeeded: 0, Failed: 0, Cancelled: 0, Results: []);
}
