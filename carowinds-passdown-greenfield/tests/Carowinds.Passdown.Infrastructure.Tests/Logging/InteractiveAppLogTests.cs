using System.IO.Compression;
using Carowinds.Passdown.Application.Diagnostics;
using Carowinds.Passdown.Infrastructure.Diagnostics;
using Carowinds.Passdown.Infrastructure.Logging;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Infrastructure.Tests.Logging;

/// <summary>
/// Phase 18B REL-005: the interactive app's own file log — separate file name, write-time path
/// redaction, bounded retention that touches only its own files, never-throwing writes, and a bounded
/// redacted excerpt in the diagnostics ZIP.
/// </summary>
public class InteractiveAppLogTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 15, 30, 0, TimeSpan.Zero);

    private readonly string _logDirectory = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-app-log-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_logDirectory))
        {
            Directory.Delete(_logDirectory, recursive: true);
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private FileLoggerOptions AppOptions(TimeProvider? time = null)
    {
        var options = FileLoggerOptions.ForInteractiveApp();
        options.LogDirectory = _logDirectory;
        options.TimeProvider = time ?? new FixedTime(Now);
        return options;
    }

    private static readonly KeyValuePair<string, string>[] Redactions =
    [
        new(@"C:\Users\jane.doe\AppData\Local", "%LOCALAPPDATA%"),
        new(@"C:\Users\jane.doe\OneDrive - Contoso", "%OneDrive%"),
        new(@"C:\Users\jane.doe", "%USERPROFILE%"),
    ];

    private string AppLogPath(string date) => Path.Combine(_logDirectory, $"app-{date}.log");

    [Fact]
    public void ForInteractiveApp_UsesAppPrefix_SameDirectory_And14DayRetention()
    {
        var options = FileLoggerOptions.ForInteractiveApp();

        Assert.Equal("app", options.FileNamePrefix);
        Assert.Equal(new FileLoggerOptions().LogDirectory, options.LogDirectory);
        Assert.Equal(14, options.RetainedDays);
        Assert.StartsWith(AppPaths.LocalApplicationDataDirectory, options.LogDirectory);
    }

    [Fact]
    public void AppLogger_WritesAppYyyyMMddLog_NeverTheScheduledRefreshFile()
    {
        using var provider = new FileLoggerProvider(AppOptions(), Redactions);

        provider.CreateLogger("Startup").LogInformation("Database ready at schema version {SchemaVersion}.", 15);

        Assert.Equal([AppLogPath("20261010")], Directory.GetFiles(_logDirectory));
        Assert.Contains("[Information] Startup: Database ready at schema version 15.", File.ReadAllText(AppLogPath("20261010")));
    }

    [Fact]
    public void AppLogger_RollsToANewFile_WhenTheUtcDateChanges()
    {
        var time = new FixedTime(Now);
        using var provider = new FileLoggerProvider(AppOptions(time), Redactions);
        var logger = provider.CreateLogger("X");

        logger.LogInformation("day one");
        time.Now = Now.AddDays(1);
        logger.LogInformation("day two");

        Assert.Contains("day one", File.ReadAllText(AppLogPath("20261010")));
        Assert.Contains("day two", File.ReadAllText(AppLogPath("20261011")));
    }

    [Fact]
    public void ExceptionPaths_AreRedactedBeforeReachingDisk()
    {
        using var provider = new FileLoggerProvider(AppOptions(), Redactions);
        var ex = new IOException(@"Could not find a part of the path 'C:\Users\jane.doe\OneDrive - Contoso\Carowinds Operations Sync\drafts\2026-09-19.json'.");

        provider.CreateLogger("Sync").LogWarning(ex, "Personal Sync pass threw unexpectedly (reason={Reason}).", "Manual");

        var content = File.ReadAllText(AppLogPath("20261010"));
        Assert.DoesNotContain("jane.doe", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Contoso", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"%OneDrive%\Carowinds Operations Sync\drafts\2026-09-19.json", content);
        Assert.Contains("reason=Manual", content);
    }

    [Fact]
    public void UnknownUserProfilePaths_AreStillRedactedByTheGenericPattern()
    {
        using var provider = new FileLoggerProvider(AppOptions(), []);

        provider.CreateLogger("X").LogError(new UnauthorizedAccessException(@"Access to 'D:\Users\someone.else\Desktop\out.pdf' denied; also /Users/devname/x and /home/devname/y"), "PDF save failed.");

        var content = File.ReadAllText(AppLogPath("20261010"));
        Assert.DoesNotContain("someone.else", content);
        Assert.DoesNotContain("devname", content);
    }

    [Fact]
    public void Retention_DeletesOnlyOwnOldAppLogs_NeverScheduledRefreshOrUnrelatedFiles()
    {
        Directory.CreateDirectory(_logDirectory);
        string Create(string name, int ageDays)
        {
            var path = Path.Combine(_logDirectory, name);
            File.WriteAllText(path, "x");
            File.SetLastWriteTimeUtc(path, Now.UtcDateTime.AddDays(-ageDays));
            return path;
        }

        var oldApp = Create("app-20260901.log", 39);
        var edgeApp = Create("app-20260927.log", 13);
        var oldScheduled = Create("scheduled-refresh-20260901.log", 39);
        var appLookalike = Create("app-notes.log", 365);
        var appBackup = Create("app-20260901.log.bak", 365);
        var appLonger = Create("app-202609011.log", 365);
        var otherApp = Create("application-20260901.log", 365);
        var unrelated = Create("readme.txt", 365);
        Directory.CreateDirectory(Path.Combine(_logDirectory, "sub"));
        var nested = Path.Combine(_logDirectory, "sub", "app-20200101.log");
        File.WriteAllText(nested, "x");
        File.SetLastWriteTimeUtc(nested, Now.UtcDateTime.AddDays(-365));

        using var provider = new FileLoggerProvider(AppOptions(), Redactions);
        provider.CreateLogger("Startup").LogInformation("trigger startup pruning");

        Assert.False(File.Exists(oldApp));
        Assert.True(File.Exists(edgeApp));
        Assert.True(File.Exists(oldScheduled));
        Assert.True(File.Exists(appLookalike));
        Assert.True(File.Exists(appBackup));
        Assert.True(File.Exists(appLonger));
        Assert.True(File.Exists(otherApp));
        Assert.True(File.Exists(unrelated));
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void ScheduledRefreshRetention_NeverDeletesAppLogs()
    {
        Directory.CreateDirectory(_logDirectory);
        var oldApp = Path.Combine(_logDirectory, "app-20200101.log");
        File.WriteAllText(oldApp, "x");
        File.SetLastWriteTimeUtc(oldApp, Now.UtcDateTime.AddDays(-365));

        using var provider = new FileLoggerProvider(new FileLoggerOptions { LogDirectory = _logDirectory, TimeProvider = new FixedTime(Now) }, Redactions);
        provider.CreateLogger("Scheduled").LogInformation("trigger pruning");

        Assert.True(File.Exists(oldApp));
        Assert.True(File.Exists(Path.Combine(_logDirectory, "scheduled-refresh-20261010.log")));
    }

    [Fact]
    public void WriteFailure_IsSwallowed_SoLoggingNeverBreaksTheCaller()
    {
        // The log "directory" path is an existing file, so CreateDirectory/Append fail every time.
        File.WriteAllText(_logDirectory, "not a directory");
        try
        {
            using var provider = new FileLoggerProvider(AppOptions(), Redactions);
            var logger = provider.CreateLogger("X");

            var thrown = Record.Exception(() => logger.LogError(new InvalidOperationException("boom"), "Failure"));

            Assert.Null(thrown);
        }
        finally
        {
            File.Delete(_logDirectory);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("../app")]
    [InlineData("app*")]
    [InlineData("App")]
    public void UnsafePrefix_IsRejected(string prefix)
    {
        Assert.Throws<ArgumentException>(() => new FileLoggerProvider(new FileLoggerOptions { LogDirectory = _logDirectory, FileNamePrefix = prefix }, Redactions));
    }

    [Fact]
    public async Task AppLogExcerpt_IsTheBoundedRedactedTailOfTheNewestAppLog_Only()
    {
        Directory.CreateDirectory(_logDirectory);
        await File.WriteAllLinesAsync(AppLogPath("20261009"), ["older file line"]);
        await File.WriteAllLinesAsync(AppLogPath("20261010"), Enumerable.Range(1, 500).Select(i => $"line {i} C:\\Users\\jane.doe\\AppData\\Local\\Carowinds Passdown"));
        await File.WriteAllLinesAsync(Path.Combine(_logDirectory, "scheduled-refresh-20261011.log"), ["scheduled line"]);
        await File.WriteAllLinesAsync(Path.Combine(_logDirectory, "app-zzzz.log"), ["lookalike line"]);

        var sut = new FileInteractiveAppLogProvider(new FileLoggerOptions { LogDirectory = _logDirectory }, Redactions);
        var lines = await sut.GetRecentLogLinesAsync(200);

        Assert.Equal(200, lines.Count);
        Assert.StartsWith("line 301 ", lines[0]);
        Assert.StartsWith("line 500 ", lines[^1]);
        Assert.All(lines, line => Assert.DoesNotContain("jane.doe", line));
        Assert.All(lines, line => Assert.Contains("%LOCALAPPDATA%", line));
    }

    [Fact]
    public async Task AppLogExcerpt_MissingDirectoryOrFile_IsEmpty()
    {
        var sut = new FileInteractiveAppLogProvider(new FileLoggerOptions { LogDirectory = _logDirectory }, Redactions);
        Assert.Empty(await sut.GetRecentLogLinesAsync(200));

        Directory.CreateDirectory(_logDirectory);
        await File.WriteAllLinesAsync(Path.Combine(_logDirectory, "scheduled-refresh-20261010.log"), ["scheduled only"]);
        Assert.Empty(await sut.GetRecentLogLinesAsync(200));
    }

    [Fact]
    public async Task AppLogExcerpt_CanBeReadWhileTheLoggerStillHoldsTheFile()
    {
        using var provider = new FileLoggerProvider(new FileLoggerOptions { LogDirectory = _logDirectory, FileNamePrefix = "app" }, Redactions);
        var logger = provider.CreateLogger("X");
        logger.LogInformation("first");

        var lines = await new FileInteractiveAppLogProvider(new FileLoggerOptions { LogDirectory = _logDirectory }, Redactions).GetRecentLogLinesAsync(10);
        logger.LogInformation("second");

        Assert.Single(lines);
        Assert.Contains("first", lines[0]);
    }

    [Fact]
    public async Task DiagnosticsZip_IncludesBothLogExcerpts_UnderFixedEntryNames()
    {
        var report = new DiagnosticReport(
            "0.1.0-dev",
            Now,
            [],
            new DatabaseDiagnostics(true, 15, 1, false, null),
            new ScheduledTaskDiagnostics(ScheduledTaskDiagnosticStatus.Enabled, null),
            ["scheduled excerpt"],
            ["app excerpt"]);

        var zip = await new DiagnosticExportService(new FakeDiagnosticReportBuilder(report), new FakeClock(Now)).BuildZipAsync();

        using var archive = new ZipArchive(new MemoryStream(zip.ZipBytes), ZipArchiveMode.Read);
        var names = archive.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(["logs/app-20261010.log", "logs/scheduled-refresh-20261010.log", "source-status.txt", "summary.txt", "system.txt"], names);
        using var reader = new StreamReader(archive.GetEntry("logs/app-20261010.log")!.Open());
        Assert.Equal("app excerpt", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task DiagnosticsZip_NoAppLog_OmitsTheAppEntry()
    {
        var report = new DiagnosticReport(
            "0.1.0-dev", Now, [], new DatabaseDiagnostics(true, 15, 1, false, null),
            new ScheduledTaskDiagnostics(ScheduledTaskDiagnosticStatus.Enabled, null), ["scheduled excerpt"]);

        var zip = await new DiagnosticExportService(new FakeDiagnosticReportBuilder(report), new FakeClock(Now)).BuildZipAsync();

        using var archive = new ZipArchive(new MemoryStream(zip.ZipBytes), ZipArchiveMode.Read);
        Assert.DoesNotContain(archive.Entries, e => e.FullName.StartsWith("logs/app-", StringComparison.Ordinal));
        Assert.Empty(report.RecentAppLogLines);
    }
}
