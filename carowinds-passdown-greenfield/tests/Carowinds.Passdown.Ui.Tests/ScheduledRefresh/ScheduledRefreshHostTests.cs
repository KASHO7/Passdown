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
}
