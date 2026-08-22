using System.Text;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Infrastructure.Logging;

/// <summary>
/// A minimal, dependency-free <see cref="ILoggerProvider"/> that appends UTF-8 lines to one
/// rolling-by-day file under <see cref="FileLoggerOptions.LogDirectory"/> (Phase 6A Part G) — for
/// unattended runs where no console is visible after the process exits. Deliberately small: no
/// third-party logging package, since one file appended to under a lock is all this needs (Part G
/// "do not add a large logging framework merely for this"). Every construction step is deferred to
/// first use (<see cref="EnsureReady"/>) so simply registering this provider does no I/O — building
/// the composition root must stay side-effect-free for tests (mirrors <c>SqliteConnectionFactory</c>'s
/// "create the directory/file on first use" contract).
/// </summary>
public sealed class FileLoggerProvider(FileLoggerOptions options) : ILoggerProvider
{
    private readonly Lock _readyLock = new();
    private string? _filePath;

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    public void Dispose()
    {
    }

    internal void Write(string line)
    {
        var filePath = EnsureReady();
        lock (_readyLock)
        {
            File.AppendAllText(filePath, line + Environment.NewLine, Encoding.UTF8);
        }
    }

    private string EnsureReady()
    {
        if (_filePath is not null)
        {
            return _filePath;
        }

        lock (_readyLock)
        {
            if (_filePath is not null)
            {
                return _filePath;
            }

            Directory.CreateDirectory(options.LogDirectory);
            PruneOldLogs();
            _filePath = Path.Combine(options.LogDirectory, $"scheduled-refresh-{DateTime.UtcNow:yyyyMMdd}.log");
            return _filePath;
        }
    }

    private void PruneOldLogs()
    {
        var cutoffUtc = DateTime.UtcNow.AddDays(-options.RetainedDays);
        foreach (var file in Directory.EnumerateFiles(options.LogDirectory, "scheduled-refresh-*.log"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoffUtc)
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
                // Best-effort pruning only: a file locked by another process is left for next time.
            }
        }
    }

    private sealed class FileLogger(string categoryName, FileLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = $"{DateTimeOffset.UtcNow:O} [{logLevel}] {categoryName}: {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            provider.Write(line);
        }
    }
}
