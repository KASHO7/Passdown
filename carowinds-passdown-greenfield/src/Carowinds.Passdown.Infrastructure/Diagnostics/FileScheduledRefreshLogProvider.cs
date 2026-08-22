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
public sealed class FileScheduledRefreshLogProvider(FileLoggerOptions options) : IScheduledRefreshLogProvider
{
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
            IReadOnlyList<string> tail = lines.Count <= maxLines ? lines : lines[^maxLines..];
            return Task.FromResult(tail);
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
