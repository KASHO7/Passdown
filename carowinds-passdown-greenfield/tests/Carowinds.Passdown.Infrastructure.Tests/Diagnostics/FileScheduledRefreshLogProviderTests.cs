using Carowinds.Passdown.Infrastructure.Diagnostics;
using Carowinds.Passdown.Infrastructure.Logging;

namespace Carowinds.Passdown.Infrastructure.Tests.Diagnostics;

/// <summary>Phase 7F Part D/G/S: only ever reads this app's own log file naming pattern, never writes, and never throws for a missing log directory.</summary>
public class FileScheduledRefreshLogProviderTests : IDisposable
{
    private readonly string _logDirectory = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-diag-logs-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_logDirectory))
        {
            Directory.Delete(_logDirectory, recursive: true);
        }
    }

    private FileScheduledRefreshLogProvider CreateSut() => new(new FileLoggerOptions { LogDirectory = _logDirectory });

    [Fact]
    public async Task GetRecentLogLinesAsync_MissingLogDirectory_ReturnsEmptyWithoutThrowing()
    {
        var result = await CreateSut().GetRecentLogLinesAsync(100);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRecentLogLinesAsync_NoLogFilesYet_ReturnsEmpty()
    {
        Directory.CreateDirectory(_logDirectory);

        var result = await CreateSut().GetRecentLogLinesAsync(100);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRecentLogLinesAsync_FewerLinesThanMax_ReturnsAllLinesInOrder()
    {
        Directory.CreateDirectory(_logDirectory);
        var logFile = Path.Combine(_logDirectory, "scheduled-refresh-20260822.log");
        await File.WriteAllLinesAsync(logFile, ["line one", "line two", "line three"]);

        var result = await CreateSut().GetRecentLogLinesAsync(100);

        Assert.Equal(["line one", "line two", "line three"], result);
    }

    [Fact]
    public async Task GetRecentLogLinesAsync_MoreLinesThanMax_ReturnsOnlyTheTail()
    {
        Directory.CreateDirectory(_logDirectory);
        var logFile = Path.Combine(_logDirectory, "scheduled-refresh-20260822.log");
        await File.WriteAllLinesAsync(logFile, Enumerable.Range(1, 10).Select(i => $"line {i}"));

        var result = await CreateSut().GetRecentLogLinesAsync(3);

        Assert.Equal(["line 8", "line 9", "line 10"], result);
    }

    [Fact]
    public async Task GetRecentLogLinesAsync_MultipleLogFiles_ReadsOnlyTheMostRecentByName()
    {
        Directory.CreateDirectory(_logDirectory);
        await File.WriteAllLinesAsync(Path.Combine(_logDirectory, "scheduled-refresh-20260820.log"), ["old day"]);
        await File.WriteAllLinesAsync(Path.Combine(_logDirectory, "scheduled-refresh-20260822.log"), ["latest day"]);

        var result = await CreateSut().GetRecentLogLinesAsync(100);

        Assert.Equal(["latest day"], result);
    }

    [Fact]
    public async Task GetRecentLogLinesAsync_UnrelatedFileInLogDirectory_IsIgnored()
    {
        Directory.CreateDirectory(_logDirectory);
        await File.WriteAllTextAsync(Path.Combine(_logDirectory, "unrelated-file.txt"), "not a scheduled-refresh log");
        await File.WriteAllLinesAsync(Path.Combine(_logDirectory, "scheduled-refresh-20260822.log"), ["real log line"]);

        var result = await CreateSut().GetRecentLogLinesAsync(100);

        Assert.Equal(["real log line"], result);
    }

    /// <summary>
    /// Phase 18A PRIV-001: these lines are copied into the diagnostic ZIP a user sends to support.
    /// Pre-18A they were copied verbatim, so a logged exception message carried the Windows account
    /// name and OneDrive tenant folder out of the machine.
    /// </summary>
    [Fact]
    public async Task GetRecentLogLinesAsync_RedactsUserProfileAndOneDrivePaths_KeepsTheRestOfTheLine()
    {
        Directory.CreateDirectory(_logDirectory);
        await File.WriteAllLinesAsync(Path.Combine(_logDirectory, "scheduled-refresh-20260822.log"),
        [
            @"2026-08-22T10:00:00Z [Critical] ScheduledRefresh: Fatal: System.IO.IOException: Could not find a part of the path 'C:\Users\jane.doe\AppData\Local\Carowinds Passdown\logs'.",
            @"System.UnauthorizedAccessException: Access to the path 'C:\Users\jane.doe\OneDrive - Contoso\Ops\x.json' is denied.",
            @"Another profile: D:\Users\Someone Else\file.txt",
        ]);

        var sut = new FileScheduledRefreshLogProvider(
            new FileLoggerOptions { LogDirectory = _logDirectory },
            [
                new(@"C:\Users\jane.doe", "%USERPROFILE%"),
                new(@"C:\Users\jane.doe\AppData\Local", "%LOCALAPPDATA%"),
                new(@"C:\Users\jane.doe\OneDrive - Contoso", "%OneDriveCommercial%"),
            ]);

        var result = await sut.GetRecentLogLinesAsync(100);
        var all = string.Join("\n", result);

        Assert.DoesNotContain("jane.doe", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Contoso", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Someone Else", all, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"'%LOCALAPPDATA%\Carowinds Passdown\logs'", result[0]);
        Assert.Contains(@"'%OneDriveCommercial%\Ops\x.json' is denied.", result[1]);
        Assert.Contains("System.IO.IOException: Could not find a part of the path", result[0]);
    }

    [Fact]
    public async Task GetRecentLogLinesAsync_DefaultConstructor_RedactsThisMachinesOwnProfilePath()
    {
        Directory.CreateDirectory(_logDirectory);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        await File.WriteAllLinesAsync(Path.Combine(_logDirectory, "scheduled-refresh-20260822.log"), [$"IOException at '{Path.Combine(profile, "secret-folder", "a.txt")}'"]);

        var result = await CreateSut().GetRecentLogLinesAsync(100);

        Assert.DoesNotContain(profile, result[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secret-folder", result[0]);
    }
}
