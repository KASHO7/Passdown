using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Infrastructure;
using Carowinds.Passdown.Infrastructure.Accessibility;
using Carowinds.Passdown.Infrastructure.Preferences;

namespace Carowinds.Passdown.Infrastructure.Tests.Preferences;

/// <summary>Phase 35A: the local "Interface animations" file — On only when nothing valid is saved; a saved Off stays Off.</summary>
public class FileInterfaceAnimationPreferenceStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-motion-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string PathFor(string fileName = "interface-animations.json") => Path.Combine(_directory, fileName);

    [Fact]
    public void Load_MissingFile_DefaultsToOn_AndCreatesNothing()
    {
        Assert.Equal(InterfaceAnimationPreference.On, new FileInterfaceAnimationPreferenceStore(PathFor()).Load());
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task SavedOff_StaysOff_AcrossRestarts()
    {
        await new FileInterfaceAnimationPreferenceStore(PathFor()).SaveAsync(InterfaceAnimationPreference.Off);

        Assert.Equal(InterfaceAnimationPreference.Off, new FileInterfaceAnimationPreferenceStore(PathFor()).Load());
        Assert.Equal(InterfaceAnimationPreference.Off, new FileInterfaceAnimationPreferenceStore(PathFor()).Load());
    }

    [Fact]
    public async Task SavedOn_RoundTrips_AndIsWrittenByName()
    {
        await new FileInterfaceAnimationPreferenceStore(PathFor()).SaveAsync(InterfaceAnimationPreference.On);

        Assert.Equal(InterfaceAnimationPreference.On, new FileInterfaceAnimationPreferenceStore(PathFor()).Load());
        Assert.Contains("\"InterfaceAnimations\": \"On\"", await File.ReadAllTextAsync(PathFor()));
    }

    [Theory]
    [InlineData("{ not valid json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{ \"InterfaceAnimations\": \"Sometimes\" }")]
    [InlineData("{ \"InterfaceAnimations\": 1 }")]
    [InlineData("{ }")]
    public void Load_CorruptOrUnrecognized_FallsBackToOn_NeverThrows(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathFor(), content);

        Assert.Equal(InterfaceAnimationPreference.On, new FileInterfaceAnimationPreferenceStore(PathFor()).Load());
        Assert.Equal(content, File.ReadAllText(PathFor())); // the bad file is left alone
    }

    [Fact]
    public async Task Save_RejectsAnUndefinedValue_AndWritesNothing()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new FileInterfaceAnimationPreferenceStore(PathFor()).SaveAsync((InterfaceAnimationPreference)7));
        Assert.False(File.Exists(PathFor()));
    }

    [Fact]
    public async Task Save_TouchesOnlyItsOwnFile_AndLeavesNoTempFile()
    {
        Directory.CreateDirectory(_directory);
        var neighbours = new Dictionary<string, string>
        {
            ["appearance.json"] = "{ \"Appearance\": \"Dark\" }",
            ["sync-config.json"] = "{ \"Enabled\": true }",
            ["passdown.db"] = "synthetic database bytes",
        };
        foreach (var (name, content) in neighbours)
        {
            await File.WriteAllTextAsync(PathFor(name), content);
        }

        var store = new FileInterfaceAnimationPreferenceStore(PathFor());
        await store.SaveAsync(InterfaceAnimationPreference.Off);
        await store.SaveAsync(InterfaceAnimationPreference.On);

        foreach (var (name, content) in neighbours)
        {
            Assert.Equal(content, await File.ReadAllTextAsync(PathFor(name)));
        }

        Assert.Equal(neighbours.Count + 1, Directory.GetFiles(_directory).Length);
    }

    [Fact]
    public void DefaultPath_IsTheLocalProfileFolder_NeverACloudSyncFolder()
    {
        Assert.StartsWith(AppPaths.LocalApplicationDataDirectory, FileInterfaceAnimationPreferenceStore.DefaultPath);
        Assert.Equal(
            Path.GetDirectoryName(FileAppearancePreferenceStore.DefaultPath),
            Path.GetDirectoryName(FileInterfaceAnimationPreferenceStore.DefaultPath));
        Assert.Equal("interface-animations.json", Path.GetFileName(FileInterfaceAnimationPreferenceStore.DefaultPath));
        Assert.DoesNotContain("OneDrive", FileInterfaceAnimationPreferenceStore.DefaultPath, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Phase 35A: Windows "Animation effects" → reduced motion, without calling user32 on the build host.</summary>
public class WindowsAnimationEffectsReaderTests
{
    [Fact]
    public void NonWindows_IsNotDetected_AndNeverCallsTheNativeApi()
    {
        var called = false;
        var reader = new WindowsAnimationEffectsReader(() => false, () => { called = true; return 1; });

        Assert.Equal(SystemReducedMotion.NotDetected, reader.Read());
        Assert.False(called);
    }

    [Theory]
    [InlineData(1, SystemReducedMotion.NotRequested)]
    [InlineData(0, SystemReducedMotion.Requested)]
    public void Windows_MapsAnimationEffectsToReducedMotion(int animationEffects, SystemReducedMotion expected) =>
        Assert.Equal(expected, new WindowsAnimationEffectsReader(() => true, () => animationEffects).Read());

    [Fact]
    public void Windows_FailedCall_IsNotDetected() =>
        Assert.Equal(SystemReducedMotion.NotDetected, new WindowsAnimationEffectsReader(() => true, () => null).Read());

    [Fact]
    public void Windows_MissingEntryPoint_IsNotDetected_NeverThrows() =>
        Assert.Equal(SystemReducedMotion.NotDetected, new WindowsAnimationEffectsReader(() => true, () => throw new EntryPointNotFoundException()).Read());

    [Fact]
    public void DefaultReader_OnThisHost_NeverThrows()
    {
        var result = new WindowsAnimationEffectsReader().Read();
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(SystemReducedMotion.NotDetected, result);
        }
    }
}
