using Carowinds.Passdown.Application.Diagnostics;
using Carowinds.Passdown.Infrastructure.Logging;

namespace Carowinds.Passdown.Infrastructure.Diagnostics;

/// <summary>
/// Reads the tail of the most recent <c>scheduled-refresh-*.log</c> file for diagnostics (Phase 7F
/// Part D/G). Never writes, never deletes, and never touches any file outside
/// <see cref="FileLoggerOptions.LogDirectory"/> matching this app's own log naming pattern — the
/// same scoping <see cref="FileLoggerProvider.PruneOldLogs"/> already relies on. Returns an empty
/// list (never throws) when the log directory or any log file does not yet exist, or cannot be read
/// (Part S "export handles missing log directory").
/// </summary>
public sealed class FileScheduledRefreshLogProvider(FileLoggerOptions options, IReadOnlyList<KeyValuePair<string, string>> pathRedactions) : IScheduledRefreshLogProvider
{
    public FileScheduledRefreshLogProvider(FileLoggerOptions options)
        : this(options, CurrentEnvironmentPathRedactions())
    {
    }

    /// <summary>
    /// Phase 18A PRIV-001: the per-user roots whose literal spelling identifies this person/machine —
    /// replaced by environment-variable tokens before any log line can enter the diagnostic ZIP (see
    /// <see cref="DiagnosticPathRedactor"/>).
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> CurrentEnvironmentPathRedactions()
    {
        var roots = new List<KeyValuePair<string, string>>
        {
            new(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%"),
            new(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%"),
            new(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%"),
            new(Path.GetTempPath(), "%TEMP%"),
        };

        foreach (var variable in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
        {
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value)
            {
                roots.Add(new(value, $"%{variable}%"));
            }
        }

        return roots;
    }

    public Task<IReadOnlyList<string>> GetRecentLogLinesAsync(int maxLines, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxLines, 0);

        if (!Directory.Exists(options.LogDirectory))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        try
        {
            var latestLogFile = Directory.EnumerateFiles(options.LogDirectory, "scheduled-refresh-*.log")
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (latestLogFile is null)
            {
                return Task.FromResult<IReadOnlyList<string>>([]);
            }

            var lines = File.ReadLines(latestLogFile).ToList();
            var tail = lines.Count <= maxLines ? lines : lines[^maxLines..];
            IReadOnlyList<string> redacted = tail.Select(line => DiagnosticPathRedactor.Redact(line, pathRedactions)).ToList();
            return Task.FromResult(redacted);
        }
        catch (IOException)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }
    }
}
