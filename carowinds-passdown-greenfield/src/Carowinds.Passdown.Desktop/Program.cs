using Avalonia;
using System;
using Carowinds.Passdown.Desktop.ScheduledRefresh;
using Carowinds.Passdown.Infrastructure.Concurrency;
using Carowinds.Passdown.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        var parsedArgs = CommandLineArguments.Parse(args);

        switch (parsedArgs.Mode)
        {
            case StartupMode.Invalid:
                Console.Error.WriteLine(parsedArgs.UsageError);
                Console.Error.WriteLine($"Usage: {AppContext.BaseDirectory}Carowinds.Passdown.Desktop[.exe] [{CommandLineArguments.ScheduledRefreshFlag}]");
                return (int)ScheduledRefreshExitCode.InvalidUsage;

            case StartupMode.ScheduledRefresh:
                return (int)RunScheduledRefresh();

            default:
                // Part I: no Avalonia initialization happens for the headless path above — this is
                // the only branch that ever starts the Avalonia runtime.
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
                return 0;
        }
    }

    private static ScheduledRefreshExitCode RunScheduledRefresh()
    {
        // Cooperative cancellation for headless/console execution (Phase 6B Part I): Ctrl+C stops
        // outstanding retrieval work via the same CancellationToken RunAsync already threads
        // through the orchestrator, rather than killing the process mid-write. e.Cancel = true
        // suppresses the runtime's default immediate-terminate behavior so this path gets to run.
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var host = new ScheduledRefreshHost(BuildScheduledRefreshServiceProvider, () => new FileScheduledRefreshLock(FileScheduledRefreshLock.DefaultPath));
        return host.RunAsync(cts.Token).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Reuses the exact composition root the desktop UI resolves <c>MainWindow</c>/<c>MainViewModel</c>
    /// from (<see cref="App.BuildServiceProvider"/>, Part B) and adds the one thing unattended
    /// execution needs that an interactive console session doesn't: a persistent file log (Part G),
    /// since the scheduled task runs with no terminal visible to read console output from.
    /// </summary>
    private static IServiceProvider BuildScheduledRefreshServiceProvider()
    {
        var services = App.BuildServiceProvider();
        services.GetRequiredService<ILoggerFactory>().AddProvider(new FileLoggerProvider(new FileLoggerOptions()));
        return services;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
