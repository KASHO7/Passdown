using System.IO.Compression;
using Carowinds.Passdown.Application.Diagnostics;
using Carowinds.Passdown.Infrastructure.Diagnostics;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.Diagnostics;

/// <summary>
/// Phase 7F Part D/G/S: the diagnostic export ZIP must contain only sanitized text content, never
/// the SQLite database file or any operational note/draft content, and must produce a safe,
/// deterministic file name.
/// </summary>
public class DiagnosticExportServiceTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 22, 14, 5, 0, TimeSpan.Zero);

    private static DiagnosticReport BuildReport(IReadOnlyList<string>? logLines = null) => new(
        "0.1.0-dev",
        GeneratedAt,
        [new SourceStatusSummary("park-hours.official", "Park Hours", Domain.SourceCategory.ParkHours, DiagnosticSourceStatus.Ok, GeneratedAt, GeneratedAt, 0)],
        new DatabaseDiagnostics(true, 7, 45_000, false, null),
        new ScheduledTaskDiagnostics(ScheduledTaskDiagnosticStatus.Enabled, null),
        logLines ?? ["log line one", "log line two"]);

    private static DiagnosticExportService CreateSut(DiagnosticReport report) =>
        new(new FakeDiagnosticReportBuilder(report), new FakeClock(GeneratedAt));

    [Fact]
    public async Task BuildZipAsync_ProducesASafeDeterministicFileName()
    {
        var result = await CreateSut(BuildReport()).BuildZipAsync();

        Assert.Equal("Carowinds-Passdown-Diagnostics-20260822-1405.zip", result.SuggestedFileName);
        Assert.DoesNotContain(Path.DirectorySeparatorChar, result.SuggestedFileName);
        Assert.DoesNotContain(Path.AltDirectorySeparatorChar, result.SuggestedFileName);
    }

    [Fact]
    public async Task BuildZipAsync_ContainsExpectedTextEntries()
    {
        var result = await CreateSut(BuildReport()).BuildZipAsync();

        using var archive = new ZipArchive(new MemoryStream(result.ZipBytes), ZipArchiveMode.Read);
        var entryNames = archive.Entries.Select(e => e.FullName).ToList();

        Assert.Contains("summary.txt", entryNames);
        Assert.Contains("system.txt", entryNames);
        Assert.Contains("source-status.txt", entryNames);
        Assert.Contains("logs/scheduled-refresh-20260822.log", entryNames);
    }

    [Fact]
    public async Task BuildZipAsync_NeverContainsADatabaseFile()
    {
        var result = await CreateSut(BuildReport()).BuildZipAsync();

        using var archive = new ZipArchive(new MemoryStream(result.ZipBytes), ZipArchiveMode.Read);

        Assert.DoesNotContain(archive.Entries, e => e.FullName.EndsWith(".db", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildZipAsync_NoLogLines_OmitsTheLogsEntryEntirely()
    {
        var result = await CreateSut(BuildReport(logLines: [])).BuildZipAsync();

        using var archive = new ZipArchive(new MemoryStream(result.ZipBytes), ZipArchiveMode.Read);

        Assert.DoesNotContain(archive.Entries, e => e.FullName.StartsWith("logs/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuildZipAsync_SourceStatusEntry_IncludesSourceDisplayName()
    {
        var result = await CreateSut(BuildReport()).BuildZipAsync();

        using var archive = new ZipArchive(new MemoryStream(result.ZipBytes), ZipArchiveMode.Read);
        var entry = archive.GetEntry("source-status.txt")!;
        using var reader = new StreamReader(entry.Open());
        var content = await reader.ReadToEndAsync();

        Assert.Contains("Park Hours", content);
    }
}
