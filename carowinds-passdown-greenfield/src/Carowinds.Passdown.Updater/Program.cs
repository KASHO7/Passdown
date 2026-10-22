using Carowinds.Passdown.Infrastructure.Updates;
using Carowinds.Passdown.Infrastructure.Updates.Installation;

namespace Carowinds.Passdown.Updater;

/// <summary>
/// Phase 23D: entry point of Carowinds.Passdown.Updater.exe. Accepts exactly
/// <c>--request &lt;path to requests\install-&lt;id&gt;.json&gt;</c> and nothing else; every decision after that is
/// made by <see cref="UpdateInstaller"/> from the validated request. It has no window: the user sees the app
/// close and the new version open (or the previous version reopen if the update had to be rolled back).
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        if (args is not [UpdaterProcessLauncher.RequestArgument, var requestPath] || string.IsNullOrWhiteSpace(requestPath))
        {
            return (int)UpdaterExitCode.InvalidUsage;
        }

        try
        {
            var installer = new UpdateInstaller(UpdatePaths.Default, new SystemUpdaterEnvironment(), AppContext.BaseDirectory, SqliteUpdateDatabaseGuard.Default);
            return (int)installer.RunAsync(requestPath).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Unexpected failure before or between journaled steps: the record (if any) still describes the last
            // completed step, and nothing reports success.
            return (int)UpdaterExitCode.NotInstalled;
        }
    }
}
