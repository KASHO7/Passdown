using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Desktop.Startup;
using Carowinds.Passdown.Ui.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Ui.Tests.Startup;

/// <summary>
/// Phase 6B Part C: the missed-run-on-launch catch-up trigger, exercised directly (not through
/// Avalonia startup) so these stay fast and don't need a real window/dispatcher.
/// </summary>
public class StartupCatchUpRefreshTests
{
    private static IServiceProvider BuildProvider(IScheduledRefreshRunner runner)
    {
        var services = new ServiceCollection();
        services.AddSingleton(runner);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task RunAsync_LockAvailable_InvokesRunnerExactlyOnce()
    {
        var runner = new FakeScheduledRefreshRunner();
        var scheduledLock = new FakeScheduledRefreshLock();

        await StartupCatchUpRefresh.RunAsync(BuildProvider(runner), scheduledLock, NullLogger.Instance);

        Assert.Equal(1, runner.CallCount);
    }

    [Fact]
    public async Task RunAsync_LockUnavailable_NeverInvokesRunner()
    {
        var runner = new FakeScheduledRefreshRunner();
        var scheduledLock = new FakeScheduledRefreshLock { AcquireSucceeds = false };

        await StartupCatchUpRefresh.RunAsync(BuildProvider(runner), scheduledLock, NullLogger.Instance);

        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task RunAsync_LockAvailable_ReleasesLockAfterCompletion()
    {
        var runner = new FakeScheduledRefreshRunner();
        var scheduledLock = new FakeScheduledRefreshLock();

        await StartupCatchUpRefresh.RunAsync(BuildProvider(runner), scheduledLock, NullLogger.Instance);

        Assert.Equal(1, scheduledLock.DisposeCallCount);
    }

    [Fact]
    public async Task RunAsync_LockUnavailable_NeverDisposesALockItNeverAcquired()
    {
        var runner = new FakeScheduledRefreshRunner();
        var scheduledLock = new FakeScheduledRefreshLock { AcquireSucceeds = false };

        await StartupCatchUpRefresh.RunAsync(BuildProvider(runner), scheduledLock, NullLogger.Instance);

        Assert.Equal(0, scheduledLock.DisposeCallCount);
    }

    // Failures here must never propagate to the caller (Part C "failures should not prevent the
    // UI from opening") — the already-open window and cached data are unaffected either way.
    [Fact]
    public async Task RunAsync_RunnerThrows_DoesNotPropagate_AndStillReleasesLock()
    {
        var runner = new FakeScheduledRefreshRunner { OnRun = _ => throw new InvalidOperationException("simulated failure") };
        var scheduledLock = new FakeScheduledRefreshLock();

        var exception = await Record.ExceptionAsync(() =>
            StartupCatchUpRefresh.RunAsync(BuildProvider(runner), scheduledLock, NullLogger.Instance));

        Assert.Null(exception);
        Assert.Equal(1, scheduledLock.DisposeCallCount);
    }

    [Fact]
    public async Task RunAsync_ResolverThrows_DoesNotPropagate()
    {
        var services = new ServiceCollection().BuildServiceProvider(); // IScheduledRefreshRunner never registered
        var scheduledLock = new FakeScheduledRefreshLock();

        var exception = await Record.ExceptionAsync(() =>
            StartupCatchUpRefresh.RunAsync(services, scheduledLock, NullLogger.Instance));

        Assert.Null(exception);
    }
}
