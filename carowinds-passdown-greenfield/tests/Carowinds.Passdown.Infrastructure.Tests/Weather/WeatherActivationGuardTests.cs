using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.Weather;

/// <summary>
/// Weather boundaries, checked mechanically against source text (spec §6.6). Phase 37B kept weather out of the
/// Desktop app entirely; Phase 37C (D4 mock-up approved, D8 verified) activated it deliberately for the Dashboard
/// card only, so Desktop may mention weather only in the composition root and the card's own files listed below —
/// no Calendar weather (Phase 37D is not authorized), nothing in Associate Passdown, History, scheduled refresh or
/// Personal Sync. Weather stays out of Personal Sync, drafts, History and PDF documents.
/// Phase 37F (owner-approved compact PDF Weather Outlook, no alerts) deliberately extends this to the Passdown PDF:
/// the Documents files and the three Desktop capture files listed below may carry the outlook, which is read from the
/// monitor's cached snapshot only; finalized snapshots, drafts, sync and History still carry no weather.
/// Phase 37D (owner-authorized Calendar Weather and Settings integration) extends the Desktop list once more: the
/// Calendar's selected-date Weather group (it only reads the monitor's cached snapshot and listens for changes) and
/// the one shared Include Weather Outlook preference shown in Settings → Passdown Defaults.
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
        "src/Carowinds.Passdown.Desktop/Converters/WeatherIconGeometryConverter.cs",
        "src/Carowinds.Passdown.Desktop/Presentation/CalendarWeatherPresentation.cs",
        "src/Carowinds.Passdown.Desktop/Presentation/DashboardWeatherPresentation.cs",
        "src/Carowinds.Passdown.Desktop/Presentation/PassdownWeatherOutlookSource.cs",
        "src/Carowinds.Passdown.Desktop/Services/IWeatherLinkLauncher.cs",
        "src/Carowinds.Passdown.Desktop/ViewModels/AssociateViewModel.cs",
        "src/Carowinds.Passdown.Desktop/ViewModels/DashboardWeatherViewModel.cs",
        "src/Carowinds.Passdown.Desktop/ViewModels/OperationsCalendarViewModel.cs",
        "src/Carowinds.Passdown.Desktop/ViewModels/OperationsDashboardViewModel.cs",
        "src/Carowinds.Passdown.Desktop/ViewModels/PassdownPdfPreviewViewModel.cs",
        "src/Carowinds.Passdown.Desktop/ViewModels/PassdownWeatherOutlookSettingsViewModel.cs",
        "src/Carowinds.Passdown.Desktop/ViewModels/PersonalSyncSettingsViewModel.cs",
        "src/Carowinds.Passdown.Desktop/Views/AssociateView.axaml",
        "src/Carowinds.Passdown.Desktop/Views/DashboardView.axaml",
        "src/Carowinds.Passdown.Desktop/Views/OperationsCalendarView.axaml",
        "src/Carowinds.Passdown.Desktop/Views/PersonalSyncSettingsView.axaml",
    ];

    /// <summary>Phase 37F: the only Documents files that may carry the PDF Weather Outlook.</summary>
    private static readonly string[] DocumentsWeatherFiles =
    [
        "src/Carowinds.Passdown.Documents/Associate/AssociatePassdownDocumentModel.cs",
        "src/Carowinds.Passdown.Documents/Associate/AssociatePassdownDocumentModelBuilder.cs",
        "src/Carowinds.Passdown.Documents/Associate/IAssociatePassdownPdfExportService.cs",
        "src/Carowinds.Passdown.Documents/Associate/PdfWeatherOutlook.cs",
        "src/Carowinds.Passdown.Documents/Associate/Templates/ClassicAssociatePassdownTemplateRenderer.cs",
        "src/Carowinds.Passdown.Documents/Associate/Templates/CompactAssociatePassdownTemplateRenderer.cs",
        "src/Carowinds.Passdown.Documents/Associate/Templates/ModernAssociatePassdownTemplateRenderer.cs",
        "src/Carowinds.Passdown.Documents/Associate/Templates/PdfWeatherOutlookComponent.cs",
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
    public void Updater_DoesNotUseWeather() =>
        Assert.Empty(FilesMentioningWeather("src/Carowinds.Passdown.Updater"));

    [Fact]
    public void Documents_UsesWeatherOnlyForThePdfOutlook_NeverInFinalizedSnapshots() =>
        Assert.Equal(DocumentsWeatherFiles, FilesMentioningWeather("src/Carowinds.Passdown.Documents").Select(path => path.Replace('\\', '/')).Order(StringComparer.Ordinal));

    [Fact]
    public void PdfWeatherOutlook_CarriesNoAlerts_AndCannotRetrieveOrRefresh()
    {
        // Phase 37F: alerts stay on the Dashboard; the PDF reads an immutable cached snapshot and requests nothing.
        foreach (var file in DocumentsWeatherFiles.Append("src/Carowinds.Passdown.Desktop/Presentation/PassdownWeatherOutlookSource.cs"))
        {
            var text = RepositoryFiles.ReadText(file);
            foreach (var forbidden in new[] { "WeatherAlert", "ActiveAlerts", "AlertCoverage", "AlertsStatus", "IWeatherMonitor", "IWeatherProvider", "WeatherRefreshCoordinator", "HttpClient", "RefreshNowAsync" })
            {
                Assert.DoesNotContain(forbidden, text, StringComparison.Ordinal);
            }
        }

        Assert.DoesNotContain("Weather", RepositoryFiles.ReadText("src/Carowinds.Passdown.Documents/Associate/FinalizedPassdownSnapshotBuilder.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("Weather", RepositoryFiles.ReadText("src/Carowinds.Passdown.Documents/Associate/AssociateTeamsTextFormatter.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Passdown_OnlyReadsTheMonitorsCurrentSnapshot()
    {
        // No subscription, no refresh, no start: the Passdown never causes a weather request (spec §7.1, §22.13).
        var text = RepositoryFiles.ReadText("src/Carowinds.Passdown.Desktop/ViewModels/AssociateViewModel.cs");
        var uses = System.Text.RegularExpressions.Regex.Matches(text, @"_weatherMonitor[^\s;,)]*").Select(match => match.Value).Distinct().Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(["_weatherMonitor", "_weatherMonitor?.Current"], uses);
        Assert.DoesNotContain("IWeatherMonitor", RepositoryFiles.ReadText("src/Carowinds.Passdown.Desktop/ViewModels/PassdownPdfPreviewViewModel.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void CalendarWeather_OnlyReadsAndObservesTheMonitor_AndNeverRequestsWeather()
    {
        // Phase 37D: selecting dates or months reads the cached snapshot; the monitor alone owns refreshes (§22.13).
        var calendar = RepositoryFiles.ReadText("src/Carowinds.Passdown.Desktop/ViewModels/OperationsCalendarViewModel.cs");
        var uses = System.Text.RegularExpressions.Regex.Matches(calendar, @"(_weatherMonitor|weatherMonitor)\.[A-Za-z]+").Select(match => match.Value).Distinct().Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["_weatherMonitor.Current", "weatherMonitor.Changed"], uses);
        foreach (var file in new[] { "src/Carowinds.Passdown.Desktop/ViewModels/OperationsCalendarViewModel.cs", "src/Carowinds.Passdown.Desktop/Presentation/CalendarWeatherPresentation.cs" })
        {
            var text = RepositoryFiles.ReadText(file);
            foreach (var forbidden in new[] { "RefreshNowAsync", ".Start()", "IWeatherProvider", "WeatherRefreshCoordinator", "HttpClient", "IExternalLinkLauncher", "IOfficialDataRefreshCoordinator" })
            {
                Assert.DoesNotContain(forbidden, text, StringComparison.Ordinal);
            }
        }

        // The Settings preference and the icon converter hold no weather data or retrieval at all.
        foreach (var file in new[] { "src/Carowinds.Passdown.Desktop/ViewModels/PassdownWeatherOutlookSettingsViewModel.cs", "src/Carowinds.Passdown.Desktop/ViewModels/PersonalSyncSettingsViewModel.cs", "src/Carowinds.Passdown.Desktop/Converters/WeatherIconGeometryConverter.cs" })
        {
            var text = RepositoryFiles.ReadText(file);
            foreach (var forbidden in new[] { "IWeatherMonitor", "WeatherSnapshot", "IWeatherProvider", "HttpClient" })
            {
                Assert.DoesNotContain(forbidden, text, StringComparison.Ordinal);
            }
        }
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
