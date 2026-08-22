using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Desktop.ScheduledRefresh;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Carowinds.Passdown.Ui.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Ui.Tests.ScheduledRefresh;

/// <summary>
/// Phase 6A Part K/L: exercises <see cref="ScheduledRefreshHost"/> without a real child process, a
/// real network call, or the real per-user AppPaths database — every test below builds its own
/// small, disposable service collection (an isolated temp-file SQLite database where a database is
/// needed at all) rather than resolving the full <c>App.BuildServiceProvider()</c> graph, so these
/// stay fast and offline.
/// </summary>
public class ScheduledRefreshHostTests
{
    private static IServiceProvider BuildMinimalProvider(IScheduledRefreshRunner runner, string? databasePath = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        if (databasePath is not null)
        {
            services.AddSingleton(new SqliteConnectionFactory(databasePath));
            services.AddSingleton<IReadOnlyList<IMigration>>([new Migration0001CreateSchemaVersionTable()]);
            services.AddSingleton<MigrationRunner>();
        }

        services.AddSingleton(runner);

        return services.BuildServiceProvider();
    }

    // 8. Successful refresh returns success exit code.
    [Fact]
    public async Task RunAsync_SuccessfulRefresh_ReturnsSuccessExitCode()
    {
        var runner = new FakeScheduledRefreshRunner { ResultToReturn = FakeScheduledRefreshRunner.EmptySuccess() };
        var tempDb = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-6a-{Guid.NewGuid():N}.db");
        try
        {
            var host = new ScheduledRefreshHost(() => BuildMinimalProvider(runner, tempDb));

            var exitCode = await host.RunAsync();

            Assert.Equal(ScheduledRefreshExitCode.Success, exitCode);
            Assert.Equal(1, runner.CallCount);
        }
        finally
        {
            File.Delete(tempDb);
        }
    }

    // 9. Partial source failure returns the documented partial-failure exit code.
    [Fact]
    public async Task RunAsync_PartialFailureRefresh_ReturnsPartialFailureExitCode()
    {
        var runner = new FakeScheduledRefreshRunner { ResultToReturn = FakeScheduledRefreshRunner.PartialFailure() };
        var tempDb = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-6a-{Guid.NewGuid():N}.db");
        try
        {
            var host = new ScheduledRefreshHost(() => BuildMinimalProvider(runner, tempDb));

            var exitCode = await host.RunAsync();

            Assert.Equal(ScheduledRefreshExitCode.PartialFailure, exitCode);
        }
        finally
        {
            File.Delete(tempDb);
        }
    }

