namespace Carowinds.Passdown.Infrastructure.Logging;

/// <summary>Tunables for <see cref="FileLoggerProvider"/> (Phase 6A Part G; Phase 18B REL-005).</summary>
public sealed class FileLoggerOptions
{
    /// <summary>File-name prefix of the unattended <c>--scheduled-refresh</c> log: <c>scheduled-refresh-yyyyMMdd.log</c>.</summary>
    public const string ScheduledRefreshPrefix = "scheduled-refresh";

    /// <summary>Phase 18B REL-005: file-name prefix of the interactive app's own log: <c>app-yyyyMMdd.log</c> — never mixed with the scheduled-refresh log.</summary>
    public const string InteractiveAppPrefix = "app";

    /// <summary>Days a log file is kept (by last write time) before startup pruning deletes it — the same 14 days for both logs.</summary>
    public const int DefaultRetainedDays = 14;

    /// <summary>Defaults under <see cref="AppPaths.LocalApplicationDataDirectory"/> — never a hard-coded OS path (Part G).</summary>
    public string LogDirectory { get; set; } = Path.Combine(AppPaths.LocalApplicationDataDirectory, "logs");

    /// <summary>A log file older than this many days is deleted on startup, keeping growth bounded (Part G).</summary>
    public int RetainedDays { get; set; } = DefaultRetainedDays;

    /// <summary>Which log this provider writes; the file is <c>&lt;prefix&gt;-yyyyMMdd.log</c> (UTC date), and pruning only ever touches files of exactly that shape.</summary>
    public string FileNamePrefix { get; set; } = ScheduledRefreshPrefix;

    /// <summary>Clock for the daily file name and retention cutoff; tests substitute a fixed one.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    public static FileLoggerOptions ForInteractiveApp() => new() { FileNamePrefix = InteractiveAppPrefix };
}
