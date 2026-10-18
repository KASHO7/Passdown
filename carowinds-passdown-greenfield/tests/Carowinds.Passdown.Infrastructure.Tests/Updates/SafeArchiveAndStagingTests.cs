using System.IO.Compression;
using System.Text;
using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Infrastructure.Updates;

namespace Carowinds.Passdown.Infrastructure.Tests.Updates;

/// <summary>Phase 23D: archive safety (traversal, links, unexpected entries) and download/staging verification.</summary>
public sealed class SafeArchiveAndStagingTests : IDisposable
{
    private readonly UpdateTestKit _kit = new();

    public void Dispose() => _kit.Dispose();

    private string Destination => Path.Combine(_kit.Root, "staged-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ExactPackage_ExtractsEveryFile()
    {
        var (zip, manifest) = _kit.BuildVerifiedRelease("0.1.0-rc.8");
        var destination = Destination;
        SafeZipExtractor.ExtractVerified(zip, manifest, destination);

        Assert.Equal("desktop-exe 0.1.0-rc.8", File.ReadAllText(Path.Combine(destination, "Carowinds.Passdown.Desktop.exe")));
        Assert.True(File.Exists(Path.Combine(destination, "LatoFont", "OFL.txt")));
    }

    /// <summary>Builds a ZIP whose listed files match a valid manifest, plus one hostile extra entry.</summary>
    private (string Zip, UpdateManifest Manifest) WithExtraEntry(string entryName, Action<ZipArchiveEntry>? configure = null)
    {
        var (_, manifest) = _kit.BuildVerifiedRelease("0.1.0-rc.8");
        var zip = _kit.CreateZip("0.1.0-rc.8", extra: (archive, _) =>
        {
            var entry = archive.CreateEntry(entryName);
            configure?.Invoke(entry);
            using var stream = entry.Open();
            stream.Write("evil"u8);
        });
        return (zip, manifest);
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("Carowinds-Operations-0.1.0-rc.8-win-x64/../../evil.txt")]
    [InlineData("Carowinds-Operations-0.1.0-rc.8-win-x64/..\\..\\evil.txt")]
    [InlineData("/etc/evil.txt")]
    [InlineData("C:/Windows/evil.txt")]
    [InlineData("Carowinds-Operations-0.1.0-rc.8-win-x64/C:/evil.txt")]
    [InlineData("Carowinds-Operations-0.1.0-rc.8-win-x64/evil.txt:stream")]
    [InlineData("Carowinds-Operations-0.1.0-rc.8-win-x64/unexpected.exe")]
    [InlineData("Other-Root/Carowinds.Passdown.Desktop.exe")]
    public void HostileOrUnexpectedEntries_AreRejected_AndNothingIsLeftBehind(string entryName)
    {
        var (zip, manifest) = WithExtraEntry(entryName);
        var destination = Destination;

        var ex = Assert.Throws<UpdateStagingException>(() => SafeZipExtractor.ExtractVerified(zip, manifest, destination));
        Assert.Equal(StagingFailureKind.InvalidArchive, ex.Kind);
        Assert.False(Directory.Exists(destination));
        Assert.False(File.Exists(Path.Combine(_kit.Root, "evil.txt")));
    }

    [Fact]
    public void SymbolicLinkEntry_IsRejected()
    {
        var (zip, manifest) = WithExtraEntry("Carowinds-Operations-0.1.0-rc.8-win-x64/link", entry => entry.ExternalAttributes = unchecked((int)0xA1FF0000));
        Assert.Throws<UpdateStagingException>(() => SafeZipExtractor.ExtractVerified(zip, manifest, Destination));
    }

    [Fact]
    public void DuplicateEntry_IsRejected()
    {
        var (_, manifest) = _kit.BuildVerifiedRelease("0.1.0-rc.8");
        var zip = _kit.CreateZip("0.1.0-rc.8", extra: (archive, root) =>
        {
            using var stream = archive.CreateEntry($"{root}/coreclr.dll").Open();
            stream.Write(Encoding.UTF8.GetBytes("runtime 0.1.0-rc.8"));
        });
        Assert.Throws<UpdateStagingException>(() => SafeZipExtractor.ExtractVerified(zip, manifest, Destination));
    }

    [Fact]
    public void MissingFile_IsRejected()
    {
        var (_, manifest) = _kit.BuildVerifiedRelease("0.1.0-rc.8");
        var files = UpdateTestKit.AppFiles("0.1.0-rc.8");
        files.Remove("coreclr.dll");
        var zip = _kit.CreateZip("0.1.0-rc.8", files);
        Assert.Throws<UpdateStagingException>(() => SafeZipExtractor.ExtractVerified(zip, manifest, Destination));
    }

    [Fact]
    public void ModifiedFileContent_IsRejected()
    {
        var (_, manifest) = _kit.BuildVerifiedRelease("0.1.0-rc.8");
        var files = UpdateTestKit.AppFiles("0.1.0-rc.8");
        files["coreclr.dll"] = Encoding.UTF8.GetBytes("runtime 0.1.0-rc.X"); // same length, different bytes
        var zip = _kit.CreateZip("0.1.0-rc.8", files);
        Assert.Throws<UpdateStagingException>(() => SafeZipExtractor.ExtractVerified(zip, manifest, Destination));
    }

    [Fact]
    public void NotAZip_IsInvalidArchive()
    {
        var (_, manifest) = _kit.BuildVerifiedRelease("0.1.0-rc.8");
        var fake = Path.Combine(_kit.Root, "fake.zip");
        File.WriteAllText(fake, "this is not a zip");
        Assert.Equal(StagingFailureKind.InvalidArchive, Assert.Throws<UpdateStagingException>(() => SafeZipExtractor.ExtractVerified(fake, manifest, Destination)).Kind);
    }

    // ---------------------------------------------------------------- staging

    private sealed class ZipFileSource(string zipPath, Func<byte[], byte[]>? corrupt = null, Exception? failAfterHalf = null) : IUpdateReleaseSource
    {
        public Task<IReadOnlyList<ReleaseCandidate>> ListReleasesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<byte[]> DownloadSmallAssetAsync(ReleaseCandidate release, string assetName, int maximumBytes, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async Task DownloadPackageAsync(ReleaseCandidate release, string assetName, Stream destination, long expectedBytes, IProgress<long>? progress, CancellationToken cancellationToken = default)
        {
            var bytes = File.ReadAllBytes(zipPath);
            bytes = corrupt?.Invoke(bytes) ?? bytes;
            if (failAfterHalf is not null)
            {
                await destination.WriteAsync(bytes.AsMemory(0, bytes.Length / 2), cancellationToken);
                throw failAfterHalf;
            }

            await destination.WriteAsync(bytes, cancellationToken);
            progress?.Report(bytes.Length);
        }
    }

    private UpdateOffer Offer(UpdateManifest manifest) =>
        new(new ReleaseCandidate($"v{manifest.Version}", manifest.Version, null, null, false, []), manifest, manifest.Package.Size);

    [Fact]
    public async Task Stage_DownloadsVerifiesAndExtracts_OutsideTheInstallAndDataFolders()
    {
        var (zip, manifest) = _kit.BuildVerifiedRelease("0.1.0-rc.8");
        var paths = new UpdatePaths(Path.Combine(_kit.Root, "updates"));
        var staged = await new UpdatePackageStager(new ZipFileSource(zip), paths).StageAsync(Offer(manifest), null);

        Assert.True(UpdatePaths.IsWithin(staged.StagedDirectory, paths.Staging));
        Assert.True(File.Exists(Path.Combine(staged.StagedDirectory, "Carowinds.Passdown.Updater.exe")));
        // The downloaded ZIP is not kept once extracted.
        Assert.Empty(Directory.EnumerateFiles(paths.Downloads, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CorruptDownload_FailsHashCheck_AndLeavesNothingStaged()
    {
        var (zip, manifest) = _kit.BuildVerifiedRelease("0.1.0-rc.8");
        var paths = new UpdatePaths(Path.Combine(_kit.Root, "updates"));
        var source = new ZipFileSource(zip, bytes => { bytes[^10] ^= 0xFF; return bytes; });

        var ex = await Assert.ThrowsAsync<UpdateStagingException>(() => new UpdatePackageStager(source, paths).StageAsync(Offer(manifest), null));
        Assert.Equal(StagingFailureKind.HashMismatch, ex.Kind);
        Assert.Empty(Directory.EnumerateFileSystemEntries(paths.Staging));
        Assert.Empty(Directory.EnumerateFiles(paths.Downloads, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task InterruptedDownload_IsADownloadFailure_AndThePartialFileIsDeleted()
    {
        var (zip, manifest) = _kit.BuildVerifiedRelease("0.1.0-rc.8");
        var paths = new UpdatePaths(Path.Combine(_kit.Root, "updates"));
        var source = new ZipFileSource(zip, failAfterHalf: new UpdateSourceException(UpdateSourceFailureKind.Offline, "cut"));

        var ex = await Assert.ThrowsAsync<UpdateStagingException>(() => new UpdatePackageStager(source, paths).StageAsync(Offer(manifest), null));
        Assert.Equal(StagingFailureKind.Download, ex.Kind);
        Assert.Empty(Directory.EnumerateFiles(paths.Downloads, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task InsufficientDiskSpace_IsDetectedBeforeDownloading()
    {
        var (zip, manifest) = _kit.BuildVerifiedRelease("0.1.0-rc.8");
        var paths = new UpdatePaths(Path.Combine(_kit.Root, "updates"));
        var stager = new UpdatePackageStager(new ZipFileSource(zip), paths, availableFreeSpace: _ => 1024);

        var ex = await Assert.ThrowsAsync<UpdateStagingException>(() => stager.StageAsync(Offer(manifest), null));
        Assert.Equal(StagingFailureKind.InsufficientDiskSpace, ex.Kind);
    }

    [Fact]
    public async Task EveryAttempt_StartsFromAFreshDownload_StaleStagingIsNeverReused()
    {
        var (zip, manifest) = _kit.BuildVerifiedRelease("0.1.0-rc.8");
        var paths = new UpdatePaths(Path.Combine(_kit.Root, "updates"));
        var stager = new UpdatePackageStager(new ZipFileSource(zip), paths);
        var first = await stager.StageAsync(Offer(manifest), null);
        var second = await stager.StageAsync(Offer(manifest), null);
        Assert.NotEqual(first.StagedDirectory, second.StagedDirectory);
    }

    [Fact]
    public void CleanObsolete_RemovesOldStagingButKeepsRecentOnes()
    {
        var paths = new UpdatePaths(Path.Combine(_kit.Root, "updates"));
        var old = Directory.CreateDirectory(Path.Combine(paths.Staging, "0.1.0-rc.8-old"));
        var recent = Directory.CreateDirectory(Path.Combine(paths.Staging, "0.1.0-rc.8-recent"));
        old.LastWriteTimeUtc = DateTime.UtcNow - TimeSpan.FromDays(2);

        new UpdatePackageStager(new ZipFileSource("unused"), paths).CleanObsolete();

        Assert.False(old.Exists && Directory.Exists(old.FullName));
        Assert.True(Directory.Exists(recent.FullName));
    }
}
