using Carowinds.Passdown.Application.Diagnostics;
using Carowinds.Passdown.Infrastructure.Logging;

namespace Carowinds.Passdown.Infrastructure.Diagnostics;

/// <summary>
/// Reads the tail of the most recent <c>scheduled-refresh-*.log</c> file for diagnostics (Phase 7F
/// Part D/G). Never writes, never deletes, and never touches any file outside
/// <see cref="FileLoggerOptions.LogDirectory"/> matching this app's own log naming pattern — the
/// same scoping pruning in <see cref="FileLoggerProvider"/> already relies on. Returns an empty
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

    public Task<IReadOnlyList<string>> GetRecentLogLinesAsync(int maxLines, CancellationToken cancellationToken = default) =>
        Task.FromResult(ReadRedactedTail(options, FileLoggerOptions.ScheduledRefreshPrefix, pathRedactions, maxLines));

    /// <summary>
    /// The last <paramref name="maxLines"/> lines of the newest <c>&lt;prefix&gt;-yyyyMMdd.log</c> in
    /// <see cref="FileLoggerOptions.LogDirectory"/>, path-redacted. Only files of exactly that name are
    /// considered (<see cref="FileLoggerProvider.OwnedLogFileName"/>); the result is bounded — never the
    /// full log history. Never throws for a missing/unreadable directory or file.
    /// </summary>
    internal static IReadOnlyList<string> ReadRedactedTail(FileLoggerOptions options, string prefix, IReadOnlyList<KeyValuePair<string, string>> pathRedactions, int maxLines)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxLines, 0);

        if (!Directory.Exists(options.LogDirectory))
        {
            return [];
        }

        try
        {
            var owned = FileLoggerProvider.OwnedLogFileName(prefix);
            var latestLogFile = Directory.EnumerateFiles(options.LogDirectory, $"{prefix}-*.log", SearchOption.TopDirectoryOnly)
                .Where(path => owned.IsMatch(Path.GetFileName(path)))
                .OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (latestLogFile is null)
            {
                return [];
            }

            // A bounded ring buffer: only the tail is ever held, however large today's file is.
            var tail = new Queue<string>(maxLines);
            using (var stream = new FileStream(latestLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
            {
                while (reader.ReadLine() is { } line)
                {
                    if (tail.Count == maxLines)
                    {
                        tail.Dequeue();
                    }

                    tail.Enqueue(line);
                }
            }

            return tail.Select(line => DiagnosticPathRedactor.Redact(line, pathRedactions)).ToList();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}

/// <summary>
/// Phase 18B REL-005: the same bounded, redacted tail as <see cref="FileScheduledRefreshLogProvider"/>,
/// for the interactive app's own <c>app-yyyyMMdd.log</c>.
/// </summary>
public sealed class FileInteractiveAppLogProvider(FileLoggerOptions options, IReadOnlyList<KeyValuePair<string, string>> pathRedactions) : IInteractiveAppLogProvider
{
    public FileInteractiveAppLogProvider(FileLoggerOptions options)
        : this(options, FileScheduledRefreshLogProvider.CurrentEnvironmentPathRedactions())
    {
    }

    public Task<IReadOnlyList<string>> GetRecentLogLinesAsync(int maxLines, CancellationToken cancellationToken = default) =>
        Task.FromResult(FileScheduledRefreshLogProvider.ReadRedactedTail(options, FileLoggerOptions.InteractiveAppPrefix, pathRedactions, maxLines));
}
