using Carowinds.Passdown.Infrastructure.Files;

namespace Carowinds.Passdown.Infrastructure.Tests.Files;

/// <summary>Phase 18B PRIV-002: "Open Final PDF" temp files are unique per open, removed on failure, swept by age, and only the store's own names are ever deleted.</summary>
public class HistoryPdfTempFileStoreTests : IDisposable
{
    private const string Suggested = "Carowinds-Operations-2026-09-19.pdf";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-temp-pdf-tests-{Guid.NewGuid():N}", "History");

    public void Dispose()
    {
        var parent = Path.GetDirectoryName(_directory)!;
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private HistoryPdfTempFileStore CreateSut() => new(_directory);

    private string Create(string name, TimeSpan age)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, [1]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
        return path;
    }

    [Fact]
    public async Task WriteAsync_EachOpenGetsItsOwnFile_WithTheExactBytes()
    {
        var sut = CreateSut();
        byte[] pdf = [0x25, 0x50, 0x44, 0x46];

        var first = await sut.WriteAsync(pdf, Suggested);
        var second = await sut.WriteAsync(pdf, Suggested);

        Assert.NotEqual(first, second);
        Assert.Equal(pdf, await File.ReadAllBytesAsync(first));
        Assert.Equal(pdf, await File.ReadAllBytesAsync(second));
        Assert.Matches(@"^Carowinds-Operations-2026-09-19\.view-[0-9a-f]{32}\.pdf$", Path.GetFileName(first));
        Assert.Equal(Path.GetFullPath(_directory), Path.GetDirectoryName(first));
    }

    [Fact]
    public async Task WriteAsync_WhilePreviousCopyIsLocked_StillSucceeds()
    {
        var sut = CreateSut();
        var first = await sut.WriteAsync([1, 2, 3], Suggested);

        using (new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var second = await sut.WriteAsync([1, 2, 3], Suggested);
            Assert.True(File.Exists(second));
        }
    }

    [Fact]
    public async Task WriteAsync_Cancelled_LeavesNoFile()
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.WriteAsync(new byte[1_000_000], Suggested, cts.Token));

        Assert.Empty(Directory.Exists(_directory) ? Directory.GetFiles(_directory) : []);
    }

    [Theory]
    [InlineData("../evil.pdf")]
    [InlineData("C:\\Users\\x\\Desktop\\a.pdf")]
    [InlineData("/etc/passwd")]
    [InlineData("Carowinds-Operations-2026-09-19.exe")]
    [InlineData("report.pdf")]
    [InlineData("Carowinds-Operations-2026-09-19.pdf/../../x.pdf")]
    public void CreateUniquePath_RejectsAnythingButTheAppsOwnSuggestedName(string suggested)
    {
        Assert.Throws<ArgumentException>(() => CreateSut().CreateUniquePath(suggested));
    }

    [Fact]
    public void CleanStale_DeletesOnlyOwnFilesOlderThanTheThreshold()
    {
        var staleView = Create("Carowinds-Operations-2026-09-19.view-0123456789abcdef0123456789abcdef.pdf", TimeSpan.FromDays(2));
        var staleLegacy = Create("Carowinds-Operations-2026-09-18.pdf", TimeSpan.FromDays(30));
        var fresh = Create("Carowinds-Operations-2026-09-20.view-0123456789abcdef0123456789abcdef.pdf", TimeSpan.FromHours(1));
        var userPdf = Create("My Passdown.pdf", TimeSpan.FromDays(365));
        var lookalike = Create("Carowinds-Operations-2026-09-19.view-XYZ.pdf", TimeSpan.FromDays(365));
        var otherPdf = Create("Carowinds-Operations-2026-09-19 (1).pdf", TimeSpan.FromDays(365));
        var notPdf = Create("Carowinds-Operations-2026-09-19.view-0123456789abcdef0123456789abcdef.txt", TimeSpan.FromDays(365));
        Directory.CreateDirectory(Path.Combine(_directory, "sub"));
        var nested = Path.Combine(_directory, "sub", "Carowinds-Operations-2026-09-19.pdf");
        File.WriteAllBytes(nested, [1]);
        File.SetLastWriteTimeUtc(nested, DateTime.UtcNow.AddDays(-365));

        var deleted = CreateSut().CleanStale();

        Assert.Equal(2, deleted);
        Assert.False(File.Exists(staleView));
        Assert.False(File.Exists(staleLegacy));
        Assert.True(File.Exists(fresh));
        Assert.True(File.Exists(userPdf));
        Assert.True(File.Exists(lookalike));
        Assert.True(File.Exists(otherPdf));
        Assert.True(File.Exists(notPdf));
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void CleanStale_NeverTouchesPdfsOutsideItsDirectory()
    {
        var parent = Path.GetDirectoryName(_directory)!;
        Directory.CreateDirectory(_directory);
        var sibling = Path.Combine(parent, "Carowinds-Operations-2026-09-19.pdf");
        File.WriteAllBytes(sibling, [1]);
        File.SetLastWriteTimeUtc(sibling, DateTime.UtcNow.AddDays(-365));

        CreateSut().CleanStale();

        Assert.True(File.Exists(sibling));
    }

    [Fact]
    public void CleanStale_MissingDirectory_IsANoOp()
    {
        Assert.Equal(0, CreateSut().CleanStale());
    }

    [Fact]
    public void TryDelete_RefusesPathsThatAreNotItsOwnFiles()
    {
        var parent = Path.GetDirectoryName(_directory)!;
        Directory.CreateDirectory(_directory);
        var outside = Path.Combine(parent, "Carowinds-Operations-2026-09-19.pdf");
        File.WriteAllBytes(outside, [1]);
        var user = Create("My Passdown.pdf", TimeSpan.Zero);

        Assert.False(CreateSut().TryDelete(outside));
        Assert.False(CreateSut().TryDelete(user));
        Assert.False(CreateSut().TryDelete(Path.Combine(_directory, "..", "Carowinds-Operations-2026-09-19.pdf")));
        Assert.True(File.Exists(outside));
        Assert.True(File.Exists(user));
    }

    [Fact]
    public async Task TryDelete_RemovesItsOwnFile()
    {
        var sut = CreateSut();
        var path = await sut.WriteAsync([1], Suggested);

        Assert.True(sut.TryDelete(path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void SymlinkedTempDirectory_IsNotCleaned()
    {
        var outside = Path.Combine(Path.GetDirectoryName(_directory)!, "outside");
        Directory.CreateDirectory(outside);
        var victim = Path.Combine(outside, "Carowinds-Operations-2026-09-19.pdf");
        File.WriteAllBytes(victim, [1]);
        File.SetLastWriteTimeUtc(victim, DateTime.UtcNow.AddDays(-365));
        try
        {
            Directory.CreateSymbolicLink(_directory, outside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // Link creation needs privileges on Windows; covered on macOS/Linux.
        }

        Assert.Equal(0, CreateSut().CleanStale());
        Assert.True(File.Exists(victim));
    }

    [Fact]
    public void DefaultDirectory_IsTheAppOwnedTempSubfolder()
    {
        Assert.Equal(Path.Combine(Path.GetTempPath(), "Carowinds Passdown", "History"), HistoryPdfTempFileStore.DefaultDirectory);
    }
}
