using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.Weather;

/// <summary>
/// Phase 37B boundaries, checked mechanically against source text: weather is not activated (nothing in the
/// Desktop app composes or calls it), and weather stays out of Personal Sync, drafts, History and PDF
/// documents (spec §6.6). Activation belongs to Phase 37C and requires the D4 mock-up approval and the D8
/// location verification; update these checks then, deliberately.
/// </summary>
public class WeatherActivationGuardTests
{
    private static IEnumerable<string> SourceFiles(string relativeDirectory) =>
        Directory.EnumerateFiles(RepositoryFiles.PathOf(relativeDirectory), "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".axaml", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string[] FilesMentioningWeather(string relativeDirectory) =>
        SourceFiles(relativeDirectory)
            .Where(path => File.ReadAllText(path).Contains("Weather", StringComparison.Ordinal))
            .Select(RepositoryFiles.RelativeOf)
            .ToArray();

    [Fact]
    public void DesktopApp_DoesNotComposeOrUseWeather() =>
        Assert.Empty(FilesMentioningWeather("src/Carowinds.Passdown.Desktop"));

    [Fact]
    public void Updater_AndDocuments_DoNotUseWeather()
    {
        Assert.Empty(FilesMentioningWeather("src/Carowinds.Passdown.Updater"));
        Assert.Empty(FilesMentioningWeather("src/Carowinds.Passdown.Documents"));
    }

    [Theory]
    [InlineData("src/Carowinds.Passdown.Application/PersonalSync")]
    [InlineData("src/Carowinds.Passdown.Infrastructure/PersonalSync")]
    [InlineData("src/Carowinds.Passdown.Application/Associate")]
    [InlineData("src/Carowinds.Passdown.Application/Finalization")]
    [InlineData("src/Carowinds.Passdown.Application/History")]
    [InlineData("src/Carowinds.Passdown.Application/PdfOutput")]
    [InlineData("src/Carowinds.Passdown.Infrastructure/Storage")]
    public void SyncDraftHistoryPdfAndSqlite_DoNotCarryWeather(string relativeDirectory) =>
        Assert.Empty(FilesMentioningWeather(relativeDirectory));

    [Fact]
    public void WeatherInfrastructure_UsesNoSqliteSyncOrOfficialRetrievalCode()
    {
        // The cache is a plain local file, and the NWS transport is separate from the official-data transport.
        var weatherFiles = SourceFiles("src/Carowinds.Passdown.Infrastructure/Weather").Select(File.ReadAllText).ToArray();

        Assert.DoesNotContain(weatherFiles, text => text.Contains("Microsoft.Data.Sqlite", StringComparison.Ordinal));
        Assert.DoesNotContain(weatherFiles, text => text.Contains("using Carowinds.Passdown.Infrastructure.Storage", StringComparison.Ordinal));
        Assert.DoesNotContain(weatherFiles, text => text.Contains("using Carowinds.Passdown.Infrastructure.Retrieval", StringComparison.Ordinal));
        Assert.DoesNotContain(weatherFiles, text => text.Contains("SyncFolder", StringComparison.Ordinal));
    }
}
