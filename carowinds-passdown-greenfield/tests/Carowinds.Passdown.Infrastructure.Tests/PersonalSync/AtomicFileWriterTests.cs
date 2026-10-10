using Carowinds.Passdown.Infrastructure.PersonalSync;

namespace Carowinds.Passdown.Infrastructure.Tests.PersonalSync;

/// <summary>Phase 16B §10: write-temp-then-rename — a reader must never observe a partial write, and no orphan temp file may survive a successful or failed write.</summary>
public class AtomicFileWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-atomic-writer-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task WriteTextAsync_CreatesParentDirectory_AndWritesFile()
    {
        var path = Path.Combine(_directory, "nested", "file.json");

        await AtomicFileWriter.WriteTextAsync(path, "hello");

        Assert.Equal("hello", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task WriteTextAsync_NoTempFileSurvives_AfterASuccessfulWrite()
    {
        var path = Path.Combine(_directory, "file.json");

        await AtomicFileWriter.WriteTextAsync(path, "hello");

        var entries = Directory.GetFiles(_directory);
        Assert.Single(entries);
        Assert.Equal(path, entries[0]);
    }

    [Fact]
    public async Task WriteTextAsync_ExistingFile_OverwritesAtomically()
    {
        var path = Path.Combine(_directory, "file.json");
        await AtomicFileWriter.WriteTextAsync(path, "first");

        await AtomicFileWriter.WriteTextAsync(path, "second");

        Assert.Equal("second", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task WriteBytesAsync_WritesExactBytes()
    {
        var path = Path.Combine(_directory, "file.bin");
        byte[] bytes = [1, 2, 3, 4];

        await AtomicFileWriter.WriteBytesAsync(path, bytes);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task WriteTextAsync_Cancelled_ThrowsAndLeavesNoTempFile()
    {
        var path = Path.Combine(_directory, "file.json");
        Directory.CreateDirectory(_directory);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AtomicFileWriter.WriteTextAsync(path, "hello", cts.Token));

        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task WriteTextAsync_StaleTempFileAlreadyPresent_DoesNotAffectTheRealWrite()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "file.json");
        var staleTemp = Path.Combine(_directory, "file.json.tmp-deadbeefdeadbeefdeadbeefdeadbeef");
        await File.WriteAllTextAsync(staleTemp, "leftover from a crashed write");

        await AtomicFileWriter.WriteTextAsync(path, "hello");

        Assert.Equal("hello", await File.ReadAllTextAsync(path));
        Assert.True(File.Exists(staleTemp), "a stray temp file from a different write attempt is not this writer's to clean up");
    }
}
