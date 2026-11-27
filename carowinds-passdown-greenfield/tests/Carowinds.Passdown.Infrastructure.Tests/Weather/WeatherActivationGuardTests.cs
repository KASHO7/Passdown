using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.Weather;

/// <summary>
/// Weather boundaries, checked mechanically against source text (spec §6.6). Phase 37B kept weather out of the
/// Desktop app entirely; Phase 37C (D4 mock-up approved, D8 verified) activated it deliberately for the Dashboard
/// card only, so Desktop may mention weather only in the composition root and the card's own files listed below —
/// no Calendar weather (Phase 37D is not authorized), nothing in Associate Passdown, History, scheduled refresh or
/// Personal Sync. Weather stays out of Personal Sync, drafts, History and PDF documents.
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

    /// <summary>Phase 37C: the only Desktop files that may compose or show weather. Extend only with an approved weather surface.</summary>
    private static readonly string[] DesktopWeatherFiles =
    [
        "src/Carowinds.Passdown.Desktop/App.axaml.cs",
        "src/Carowinds.Passdown.Desktop/Presentation/DashboardWeatherPresentation.cs",
        "src/Carowinds.Passdown.Desktop/Services/IWeatherLinkLauncher.cs",
        "src/Carowinds.Passdown.Desktop/ViewModels/DashboardWeatherViewModel.cs",
        "src/Carowinds.Passdown.Desktop/ViewModels/OperationsDashboardViewModel.cs",
        "src/Carowinds.Passdown.Desktop/Views/DashboardView.axaml",
    ];

    [Fact]
    public void DesktopApp_UsesWeatherOnlyInTheCompositionRootAndTheDashboardCard() =>
        Assert.Equal(DesktopWeatherFiles, FilesMentioningWeather("src/Carowinds.Passdown.Desktop").Select(path => path.Replace('\\', '/')).Order(StringComparer.Ordinal));

    [Fact]
    public void DashboardWeather_NeverUsesTheOfficialDataLinkLauncherOrRefresh()
    {
        // NWS pages open only through the weather allowlist; Check Online keeps its official-data meaning (D6).
        foreach (var file in new[] { "src/Carowinds.Passdown.Desktop/ViewModels/DashboardWeatherViewModel.cs", "src/Carowinds.Passdown.Desktop/Presentation/DashboardWeatherPresentation.cs" })
        {
            var text = RepositoryFiles.ReadText(file);
            Assert.DoesNotContain("IExternalLinkLauncher", text, StringComparison.Ordinal);
            Assert.DoesNotContain("OfficialSourceLinkPolicy", text, StringComparison.Ordinal);
            Assert.DoesNotContain("IOfficialDataRefreshCoordinator", text, StringComparison.Ordinal);
            Assert.DoesNotContain("CheckOnline", text, StringComparison.Ordinal);
        }

        Assert.Contains("WeatherSourceLinkPolicy.TryValidate", RepositoryFiles.ReadText("src/Carowinds.Passdown.Desktop/Services/IWeatherLinkLauncher.cs"), StringComparison.Ordinal);
    }

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
    [InlineData("src/Carowinds.Passdown.Application/Dashboard")]
    [InlineData("src/Carowinds.Passdown.Application/Calendar")]
    [InlineData("src/Carowinds.Passdown.Desktop/ScheduledRefresh")]
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
