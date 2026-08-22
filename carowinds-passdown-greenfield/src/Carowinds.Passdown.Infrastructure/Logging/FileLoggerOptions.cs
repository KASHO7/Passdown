namespace Carowinds.Passdown.Infrastructure.Logging;

/// <summary>Tunables for <see cref="FileLoggerProvider"/> (Phase 6A Part G).</summary>
public sealed class FileLoggerOptions
{
    /// <summary>Defaults under <see cref="AppPaths.LocalApplicationDataDirectory"/> — never a hard-coded OS path (Part G).</summary>
    public string LogDirectory { get; set; } = Path.Combine(AppPaths.LocalApplicationDataDirectory, "logs");

    /// <summary>A log file older than this many days is deleted on startup, keeping growth bounded (Part G).</summary>
    public int RetainedDays { get; set; } = 14;
}
