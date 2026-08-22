using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Application.Tests.Refresh;

/// <summary>
/// Phase 6A Part D/L: <see cref="ScheduledRefreshRunner"/> is a thin wrapper around
/// <see cref="IRefreshOrchestrator.RunScheduledRefreshAsync"/> — it must invoke exactly that one
/// entry point, never the manual-date one, and classify the result without inventing any new
/// failure semantics beyond what <see cref="RefreshSummary"/> already reports.
/// </summary>
public class ScheduledRefreshRunnerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);

    private static RefreshSummary Summary(int attempted, int succeeded, int failed, int cancelled) =>
        new(RefreshJobType.ScheduledRollingWindow, FixedNow, FixedNow.AddSeconds(5), attempted, succeeded, failed, cancelled, []);

    // 4. Scheduled runner invokes RunScheduledRefreshAsync exactly once.
    [Fact]
    public async Task RunAsync_InvokesScheduledRefreshExactlyOnce()
    {
        var orchestrator = new FakeRefreshOrchestrator();
        var runner = new ScheduledRefreshRunner(orchestrator, new FakeClock(FixedNow), NullLogger<ScheduledRefreshRunner>.Instance);

        await runner.RunAsync();

        Assert.Equal(1, orchestrator.ScheduledRefreshCallCount);
    }

    // 5. Scheduled runner does not invoke manual-date refresh.
    [Fact]
    public async Task RunAsync_NeverInvokesManualDateRefresh()
    {
        var orchestrator = new FakeRefreshOrchestrator();
        var runner = new ScheduledRefreshRunner(orchestrator, new FakeClock(FixedNow), NullLogger<ScheduledRefreshRunner>.Instance);

        await runner.RunAsync();

        Assert.Empty(orchestrator.ManualDateRefreshCalls);
    }

    // 8. Successful refresh returns success exit code (classification, not the process exit code itself).
    [Fact]
    public async Task RunAsync_NoFailures_ClassifiesAsSuccess()
    {
        var orchestrator = new FakeRefreshOrchestrator { ScheduledRefreshSummaryOverride = Summary(attempted: 3, succeeded: 3, failed: 0, cancelled: 0) };
        var runner = new ScheduledRefreshRunner(orchestrator, new FakeClock(FixedNow), NullLogger<ScheduledRefreshRunner>.Instance);

        var result = await runner.RunAsync();

        Assert.Equal(ScheduledRefreshOutcome.Success, result.Outcome);
    }

    // 9. Partial source failure returns the documented partial-failure classification.
    [Fact]
    public async Task RunAsync_AtLeastOneFailure_ClassifiesAsPartialFailure()
    {
        var orchestrator = new FakeRefreshOrchestrator { ScheduledRefreshSummaryOverride = Summary(attempted: 3, succeeded: 2, failed: 1, cancelled: 0) };
        var runner = new ScheduledRefreshRunner(orchestrator, new FakeClock(FixedNow), NullLogger<ScheduledRefreshRunner>.Instance);

        var result = await runner.RunAsync();

        Assert.Equal(ScheduledRefreshOutcome.PartialFailure, result.Outcome);
    }

    // 10. Unavailable/NotPublished/Closed results do not automatically count as fatal — they are
    // already folded into SourceOperationOutcomeKind.Success upstream (spec §5.2), so a summary
    // with zero Failed entries — regardless of how many individual sources returned a non-Verified
    // status — must classify as Success, never PartialFailure.
    [Fact]
    public async Task RunAsync_AllSuccessfulCompletionsIncludingNonVerifiedStatuses_ClassifiesAsSuccess()
    {
        var orchestrator = new FakeRefreshOrchestrator { ScheduledRefreshSummaryOverride = Summary(attempted: 4, succeeded: 4, failed: 0, cancelled: 0) };
        var runner = new ScheduledRefreshRunner(orchestrator, new FakeClock(FixedNow), NullLogger<ScheduledRefreshRunner>.Instance);

        var result = await runner.RunAsync();

        Assert.Equal(ScheduledRefreshOutcome.Success, result.Outcome);
        Assert.Equal(4, result.Summary.Succeeded);
    }

    // 15. Log summary contains success/failure counts.
    [Fact]
    public async Task RunAsync_LogsSuccessAndFailureCounts()
    {
        var orchestrator = new FakeRefreshOrchestrator { ScheduledRefreshSummaryOverride = Summary(attempted: 5, succeeded: 3, failed: 2, cancelled: 0) };
        var recordingLogger = new RecordingLogger();
        var runner = new ScheduledRefreshRunner(orchestrator, new FakeClock(FixedNow), recordingLogger);

        await runner.RunAsync();

        Assert.Contains(recordingLogger.Messages, m => m.Contains("5") && m.Contains("3") && m.Contains("2") && m.Contains("summary", StringComparison.OrdinalIgnoreCase));
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
