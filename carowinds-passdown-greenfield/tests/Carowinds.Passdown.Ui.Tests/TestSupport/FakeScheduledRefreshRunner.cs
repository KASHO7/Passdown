using Carowinds.Passdown.Application.Refresh;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Scriptable <see cref="IScheduledRefreshRunner"/> for <c>ScheduledRefreshHost</c> tests — never touches a real orchestrator, database, or network.</summary>
internal sealed class FakeScheduledRefreshRunner : IScheduledRefreshRunner
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);

    public int CallCount { get; private set; }

    public Func<CancellationToken, Task<ScheduledRefreshResult>>? OnRun { get; set; }

    public ScheduledRefreshResult ResultToReturn { get; set; } = EmptySuccess();

    public Task<ScheduledRefreshResult> RunAsync(CancellationToken cancellationToken = default)
    {
        CallCount++;
        return OnRun is not null ? OnRun(cancellationToken) : Task.FromResult(ResultToReturn);
    }

    public static ScheduledRefreshResult EmptySuccess() => new(
        ScheduledRefreshOutcome.Success,
        new RefreshSummary(RefreshJobType.ScheduledRollingWindow, FixedNow, FixedNow, 0, 0, 0, 0, []));

    public static ScheduledRefreshResult PartialFailure() => new(
        ScheduledRefreshOutcome.PartialFailure,
        new RefreshSummary(RefreshJobType.ScheduledRollingWindow, FixedNow, FixedNow, 3, 2, 1, 0, []));
}
