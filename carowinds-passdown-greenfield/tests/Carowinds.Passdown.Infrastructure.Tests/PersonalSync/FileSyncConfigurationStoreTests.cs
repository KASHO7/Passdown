using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Infrastructure.PersonalSync;

namespace Carowinds.Passdown.Infrastructure.Tests.PersonalSync;

/// <summary>Phase 16B §2: persisted local Personal Sync configuration — disabled is always the safe fallback.</summary>
public class FileSyncConfigurationStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-sync-config-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string PathFor(string fileName) => Path.Combine(_directory, fileName);

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsDisabled()
    {
        var store = new FileSyncConfigurationStore(PathFor("sync-config.json"));

        var configuration = await store.LoadAsync();

        Assert.Equal(SyncConfiguration.Disabled, configuration);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTrips()
    {
        var store = new FileSyncConfigurationStore(PathFor("sync-config.json"));
        var saved = new SyncConfiguration(true, "/Users/associate/OneDrive");

        await store.SaveAsync(saved);
        var loaded = await store.LoadAsync();

        Assert.Equal(saved, loaded);
    }

    [Fact]
    public async Task LoadAsync_MalformedFile_ReturnsDisabled_NeverThrows()
    {
        var path = PathFor("sync-config.json");
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(path, "{ not valid json");

        var configuration = await new FileSyncConfigurationStore(path).LoadAsync();

        Assert.Equal(SyncConfiguration.Disabled, configuration);
    }

    [Fact]
    public async Task SaveAsync_DoesNotStoreAnyCredentialLikeField()
    {
        var store = new FileSyncConfigurationStore(PathFor("sync-config.json"));
        await store.SaveAsync(new SyncConfiguration(true, "/Users/associate/OneDrive"));

        var json = await File.ReadAllTextAsync(PathFor("sync-config.json"));

        Assert.DoesNotContain("Token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Credential", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DefaultPath_IsUnderAppPathsLocalApplicationDataDirectory()
    {
        Assert.StartsWith(AppPaths.LocalApplicationDataDirectory, FileSyncConfigurationStore.DefaultPath);
    }
}
