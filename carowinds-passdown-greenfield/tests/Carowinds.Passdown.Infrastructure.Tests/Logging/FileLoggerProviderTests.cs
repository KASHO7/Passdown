using Carowinds.Passdown.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Infrastructure.Tests.Logging;

/// <summary>Phase 6A Part G/L (#16/#17): the scheduled-refresh operational log file — location, UTF-8 append behavior, and bounded-growth pruning.</summary>
public class FileLoggerProviderTests
{
    // 16. Log file path uses AppPaths/local-data path.
    [Fact]
    public void DefaultLogDirectory_IsUnderAppPathsLocalApplicationDataDirectory()
    {
        var options = new FileLoggerOptions();

        Assert.StartsWith(AppPaths.LocalApplicationDataDirectory, options.LogDirectory);
    }

    // 17. No hard-coded Windows user path exists.
    [Fact]
    public void DefaultLogDirectory_ContainsNoHardCodedWindowsUserPath()
    {
        var options = new FileLoggerOptions();

        Assert.DoesNotContain(@"C:\Users", options.LogDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"\AppData\", options.LogDirectory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Log_WritesUtf8LineToDailyFile_UnderConfiguredDirectory()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-logs-{Guid.NewGuid():N}");
        try
        {
            using var provider = new FileLoggerProvider(new FileLoggerOptions { LogDirectory = tempDirectory });
            var logger = provider.CreateLogger("Test.Category");

            logger.LogInformation("Hello, {Name}.", "world");

            var expectedFile = Path.Combine(tempDirectory, $"scheduled-refresh-{DateTime.UtcNow:yyyyMMdd}.log");
            Assert.True(File.Exists(expectedFile));
            var content = File.ReadAllText(expectedFile);
            Assert.Contains("Hello, world.", content);
            Assert.Contains("Test.Category", content);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Construction_DoesNoIo_UntilFirstLogCall()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-logs-{Guid.NewGuid():N}");

        using var provider = new FileLoggerProvider(new FileLoggerOptions { LogDirectory = tempDirectory });
        _ = provider.CreateLogger("Test.Category");

        Assert.False(Directory.Exists(tempDirectory));
    }

    [Fact]
    public void Log_PrunesLogFilesOlderThanRetainedDays()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-logs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var staleFile = Path.Combine(tempDirectory, "scheduled-refresh-20200101.log");
            File.WriteAllText(staleFile, "stale");
            File.SetLastWriteTimeUtc(staleFile, DateTime.UtcNow.AddDays(-30));

            using var provider = new FileLoggerProvider(new FileLoggerOptions { LogDirectory = tempDirectory, RetainedDays = 14 });
            var logger = provider.CreateLogger("Test.Category");

            logger.LogInformation("trigger pruning");

            Assert.False(File.Exists(staleFile));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    // Phase 7D Part J: pruning must only ever consider files matching this app's own
    // "scheduled-refresh-*.log" naming pattern — an unrelated legacy log file sharing the same
    // logs\ directory (a different app, or a future differently-named file of this app's own)
    // must never be enumerated or deleted, no matter how old its timestamp is.
    [Fact]
    public void Log_PruningIgnoresUnrelatedLogFiles_RegardlessOfAge()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-logs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var unrelatedOldLog = Path.Combine(tempDirectory, "previous-app.log");
            File.WriteAllText(unrelatedOldLog, "legacy content");
            File.SetLastWriteTimeUtc(unrelatedOldLog, DateTime.UtcNow.AddDays(-365));

            var unrelatedOldFile = Path.Combine(tempDirectory, "old-settings.xml");
            File.WriteAllText(unrelatedOldFile, "<settings/>");
            File.SetLastWriteTimeUtc(unrelatedOldFile, DateTime.UtcNow.AddDays(-365));

            var ownStaleLog = Path.Combine(tempDirectory, "scheduled-refresh-20200101.log");
            File.WriteAllText(ownStaleLog, "stale");
            File.SetLastWriteTimeUtc(ownStaleLog, DateTime.UtcNow.AddDays(-365));

            using var provider = new FileLoggerProvider(new FileLoggerOptions { LogDirectory = tempDirectory, RetainedDays = 14 });
            var logger = provider.CreateLogger("Test.Category");

            logger.LogInformation("trigger pruning");

            Assert.True(File.Exists(unrelatedOldLog), "Unrelated legacy log must never be pruned.");
            Assert.True(File.Exists(unrelatedOldFile), "Unrelated non-log file must never be pruned.");
            Assert.False(File.Exists(ownStaleLog), "This app's own stale scheduled-refresh log should still be pruned.");
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    // Phase 6B Part J (#20): a failure while pruning one stale file must not stop the current
    // log line from being written, and must not abort the caller's refresh. Windows has no direct
    // equivalent of the Unix-permission trick below, but the code path under test (the per-file
    // try/catch in PruneOldLogs) is platform-agnostic — this exercises the same catch clauses a
    // Windows AV-scan-hold or ACL failure would hit.
    [Fact]
    public void Log_WhenPruningAnOldFileFails_StillWritesTheCurrentDayFile()
    {
        if (OperatingSystem.IsWindows())
        {
            // Directory Unix-permission bits aren't a faithful simulation on Windows; the
            // production code path (broadened catch around File.Delete) is still covered by the
            // Unix run of this same test in CI/dev.
            return;
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-logs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var staleFile = Path.Combine(tempDirectory, "scheduled-refresh-20200101.log");
            File.WriteAllText(staleFile, "stale");
            File.SetLastWriteTimeUtc(staleFile, DateTime.UtcNow.AddDays(-30));

            // Pre-create today's file as an existing directory entry: appending to it needs only
            // file-level write permission, not directory-level, so it stays possible even once the
            // directory itself is made read-only below.
            var todayFile = Path.Combine(tempDirectory, $"scheduled-refresh-{DateTime.UtcNow:yyyyMMdd}.log");
            File.WriteAllText(todayFile, "");

            File.SetUnixFileMode(tempDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            using var provider = new FileLoggerProvider(new FileLoggerOptions { LogDirectory = tempDirectory, RetainedDays = 14 });
            var logger = provider.CreateLogger("Test.Category");

            var exception = Record.Exception(() => logger.LogInformation("still usable"));

            Assert.Null(exception);
            Assert.Contains("still usable", File.ReadAllText(todayFile));
        }
        finally
        {
            File.SetUnixFileMode(tempDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