    // 13. Migration/database initialization happens before refresh.
    [Fact]
    public async Task RunAsync_RunsMigrationsBeforeInvokingRunner_AgainstAnIsolatedTempDatabase()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-6a-{Guid.NewGuid():N}.db");
        try
        {
            var migratedBeforeRunnerInvoked = false;
            var runner = new FakeScheduledRefreshRunner
            {
                OnRun = _ =>
                {
                    using var connection = new SqliteConnection($"Data Source={tempDb}");
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT COUNT(*) FROM schema_version;";
                    migratedBeforeRunnerInvoked = Convert.ToInt32(command.ExecuteScalar()) > 0;
                    return Task.FromResult(FakeScheduledRefreshRunner.EmptySuccess());
                },
            };
            var host = new ScheduledRefreshHost(() => BuildMinimalProvider(runner, tempDb));

            var exitCode = await host.RunAsync();

            Assert.Equal(ScheduledRefreshExitCode.Success, exitCode);
            Assert.True(migratedBeforeRunnerInvoked, "Expected the schema_version table to already be populated by the time the runner ran.");
        }
        finally
        {
            File.Delete(tempDb);
        }
    }

    // 11. Fatal startup exception returns fatal exit code (service-provider construction itself fails).
    [Fact]
    public async Task RunAsync_ServiceProviderFactoryThrows_ReturnsFatalFailureExitCode()
    {
        var host = new ScheduledRefreshHost(() => throw new InvalidOperationException("simulated configuration failure"));

        var exitCode = await host.RunAsync();

        Assert.Equal(ScheduledRefreshExitCode.FatalFailure, exitCode);
    }

    // Fatal failure when the database/migration dependency itself is missing from composition
    // (simulates a broken registration without needing a real corrupt database).
    [Fact]
    public async Task RunAsync_MigrationRunnerNotRegistered_ReturnsFatalFailureExitCode()
    {
        var runner = new FakeScheduledRefreshRunner();
        var host = new ScheduledRefreshHost(() => BuildMinimalProvider(runner, databasePath: null));

        var exitCode = await host.RunAsync();

        Assert.Equal(ScheduledRefreshExitCode.FatalFailure, exitCode);
        Assert.Equal(0, runner.CallCount); // never reached the runner
    }

    // The runner throwing unexpectedly after successful initialization is also fatal.
    [Fact]
    public async Task RunAsync_RunnerThrowsUnexpectedly_ReturnsFatalFailureExitCode()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-6a-{Guid.NewGuid():N}.db");
        try
        {
            var runner = new FakeScheduledRefreshRunner { OnRun = _ => throw new InvalidOperationException("simulated unexpected failure") };
            var host = new ScheduledRefreshHost(() => BuildMinimalProvider(runner, tempDb));

            var exitCode = await host.RunAsync();

            Assert.Equal(ScheduledRefreshExitCode.FatalFailure, exitCode);
        }
        finally
        {
            File.Delete(tempDb);
        }
    }

    // 18. Cancellation is handled deterministically: always the same exit code, never a hang, never
    // an unhandled exception out of RunAsync.
    [Fact]
    public async Task RunAsync_Cancelled_ReturnsFatalFailureExitCode_Deterministically()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-6a-{Guid.NewGuid():N}.db");
        try
        {
            var runner = new FakeScheduledRefreshRunner { OnRun = ct => Task.FromCanceled<ScheduledRefreshResult>(ct) };
            var host = new ScheduledRefreshHost(() => BuildMinimalProvider(runner, tempDb));
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var exitCode = await host.RunAsync(cts.Token);

            Assert.Equal(ScheduledRefreshExitCode.FatalFailure, exitCode);
        }
        finally
        {
            File.Delete(tempDb);
        }
    }

    // 6/7. Scheduled mode never resolves MainWindow/AssociateViewModel: this container never
    // registers either type, so if ScheduledRefreshHost's code ever tried to resolve one, the
    // resulting DI exception would surface as FatalFailure below instead of the expected Success.
    [Fact]
    public async Task RunAsync_NeverResolvesUiTypes()
    {
        var runner = new FakeScheduledRefreshRunner();
        var tempDb = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-6a-{Guid.NewGuid():N}.db");
        try
        {
            var host = new ScheduledRefreshHost(() => BuildMinimalProvider(runner, tempDb));

            var exitCode = await host.RunAsync();

            Assert.Equal(ScheduledRefreshExitCode.Success, exitCode);
        }
        finally
        {
            File.Delete(tempDb);
        }
    }

    // Fatal failure when the database itself cannot be opened/initialized (Phase 6B Part G): here,
    // the containing directory can never be created because a plain file already occupies that
    // path segment, so SqliteConnectionFactory.OpenConnection's Directory.CreateDirectory throws
    // before a single row is ever touched.
    [Fact]
    public async Task RunAsync_DatabaseDirectoryCannotBeCreated_ReturnsFatalFailureExitCode_AndNeverInvokesRunner()
    {
        var blockingFile = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-6b-blocker-{Guid.NewGuid():N}");
        File.WriteAllText(blockingFile, "not a directory");
        try
        {
            var runner = new FakeScheduledRefreshRunner();
            var tempDb = Path.Combine(blockingFile, "sub", "carowinds.db"); // blockingFile can never be a directory
            var host = new ScheduledRefreshHost(() => BuildMinimalProvider(runner, tempDb));

            var exitCode = await host.RunAsync();

            Assert.Equal(ScheduledRefreshExitCode.FatalFailure, exitCode);
            Assert.Equal(0, runner.CallCount);
        }
        finally
        {
            File.Delete(blockingFile);
        }
    }

    // Phase 7D Part F: a database file that exists but is not a valid SQLite database (a stray
    // text file, disk corruption, a previous crash's partial write) must be classified as a fatal
    // startup failure — the existing FatalFailure exit code — never silently deleted, overwritten,
    // or replaced with a fresh blank database, and never mistaken for a source/partial failure.
    [Fact]
    public async Task RunAsync_DatabaseFileIsNotValidSqlite_ReturnsFatalFailureExitCode_AndLeavesFileUnchanged()
    {
        var runner = new FakeScheduledRefreshRunner();
        var tempDb = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-7d-corrupt-{Guid.NewGuid():N}.db");
        var originalBytes = "not a sqlite database"u8.ToArray();
        File.WriteAllBytes(tempDb, originalBytes);
        try
        {
            var host = new ScheduledRefreshHost(() => BuildMinimalProvider(runner, tempDb));

            var exitCode = await host.RunAsync();

            Assert.Equal(ScheduledRefreshExitCode.FatalFailure, exitCode);
            Assert.Equal(0, runner.CallCount);
            SqliteConnection.ClearAllPools();
            Assert.Equal(originalBytes, File.ReadAllBytes(tempDb));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(tempDb);
        }
    }

    // Phase 6B Part A/N (#2/#3/#21): a second overlapping invocation detects the active run,
    // never invokes the refresh runner, is classified as a distinct nonfatal outcome (not a source
    // failure), and logs an overlap line.
    [Fact]
    public async Task RunAsync_LockUnavailable_ReturnsOverlapExitCode_AndNeverInvokesRunner()
    {
        var runner = new FakeScheduledRefreshRunner();
        var scheduledLock = new FakeScheduledRefreshLock { AcquireSucceeds = false };
        var tempDb = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-6b-{Guid.NewGuid():N}.db");
        try
        {
            var host = new ScheduledRefreshHost(() => BuildMinimalProvider(runner, tempDb), () => scheduledLock);

            var exitCode = await host.RunAsync();

            Assert.Equal(ScheduledRefreshExitCode.Overlap, exitCode);
            Assert.Equal(0, runner.CallCount);
        }
        finally
        {
            File.Delete(tempDb);
        }
    }

    // Lock acquired and released on a normal successful completion.
    [Fact]
    public async Task RunAsync_LockAvailable_AcquiresRunsAndReleasesLock()
    {
        var runner = new FakeScheduledRefreshRunner { ResultToReturn = FakeScheduledRefreshRunner.EmptySuccess() };
        var scheduledLock = new FakeScheduledRefreshLock();
        var tempDb = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-6b-{Guid.NewGuid():N}.db");
        try
        {
            var host = new ScheduledRefreshHost(() => BuildMinimalProvider(runner, tempDb), () => scheduledLock);

            var exitCode = await host.RunAsync();

            Assert.Equal(ScheduledRefreshExitCode.Success, exitCode);
            Assert.Equal(1, runner.CallCount);
            Assert.Equal(1, scheduledLock.TryAcquireCallCount);
            Assert.Equal(1, scheduledLock.DisposeCallCount);
        }
        finally
        {
            File.Delete(tempDb);
        }
    }

    // Lock is still released when the runner throws unexpectedly (Part A "released after
    // exception/process cleanup as safely as the platform permits").
    [Fact]
    public async Task RunAsync_RunnerThrows_StillReleasesLock()
    {
        var runner = new FakeScheduledRefreshRunner { OnRun = _ => throw new InvalidOperationException("simulated failure") };
        var scheduledLock = new FakeScheduledRefreshLock();
        var tempDb = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-6b-{Guid.NewGuid():N}.db");
        try
        {
            var host = new ScheduledRefreshHost(() => BuildMinimalProvider(runner, tempDb), () => scheduledLock);

            var exitCode = await host.RunAsync();

            Assert.Equal(ScheduledRefreshExitCode.FatalFailure, exitCode);
            Assert.Equal(1, scheduledLock.DisposeCallCount);
        }
        finally
        {
            File.Delete(tempDb);
        }
    }

    // Part D/G: even when every source operation failed (e.g. the machine has no usable network at
    // all), the process must still report PartialFailure — not FatalFailure — because local
    // infrastructure (migrations, the database) started up healthily; "fatal" is reserved for a
    // genuine startup/database/unhandled failure.
    [Fact]
    public async Task RunAsync_EveryOperationFailed_StillReturnsPartialFailureExitCode_NotFatal()
    {
        var allFailed = new ScheduledRefreshResult(
            ScheduledRefreshOutcome.PartialFailure,
            new RefreshSummary(RefreshJobType.ScheduledRollingWindow, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, OperationsAttempted: 4, Succeeded: 0, Failed: 4, Cancelled: 0, Results: []));
        var runner = new FakeScheduledRefreshRunner { ResultToReturn = allFailed };
        var tempDb = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-6b-{Guid.NewGuid():N}.db");
        try
        {
            var host = new ScheduledRefreshHost(() => BuildMinimalProvider(runner, tempDb));

            var exitCode = await host.RunAsync();

            Assert.Equal(ScheduledRefreshExitCode.PartialFailure, exitCode);
        }
        finally
        {
            File.Delete(tempDb);
        }
    }

    // A lock unavailable never even reaches migrations, so it must never be treated as a fatal DB
    // failure either — Overlap is its own outcome, distinct from both Success/PartialFailure and
    // FatalFailure.
    [Fact]
    public async Task RunAsync_LockUnavailable_NeverRunsMigrations()
    {
        var runner = new FakeScheduledRefreshRunner();
        var scheduledLock = new FakeScheduledRefreshLock { AcquireSucceeds = false };
        // No database path at all: if migrations ran, resolving MigrationRunner would throw (it
        // isn't registered), surfacing as FatalFailure instead of the expected Overlap.
        var host = new ScheduledRefreshHost(() => BuildMinimalProvider(runner, databasePath: null), () => scheduledLock);

        var exitCode = await host.RunAsync();

        Assert.Equal(ScheduledRefreshExitCode.Overlap, exitCode);
    }
}
