using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Infrastructure.PersonalSync;
using Carowinds.Passdown.Infrastructure.Preferences;

namespace Carowinds.Passdown.Infrastructure.Tests.Preferences;

/// <summary>Phase 37F follow-up: the local "Include Weather Outlook" device preference — on unless explicitly saved off.</summary>
public class FilePassdownWeatherOutlookPreferenceStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"carowinds-weather-outlook-preference-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string FilePath => Path.Combine(_directory, "passdown-weather-outlook.json");

    [Fact]
    public async Task MissingFile_IsIncluded()
    {
        Assert.True((await new FilePassdownWeatherOutlookPreferenceStore(FilePath).LoadAsync()).IncludeWeatherOutlook);
        Assert.False(File.Exists(FilePath)); // loading never writes
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavedChoice_SurvivesARestart(bool include)
    {
        await new FilePassdownWeatherOutlookPreferenceStore(FilePath).SaveAsync(new PassdownWeatherOutlookPreferences(include));

        var reloaded = await new FilePassdownWeatherOutlookPreferenceStore(FilePath).LoadAsync(); // a new instance, as after restart

        Assert.Equal(include, reloaded.IncludeWeatherOutlook);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"IncludeWeatherOutlook\": \"maybe\"}")]
    [InlineData("null")]
    public async Task UnreadableOrMissingValue_FallsBackToIncluded(string content)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, content);

        Assert.True((await new FilePassdownWeatherOutlookPreferenceStore(FilePath).LoadAsync()).IncludeWeatherOutlook);
    }

    [Fact]
    public async Task FileHoldsOnlyTheChoice()
    {
        await new FilePassdownWeatherOutlookPreferenceStore(FilePath).SaveAsync(new PassdownWeatherOutlookPreferences(false));

        var json = await File.ReadAllTextAsync(FilePath);
        Assert.Contains("\"IncludeWeatherOutlook\": false", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Weather\":", json.Replace("IncludeWeatherOutlook\":", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultPath_IsTheLocalApplicationDataFolder_NotTheSyncFolder()
    {
        Assert.Equal("passdown-weather-outlook.json", Path.GetFileName(FilePassdownWeatherOutlookPreferenceStore.DefaultPath));
        Assert.Equal(Path.GetFullPath(AppPaths.LocalApplicationDataDirectory), Path.GetDirectoryName(Path.GetFullPath(FilePassdownWeatherOutlookPreferenceStore.DefaultPath)));
    }
}
