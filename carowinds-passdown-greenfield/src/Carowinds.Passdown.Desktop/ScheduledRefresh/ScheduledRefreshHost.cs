using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Desktop.ScheduledRefresh;

/// <summary>
/// The headless host for <c>--scheduled-refresh</c> (Phase 6A Parts C/D/E/H): builds the same
/// composition root the desktop UI uses, initializes the database, invokes
/// <see cref="IScheduledRefreshRunner"/>, and classifies the outcome into a process exit code.
/// Never resolves <c>MainWindow</c>/<c>MainViewModel</c>/<c>AssociateViewModel</c> — those types are
/// registered in the shared composition root but this class deliberately never asks for them
/// (Part H).
/// </summary>
public sealed class ScheduledRefreshHost(Func<IServiceProvider> serviceProviderFactory)
{
    public async Task<ScheduledRefreshExitCode> RunAsync(CancellationToken cancellationToken = default)
    {
        IServiceProvider services;
        ILogger logger;
        try
        {
            services = serviceProviderFactory();
            logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("ScheduledRefresh");
        }
        catch (Exception ex)
        {
            // No logger is guaranteed to exist yet at this point (building the provider is what
            // would supply one), so this one failure mode is console-only.
            Console.Error.WriteLine($"Fatal: failed to initialize application services: {ex}");
            return ScheduledRefreshExitCode.FatalFailure;
        }

        try
        {
            // Storage must be ready before anything touches the database (spec §17 Phase 1; mirrors
            // App.OnFrameworkInitializationCompleted's normal-mode ordering, Part C).
            services.GetRequiredService<MigrationRunner>().Run();

            var runner = services.GetRequiredService<IScheduledRefreshRunner>();
            var result = await runner.RunAsync(cancellationToken).ConfigureAwait(false);

            return result.Outcome switch
            {
                ScheduledRefreshOutcome.Success => ScheduledRefreshExitCode.Success,
                ScheduledRefreshOutcome.PartialFailure => ScheduledRefreshExitCode.PartialFailure,
                _ => ScheduledRefreshExitCode.FatalFailure,
            };
        }
        catch (OperationCanceledException)
        {
            // Deterministic regardless of when cancellation lands: the job never produced a summary,
            // so it is reported the same way any other incomplete run is (Part L "cancellation is
            // handled deterministically"). Single-instance/overlap handling remains a Phase 6B
            // concern (Part J).
            logger.LogWarning("Scheduled refresh was cancelled before completion.");
            return ScheduledRefreshExitCode.FatalFailure;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Fatal: scheduled refresh failed unexpectedly.");
            return ScheduledRefreshExitCode.FatalFailure;
        }
        finally
        {
            (services as IDisposable)?.Dispose();
        }
    }
}
