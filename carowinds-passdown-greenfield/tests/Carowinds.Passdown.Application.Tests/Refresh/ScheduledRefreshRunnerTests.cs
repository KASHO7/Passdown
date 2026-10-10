using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Application.Tests.Refresh;

/// <summary>
/// Phase 6A Part D/L; Phase 13D Part Q: <see cref="ScheduledRefreshRunner"/> is a thin wrapper around
/// <see cref="IRefreshOrchestrator.RunDeepRefreshAsync"/> (called with freshness NOT bypassed) — it
/// must invoke exactly that one entry point, never the manual-date one, and classify the result
/// without inventing any new failure semantics beyond what <see cref="RefreshSummary"/> already
/// reports.
/// </summary>
public class ScheduledRefreshRunnerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);

    private static RefreshSummary Summary(int attempted, int succeeded, int failed, int cancelled) =>
        new(RefreshJobType.DeepRefresh, FixedNow, FixedNow.AddSeconds(5), attempted, succeeded, failed, cancelled, []);

    // 4. Scheduled runner invokes RunScheduledRefreshAsync exactly once.
    [Fact]
    public async Task RunAsync_InvokesScheduledRefreshExactlyOnce()
    {
        var orchestrator = new FakeRefreshOrchestrator();
        var runner = new ScheduledRefreshRunner(orchestrator, new FakeClock(FixedNow), NullLogger<ScheduledRefreshRunner>.Instance);

        await runner.RunAsync(RefreshMode.Scheduled);

        Assert.Single(orchestrator.DeepRefreshCalls);
    }

    // 5. Scheduled runner does not invoke manual-date refresh.
    [Fact]
    public async Task RunAsync_NeverInvokesManualDateRefresh()
    {
        var orchestrator = new FakeRefreshOrchestrator();
        var runner = new ScheduledRefreshRunner(orchestrator, new FakeClock(FixedNow), NullLogger<ScheduledRefreshRunner>.Instance);

        await runner.RunAsync(RefreshMode.Scheduled);

        Assert.Empty(orchestrator.ManualDateRefreshCalls);
    }

    // 8. Successful refresh returns success exit code (classification, not the process exit code itself).
    [Fact]
    public async Task RunAsync_NoFailures_ClassifiesAsSuccess()
    {
        var orchestrator = new FakeRefreshOrchestrator { DeepRefreshSummaryOverride = Summary(attempted: 3, succeeded: 3, failed: 0, cancelled: 0) };
        var runner = new ScheduledRefreshRunner(orchestrator, new FakeClock(FixedNow), NullLogger<ScheduledRefreshRunner>.Instance);

        var result = await runner.RunAsync(RefreshMode.Scheduled);

        Assert.Equal(ScheduledRefreshOutcome.Success, result.Outcome);
    }

    // 9. Partial source failure returns the documented partial-failure classification.
    [Fact]
    public async Task RunAsync_AtLeastOneFailure_ClassifiesAsPartialFailure()
    {
        var orchestrator = new FakeRefreshOrchestrator { DeepRefreshSummaryOverride = Summary(attempted: 3, succeeded: 2, failed: 1, cancelled: 0) };
        var runner = new ScheduledRefreshRunner(orchestrator, new FakeClock(FixedNow), NullLogger<ScheduledRefreshRunner>.Instance);

        var result = await runner.RunAsync(RefreshMode.Scheduled);

        Assert.Equal(ScheduledRefreshOutcome.PartialFailure, result.Outcome);
    }

    // 10. Unavailable/NotPublished/Closed results do not automatically count as fatal — they are
    // already folded into SourceOperationOutcomeKind.Success upstream (spec §5.2), so a summary
    // with zero Failed entries — regardless of how many individual sources returned a non-Verified
    // status — must classify as Success, never PartialFailure.
    [Fact]
    public async Task RunAsync_AllSuccessfulCompletionsIncludingNonVerifiedStatuses_ClassifiesAsSuccess()
    {
        var orchestrator = new FakeRefreshOrchestrator { DeepRefreshSummaryOverride = Summary(attempted: 4, succeeded: 4, failed: 0, cancelled: 0) };
        var runner = new ScheduledRefreshRunner(orchestrator, new FakeClock(FixedNow), NullLogger<ScheduledRefreshRunner>.Instance);

        var result = await runner.RunAsync(RefreshMode.Scheduled);

        Assert.Equal(ScheduledRefreshOutcome.Success, result.Outcome);
        Assert.Equal(4, result.Summary.Succeeded);
    }

    // Phase 6B Part D/E/N (#12): even when every planned operation failed (e.g. the machine is
    // fully offline, so nothing succeeds at all), this still classifies as PartialFailure, never a
    // fatal outcome — "fatal" is reserved for startup/database/unhandled failure (ScheduledRefreshHost),
    // not "every source happened to fail this run."
    [Fact]
    public async Task RunAsync_AllOperationsFail_StillClassifiesAsPartialFailure_NotFatal()
    {
        var orchestrator = new FakeRefreshOrchestrator { DeepRefreshSummaryOverride = Summary(attempted: 4, succeeded: 0, failed: 4, cancelled: 0) };
        var runner = new ScheduledRefreshRunner(orchestrator, new FakeClock(FixedNow), NullLogger<ScheduledRefreshRunner>.Instance);

        var result = await runner.RunAsync(RefreshMode.Scheduled);

        Assert.Equal(ScheduledRefreshOutcome.PartialFailure, result.Outcome);
    }

    // 15. Log summary contains success/failure counts.
    [Fact]
    public async Task RunAsync_LogsSuccessAndFailureCounts()
    {
        var orchestrator = new FakeRefreshOrchestrator { DeepRefreshSummaryOverride = Summary(attempted: 5, succeeded: 3, failed: 2, cancelled: 0) };
        var recordingLogger = new RecordingLogger();
        var runner = new ScheduledRefreshRunner(orchestrator, new FakeClock(FixedNow), recordingLogger);

        await runner.RunAsync(RefreshMode.Scheduled);

        Assert.Contains(recordingLogger.Messages, m => m.Contains("5") && m.Contains("3") && m.Contains("2") && m.Contains("summary", StringComparison.OrdinalIgnoreCase));
    }

    // Phase 13B Part A: RefreshMode.StartupCatchUp still resolves to the identical
    // RunScheduledRefreshAsync call — only the logged mode differs.
    [Fact]
    public async Task RunAsync_StartupCatchUpMode_StillInvokesScheduledRefreshExactlyOnce()
    {
        var orchestrator = new FakeRefreshOrchestrator();
        var runner = new ScheduledRefreshRunner(orchestrator, new FakeClock(FixedNow), NullLogger<ScheduledRefreshRunner>.Instance);

        await runner.RunAsync(RefreshMode.StartupCatchUp);

        Assert.Single(orchestrator.DeepRefreshCalls);
        Assert.Empty(orchestrator.ManualDateRefreshCalls);
    }

    // Phase 13B Part A: RefreshMode.UserForced never reaches this runner — it has its own entry
    // point (IRefreshOrchestrator.RunUserForcedRefreshAsync, called directly by AssociatePassdownService).
    [Fact]
    public async Task RunAsync_UserForcedMode_Throws()
    {
        var orchestrator = new FakeRefreshOrchestrator();
        var runner = new ScheduledRefreshRunner(orchestrator, new FakeClock(FixedNow), NullLogger<ScheduledRefreshRunner>.Instance);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => runner.RunAsync(RefreshMode.UserForced));
        Assert.Empty(orchestrator.DeepRefreshCalls);
    }

    // Phase 13B Part A: the log line records which mode triggered this run, so Scheduled and
    // StartupCatchUp invocations are distinguishable in the log even though they share one runner.
    [Fact]
    public async Task RunAsync_LogsWhichModeTriggeredTheRun()
    {
        var orchestrator = new FakeRefreshOrchestrator();
        var recordingLogger = new RecordingLogger();
        var runner = new ScheduledRefreshRunner(orchestrator, new FakeClock(FixedNow), recordingLogger);

        await runner.RunAsync(RefreshMode.StartupCatchUp);

        Assert.Contains(recordingLogger.Messages, m => m.Contains(nameof(RefreshMode.StartupCatchUp)));
    }

    // Phase 13D Part Q: scheduled/startup-catch-up maintenance must stay due-gated for every source
    // other than Park Hours — never bypass freshness, or every nightly run would redo the full sweep
    // unconditionally regardless of what already succeeded.
    [Fact]
    public async Task RunAsync_CallsRunDeepRefreshAsync_WithFreshnessNotBypassed()
    {
        var orchestrator = new FakeRefreshOrchestrator();
        var runner = new ScheduledRefreshRunner(orchestrator, new FakeClock(FixedNow), NullLogger<ScheduledRefreshRunner>.Instance);

        await runner.RunAsync(RefreshMode.Scheduled);

        var call = Assert.Single(orchestrator.DeepRefreshCalls);
        Assert.False(call.BypassFreshness);
        Assert.Null(call.FocusDate);
    }

    private sealed class RecordingLogger : Microsoft.Extensions.Logging.ILogger<ScheduledRefreshRunner>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
