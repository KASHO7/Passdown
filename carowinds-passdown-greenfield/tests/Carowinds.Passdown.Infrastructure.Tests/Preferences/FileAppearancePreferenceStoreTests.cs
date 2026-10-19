using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Infrastructure;
using Carowinds.Passdown.Infrastructure.Preferences;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Infrastructure.Tests.Preferences;

/// <summary>Phase 23C: the local appearance preference file — System is always the safe fallback, and nothing else is written.</summary>
public class FileAppearancePreferenceStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-appearance-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string PathFor(string fileName = "appearance.json") => Path.Combine(_directory, fileName);

    private string WriteRaw(string content)
    {
        Directory.CreateDirectory(_directory);
        var path = PathFor();
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Load_MissingFile_ReturnsSystem()
    {
        Assert.Equal(AppearancePreference.System, new FileAppearancePreferenceStore(PathFor()).Load());
        Assert.False(Directory.Exists(_directory)); // loading never creates anything
    }

    [Theory]
    [InlineData(AppearancePreference.System)]
    [InlineData(AppearancePreference.Light)]
    [InlineData(AppearancePreference.Dark)]
    public async Task Save_ThenLoad_RoundTrips_AcrossNewInstances(AppearancePreference preference)
    {
        await new FileAppearancePreferenceStore(PathFor()).SaveAsync(preference);

        Assert.Equal(preference, new FileAppearancePreferenceStore(PathFor()).Load()); // a restart reads it back
    }

    [Fact]
    public async Task Save_WritesTheStableNameNotADisplayLabel()
    {
        await new FileAppearancePreferenceStore(PathFor()).SaveAsync(AppearancePreference.Dark);

        var json = await File.ReadAllTextAsync(PathFor());
        Assert.Contains("\"Appearance\": \"Dark\"", json);
        Assert.DoesNotContain("Windows", json);
    }

    [Theory]
    [InlineData("{ not valid json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{ \"Appearance\": \"Blue\" }")]
    [InlineData("{ \"Appearance\": \"Use Windows setting\" }")]
    [InlineData("{ \"Appearance\": 2 }")]
    [InlineData("{ \"Appearance\": 7 }")]
    [InlineData("{ \"Appearance\": null }")]
    [InlineData("{ }")]
    [InlineData("{ \"Theme\": \"Dark\" }")]
    public void Load_MissingCorruptOrUnrecognizedValue_ReturnsSystem_NeverThrows(string content)
    {
        var path = WriteRaw(content);

        Assert.Equal(AppearancePreference.System, new FileAppearancePreferenceStore(path).Load());
        Assert.Equal(content, File.ReadAllText(path)); // the bad file is left alone, not "repaired"
    }

    [Fact]
    public void Load_InvalidValue_LogsOneNonSensitiveWarning()
    {
        var path = WriteRaw("{ \"Appearance\": \"Blue\" }");
        var logger = new ListLogger();

        new FileAppearancePreferenceStore(path, logger).Load();

        var message = Assert.Single(logger.Messages);
        Assert.Contains("using System", message);
        Assert.DoesNotContain(_directory, message);
        Assert.DoesNotContain("Blue", message);
    }

    [Fact]
    public void Load_ValidOrMissingFile_LogsNothing()
    {
        var logger = new ListLogger();
        new FileAppearancePreferenceStore(PathFor(), logger).Load();
        WriteRaw("{ \"Appearance\": \"Light\" }");
        Assert.Equal(AppearancePreference.Light, new FileAppearancePreferenceStore(PathFor(), logger).Load());
        Assert.Empty(logger.Messages);
    }

    [Fact]
    public async Task Save_RejectsAnUndefinedValue_AndWritesNothing()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new FileAppearancePreferenceStore(PathFor()).SaveAsync((AppearancePreference)9));
        Assert.False(File.Exists(PathFor()));
    }

    [Fact]
    public async Task Save_TouchesOnlyTheAppearanceFile()
    {
        Directory.CreateDirectory(_directory);
        var neighbours = new Dictionary<string, string>
        {
            ["associate-passdown-template.json"] = "{ \"Template\": \"Compact\" }",
            ["sync-config.json"] = "{ \"Enabled\": true, \"SyncRootPath\": \"C:\\\\Users\\\\x\\\\OneDrive\\\\Passdown\" }",
            ["device-id.txt"] = "00000000-0000-0000-0000-000000000001",
            ["passdown.db"] = "synthetic database bytes",
        };
        foreach (var (name, content) in neighbours)
        {
            await File.WriteAllTextAsync(PathFor(name), content);
        }

        var stamps = neighbours.Keys.ToDictionary(n => n, n => File.GetLastWriteTimeUtc(PathFor(n)));
        var store = new FileAppearancePreferenceStore(PathFor());
        await store.SaveAsync(AppearancePreference.Dark);
        await store.SaveAsync(AppearancePreference.Light);

        foreach (var (name, content) in neighbours)
        {
            Assert.Equal(content, await File.ReadAllTextAsync(PathFor(name)));
            Assert.Equal(stamps[name], File.GetLastWriteTimeUtc(PathFor(name)));
        }

        Assert.Equal(neighbours.Count + 1, Directory.GetFiles(_directory).Length); // no temp file left behind
    }

    [Fact]
    public async Task IndependentProfiles_KeepTheirOwnChoice()
    {
        var first = new FileAppearancePreferenceStore(Path.Combine(_directory, "pc-a", "appearance.json"));
        var second = new FileAppearancePreferenceStore(Path.Combine(_directory, "pc-b", "appearance.json"));
        var third = new FileAppearancePreferenceStore(Path.Combine(_directory, "pc-c", "appearance.json"));

        await first.SaveAsync(AppearancePreference.Dark);
        await second.SaveAsync(AppearancePreference.Light);

        Assert.Equal(AppearancePreference.Dark, first.Load());
        Assert.Equal(AppearancePreference.Light, second.Load());
        Assert.Equal(AppearancePreference.System, third.Load());
    }

    [Fact]
    public void DefaultPath_IsTheExistingLocalProfileFolder_BesideTheTemplatePreference()
    {
        Assert.StartsWith(AppPaths.LocalApplicationDataDirectory, FileAppearancePreferenceStore.DefaultPath);
        Assert.Equal(
            Path.GetDirectoryName(FileAssociatePassdownTemplatePreferenceStore.DefaultPath),
            Path.GetDirectoryName(FileAppearancePreferenceStore.DefaultPath));
        Assert.Equal("appearance.json", Path.GetFileName(FileAppearancePreferenceStore.DefaultPath));
        Assert.DoesNotContain("OneDrive", FileAppearancePreferenceStore.DefaultPath, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ListLogger : ILogger<FileAppearancePreferenceStore>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
