using Carowinds.Passdown.Infrastructure.PersonalSync;

namespace Carowinds.Passdown.Infrastructure.Tests.PersonalSync;

/// <summary>Phase 16B §1: a stable, anonymous, per-installation DeviceId.</summary>
public class FileDeviceIdProviderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-device-id-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string PathFor(string fileName) => Path.Combine(_directory, fileName);

    [Fact]
    public void GetOrCreateDeviceId_FirstAccess_CreatesId()
    {
        var provider = new FileDeviceIdProvider(PathFor("device-id.txt"));

        var id = provider.GetOrCreateDeviceId();

        Assert.NotEqual(Guid.Empty, id);
        Assert.True(File.Exists(PathFor("device-id.txt")));
    }

    [Fact]
    public void GetOrCreateDeviceId_SecondCallOnSameProvider_ReturnsSameId()
    {
        var provider = new FileDeviceIdProvider(PathFor("device-id.txt"));

        var first = provider.GetOrCreateDeviceId();
        var second = provider.GetOrCreateDeviceId();

        Assert.Equal(first, second);
    }

    [Fact]
    public void GetOrCreateDeviceId_NewProviderInstance_SamePath_ReturnsPersistedId()
    {
        var path = PathFor("device-id.txt");
        var first = new FileDeviceIdProvider(path).GetOrCreateDeviceId();

        var second = new FileDeviceIdProvider(path).GetOrCreateDeviceId();

        Assert.Equal(first, second);
    }

    [Fact]
    public void GetOrCreateDeviceId_MissingFile_RegeneratesSafely()
    {
        var path = PathFor("device-id.txt");
        Assert.False(File.Exists(path));

        var id = new FileDeviceIdProvider(path).GetOrCreateDeviceId();

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public void GetOrCreateDeviceId_CorruptFile_RegeneratesSafelyAndPersistsTheNewId()
    {
        var path = PathFor("device-id.txt");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "not-a-guid");

        var regenerated = new FileDeviceIdProvider(path).GetOrCreateDeviceId();
        var readBack = new FileDeviceIdProvider(path).GetOrCreateDeviceId();

        Assert.NotEqual(Guid.Empty, regenerated);
        Assert.Equal(regenerated, readBack);
    }

    [Fact]
    public void GetOrCreateDeviceId_TwoIndependentStores_ProduceDifferentIds()
    {
        var first = new FileDeviceIdProvider(PathFor("device-id-a.txt")).GetOrCreateDeviceId();
        var second = new FileDeviceIdProvider(PathFor("device-id-b.txt")).GetOrCreateDeviceId();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void DefaultPath_IsUnderAppPathsLocalApplicationDataDirectory()
    {
        Assert.StartsWith(AppPaths.LocalApplicationDataDirectory, FileDeviceIdProvider.DefaultPath);
    }

    /// <summary>
    /// Phase 18A REL-006: a transient read failure (here: another handle holding the file exclusively,
    /// as an AV/backup scanner can on Windows) must never mint and persist a new identity. Pre-18A this
    /// returned a fresh Guid and overwrote the file.
    /// </summary>
    [Fact]
    public void GetOrCreateDeviceId_ExistingFileTemporarilyUnreadable_ThrowsAndNeverReplacesTheId()
    {
        var path = PathFor("device-id.txt");
        var original = new FileDeviceIdProvider(path).GetOrCreateDeviceId();

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => new FileDeviceIdProvider(path).GetOrCreateDeviceId());
        }

        Assert.Equal(original, new FileDeviceIdProvider(path).GetOrCreateDeviceId());
        Assert.Equal(original.ToString("D"), File.ReadAllText(path).Trim());
    }
}
