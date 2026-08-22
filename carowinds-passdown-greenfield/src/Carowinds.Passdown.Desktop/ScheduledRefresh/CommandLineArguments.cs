namespace Carowinds.Passdown.Desktop.ScheduledRefresh;

/// <summary>Which of the two supported startup paths (Phase 6A Part A) a parsed command line selects.</summary>
public enum StartupMode
{
    /// <summary>No recognized argument: open the desktop UI exactly as today.</summary>
    Normal,

    /// <summary><c>--scheduled-refresh</c>: run the headless unattended refresh and exit.</summary>
    ScheduledRefresh,

    /// <summary>An unrecognized argument was supplied — never silently falls back to <see cref="Normal"/> or <see cref="ScheduledRefresh"/>.</summary>
    Invalid,
}

/// <summary>
/// The smallest clean command-line parser needed to distinguish normal desktop launch from
/// unattended scheduled-refresh launch (Phase 6A Part A). Deliberately not a CLI framework — one
/// recognized flag, everything else is <see cref="StartupMode.Invalid"/>.
/// </summary>
public sealed record CommandLineArguments(StartupMode Mode, string? UsageError = null)
{
    public const string ScheduledRefreshFlag = "--scheduled-refresh";

    public static CommandLineArguments Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new CommandLineArguments(StartupMode.Normal);
        }

        if (args.Length == 1 && args[0] == ScheduledRefreshFlag)
        {
            return new CommandLineArguments(StartupMode.ScheduledRefresh);
        }

        return new CommandLineArguments(StartupMode.Invalid, $"Unrecognized command-line arguments: {string.Join(' ', args)}");
    }
}
