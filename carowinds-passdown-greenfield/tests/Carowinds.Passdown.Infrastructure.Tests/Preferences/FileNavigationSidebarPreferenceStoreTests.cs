using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Infrastructure;
using Carowinds.Passdown.Infrastructure.Preferences;

namespace Carowinds.Passdown.Infrastructure.Tests.Preferences;

/// <summary>Phase 35A: the local sidebar state file — Expanded unless a valid Collapsed is saved.</summary>
public class FileNavigationSidebarPreferenceStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-sidebar-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string PathFor(string name = "navigation-sidebar.json") => Path.Combine(_directory, name);

    [Fact]
    public void Load_MissingFile_IsExpanded_AndCreatesNothing()
    {
        Assert.Equal(NavigationSidebarState.Expanded, new FileNavigationSidebarPreferenceStore(PathFor()).Load());
        Assert.False(Directory.Exists(_directory));
    }

    [Theory]
    [InlineData(NavigationSidebarState.Collapsed)]
    [InlineData(NavigationSidebarState.Expanded)]
    public async Task Save_ThenLoad_RoundTrips_ByName(NavigationSidebarState state)
    {
        await new FileNavigationSidebarPreferenceStore(PathFor()).SaveAsync(state);

        Assert.Equal(state, new FileNavigationSidebarPreferenceStore(PathFor()).Load());
        Assert.Contains($"\"Sidebar\": \"{state}\"", await File.ReadAllTextAsync(PathFor()));
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{ \"Sidebar\": \"Hidden\" }")]
    [InlineData("{ \"Sidebar\": 1 }")]
    public void Load_CorruptOrUnrecognized_IsExpanded_NeverThrows(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathFor(), content);

        Assert.Equal(NavigationSidebarState.Expanded, new FileNavigationSidebarPreferenceStore(PathFor()).Load());
    }

    [Fact]
    public async Task Save_RejectsAnUndefinedValue()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new FileNavigationSidebarPreferenceStore(PathFor()).SaveAsync((NavigationSidebarState)5));
        Assert.False(File.Exists(PathFor()));
    }

    [Fact]
    public void DefaultPath_IsTheLocalProfileFolder_NeverACloudSyncFolder()
    {
        Assert.Equal(
            Path.GetDirectoryName(FileAppearancePreferenceStore.DefaultPath),
            Path.GetDirectoryName(FileNavigationSidebarPreferenceStore.DefaultPath));
        Assert.StartsWith(AppPaths.LocalApplicationDataDirectory, FileNavigationSidebarPreferenceStore.DefaultPath);
        Assert.Equal("navigation-sidebar.json", Path.GetFileName(FileNavigationSidebarPreferenceStore.DefaultPath));
        Assert.DoesNotContain("OneDrive", FileNavigationSidebarPreferenceStore.DefaultPath, StringComparison.OrdinalIgnoreCase);
    }
}
