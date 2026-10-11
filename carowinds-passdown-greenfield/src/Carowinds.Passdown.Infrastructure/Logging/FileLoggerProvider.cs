using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Carowinds.Passdown.Application.Diagnostics;
using Carowinds.Passdown.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Infrastructure.Logging;

/// <summary>
/// A minimal, dependency-free <see cref="ILoggerProvider"/> that appends UTF-8 lines to one
/// rolling-by-day file <c>&lt;prefix&gt;-yyyyMMdd.log</c> under <see cref="FileLoggerOptions.LogDirectory"/>
/// (Phase 6A Part G). Used twice with different prefixes: the unattended scheduled refresh
/// (<c>scheduled-refresh-*</c>) and, since Phase 18B REL-005, the interactive app (<c>app-*</c>), so a
/// WinExe with no console still leaves a persistent trace of load/save/sync/PDF failures.
/// Deliberately small: no third-party logging package (Part G "do not add a large logging framework
/// merely for this"). Every construction step is deferred to first use so simply registering this
/// provider does no I/O — building the composition root must stay side-effect-free for tests.
/// <para>
/// Phase 18B privacy: every line (including an exception's text) passes through
/// <see cref="DiagnosticPathRedactor"/> before it reaches disk, so per-user paths (Windows account
/// name, OneDrive tenant folder) are not written even to the local file. Callers already log only
/// ids, dates, counts, and statuses — never draft/note/Daily Feature text, PDF content, sync payloads,
/// or HTTP bodies (Phase 18A §6 #13). A logging failure (disk full, file locked) is swallowed: logging
/// must never take down the app or the run it is describing.
/// </para>
/// </summary>
public sealed partial class FileLoggerProvider : ILoggerProvider
{
    private readonly FileLoggerOptions _options;
    private readonly IReadOnlyList<KeyValuePair<string, string>> _pathRedactions;
    private readonly Regex _ownedFileName;
    private readonly Lock _writeLock = new();
    private bool _ready;

    public FileLoggerProvider(FileLoggerOptions options)
        : this(options, FileScheduledRefreshLogProvider.CurrentEnvironmentPathRedactions())
    {
    }

    public FileLoggerProvider(FileLoggerOptions options, IReadOnlyList<KeyValuePair<string, string>> pathRedactions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pathRedactions);
        if (!SafePrefix().IsMatch(options.FileNamePrefix))
        {
            throw new ArgumentException("The log file-name prefix must be lowercase letters and dashes only.", nameof(options));
        }

        _options = options;
        _pathRedactions = pathRedactions;
        _ownedFileName = OwnedLogFileName(options.FileNamePrefix);
    }

    /// <summary>The exact <c>&lt;prefix&gt;-yyyyMMdd.log</c> shape this provider writes, prunes, and diagnostics reads — nothing else in the directory ever matches.</summary>
    public static Regex OwnedLogFileName(string prefix) =>
        new($"^{Regex.Escape(prefix)}-[0-9]{{8}}\\.log$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    public void Dispose()
    {
    }

    /// <summary>The file today's lines go to (UTC date, so it rolls over at midnight UTC even in a long-running interactive session).</summary>
    internal string CurrentFilePath =>
        Path.Combine(_options.LogDirectory, $"{_options.FileNamePrefix}-{_options.TimeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");

    internal void Write(string line)
    {
        var redacted = DiagnosticPathRedactor.Redact(line, _pathRedactions);

        lock (_writeLock)
        {
            try
            {
                if (!_ready)
                {
                    Directory.CreateDirectory(_options.LogDirectory);
                    PruneOldLogs();
                    _ready = true;
                }

                File.AppendAllText(CurrentFilePath, redacted + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Never let logging break the caller (Phase 18B); the next line simply tries again.
            }
        }
    }

    /// <summary>
    /// Runs once per process, on the first logged line (i.e. at startup). Deletes only files whose
    /// name is exactly this provider's own <c>&lt;prefix&gt;-yyyyMMdd.log</c> and whose last write is
    /// older than <see cref="FileLoggerOptions.RetainedDays"/> — the other log's files, unrelated
    /// files, and anything in subfolders are never considered.
    /// </summary>
    private void PruneOldLogs()
    {
        var cutoffUtc = _options.TimeProvider.GetUtcNow().UtcDateTime.AddDays(-_options.RetainedDays);
        foreach (var file in Directory.EnumerateFiles(_options.LogDirectory, $"{_options.FileNamePrefix}-*.log", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (_ownedFileName.IsMatch(Path.GetFileName(file)) && File.GetLastWriteTimeUtc(file) < cutoffUtc)
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
                // Best-effort pruning only (Phase 6B Part J): a file locked by another process, or
                // one that vanished in a race with a concurrent prune, is left for next time — this
                // must never abort the run or the log line that triggered it.
            }
            catch (UnauthorizedAccessException)
            {
                // Same best-effort contract as above — e.g. a transient AV-scan hold on Windows.
            }
        }
    }

    [GeneratedRegex("^[a-z]+(-[a-z]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SafePrefix();

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

            var line = $"{provider._options.TimeProvider.GetUtcNow():O} [{logLevel}] {categoryName}: {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            provider.Write(line);
        }
    }
}
