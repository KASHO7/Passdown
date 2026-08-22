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
}
