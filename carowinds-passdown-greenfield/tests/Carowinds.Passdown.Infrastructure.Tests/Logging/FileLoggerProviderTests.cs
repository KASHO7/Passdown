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
}
