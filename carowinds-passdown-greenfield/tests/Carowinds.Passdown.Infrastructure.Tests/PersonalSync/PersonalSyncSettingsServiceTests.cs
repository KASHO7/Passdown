using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Infrastructure.PersonalSync;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.PersonalSync;

/// <summary>
/// Phase 16B §4/§14: the settings screen's status computation — Off/NotConfigured/Ready/
/// FolderUnavailable/ConfigurationError — driven entirely by scripted fakes so the manifest
/// bootstrap's exact trigger condition (spec §12: only when enabled with an already-valid folder)
/// is directly verifiable.
/// </summary>
public class PersonalSyncSettingsServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-settings-service-tests-{Guid.NewGuid():N}");
    private readonly FakeSyncConfigurationStore _configurationStore = new();
    private readonly FakeSyncFolderValidator _folderValidator = new();
    private readonly FakeOneDriveLocationSuggestionProvider _suggestionProvider = new();
    private readonly FakeDeviceIdProvider _deviceIdProvider = new(Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"));
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private PersonalSyncSettingsService CreateService() =>
        new(_configurationStore, _folderValidator, _suggestionProvider, _deviceIdProvider, _clock);

    [Fact]
    public async Task LoadAsync_DisabledConfiguration_ReturnsOff_AndNeverValidatesOrTouchesTheFilesystem()
    {
        _configurationStore.Current = SyncConfiguration.Disabled;

        var snapshot = await CreateService().LoadAsync();

        Assert.Equal(PersonalSyncStatus.Off, snapshot.Status);
        Assert.Equal(0, _folderValidator.ValidateCallCount);
    }

    [Fact]
    public async Task LoadAsync_EnabledWithNoPath_ReturnsNotConfigured()
    {
        _configurationStore.Current = new SyncConfiguration(true, null);

        var snapshot = await CreateService().LoadAsync();

        Assert.Equal(PersonalSyncStatus.NotConfigured, snapshot.Status);
    }

    [Fact]
    public async Task LoadAsync_EnabledWithInvalidFolder_ReturnsConfigurationError()
    {
        _configurationStore.Current = new SyncConfiguration(true, _root);
        _folderValidator.NextResult = SyncFolderValidationResult.Invalid("bad folder");

        var snapshot = await CreateService().LoadAsync();

        Assert.Equal(PersonalSyncStatus.ConfigurationError, snapshot.Status);
        Assert.Equal("bad folder", snapshot.StatusDetail);
    }

    [Fact]
    public async Task LoadAsync_EnabledWithUnavailableFolder_ReturnsFolderUnavailable()
    {
        _configurationStore.Current = new SyncConfiguration(true, _root);
        _folderValidator.NextResult = SyncFolderValidationResult.Unavailable("not writable");

        var snapshot = await CreateService().LoadAsync();

        Assert.Equal(PersonalSyncStatus.FolderUnavailable, snapshot.Status);
    }

    [Fact]
    public async Task LoadAsync_EnabledWithValidFolder_ReturnsReady_AndCreatesManifest()
    {
        _configurationStore.Current = new SyncConfiguration(true, _root);
        _folderValidator.NextResult = SyncFolderValidationResult.Valid();

        var snapshot = await CreateService().LoadAsync();

        Assert.Equal(PersonalSyncStatus.Ready, snapshot.Status);
        Assert.True(File.Exists(SyncPathService.GetManifestFilePath(_root)));
    }

    [Fact]
    public async Task LoadAsync_DisabledConfiguration_NeverCreatesManifest()
    {
        _configurationStore.Current = SyncConfiguration.Disabled;

        await CreateService().LoadAsync();

        Assert.False(Directory.Exists(SyncPathService.GetSyncFolderPath(_root)));
    }

    [Fact]
    public async Task LoadAsync_AlwaysReportsDeviceIdAndSuggestions_RegardlessOfEnabledState()
    {
        _configurationStore.Current = SyncConfiguration.Disabled;
        _suggestionProvider.Roots = ["/Users/associate/OneDrive"];

        var snapshot = await CreateService().LoadAsync();

        Assert.Equal(_deviceIdProvider.FixedId, snapshot.DeviceId);
        Assert.Equal(["/Users/associate/OneDrive"], snapshot.SuggestedRoots);
    }

    [Fact]
    public async Task SetEnabledAsync_PersistsThroughTheConfigurationStore()
    {
        _configurationStore.Current = SyncConfiguration.Disabled;

        await CreateService().SetEnabledAsync(true);

        Assert.True(_configurationStore.Current.PersonalSyncEnabled);
    }

    [Fact]
    public async Task SetSyncRootAsync_PersistsThroughTheConfigurationStore()
    {
        _configurationStore.Current = new SyncConfiguration(true, null);

        await CreateService().SetSyncRootAsync(_root);

        Assert.Equal(_root, _configurationStore.Current.SyncRootPath);
    }

    private sealed class FakeSyncConfigurationStore : ISyncConfigurationStore
    {
        public SyncConfiguration Current { get; set; } = SyncConfiguration.Disabled;

        public Task<SyncConfiguration> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);

        public Task SaveAsync(SyncConfiguration configuration, CancellationToken cancellationToken = default)
        {
            Current = configuration;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSyncFolderValidator : ISyncFolderValidator
    {
        public int ValidateCallCount { get; private set; }

        public SyncFolderValidationResult NextResult { get; set; } = SyncFolderValidationResult.Valid();

        public Task<SyncFolderValidationResult> ValidateAsync(string selectedRootPath, CancellationToken cancellationToken = default)
        {
            ValidateCallCount++;
            return Task.FromResult(NextResult);
        }
    }

    private sealed class FakeOneDriveLocationSuggestionProvider : IOneDriveLocationSuggestionProvider
    {
        public IReadOnlyList<string> Roots { get; set; } = [];

        public IReadOnlyList<string> GetSuggestedRoots() => Roots;
    }

    private sealed class FakeDeviceIdProvider(Guid fixedId) : IDeviceIdProvider
    {
        public Guid FixedId { get; } = fixedId;

        public Guid GetOrCreateDeviceId() => FixedId;
    }
}
