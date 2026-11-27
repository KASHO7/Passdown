using System.Text;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.PdfOutput;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Desktop;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Domain.Weather;
using Carowinds.Passdown.Ui.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 37F: the PDF Weather Outlook in the Passdown's real document paths (preview, Print, Save PDF, Create PDF).
/// One immutable outlook is captured per generation from the monitor's cached snapshot, for the selected date's own
/// hours; nothing here requests weather, subscribes to the monitor, or changes drafts. Synthetic data only.
/// </summary>
public class Phase37FPassdownWeatherIntegrationTests
{
    /// <summary>Saturday, October 17, 2026, 9:02 AM EDT.</summary>
    private static readonly DateTimeOffset Now = WeatherUiFixtures.Morning;

    private static readonly DateOnly Today = WeatherUiFixtures.Day;

    private static EffectiveHours Hours(int open, int close) =>
        ParkHoursCalculator.Calculate(false, new TimeOnly(open, 0), new TimeOnly(close, 0), false, null, null, false, null, null);

    private static AssociateWorkspace WorkspaceFor(DateOnly date) =>
        FakeAssociatePassdownService.DefaultWorkspace(date) with
        {
            ParkHours = new AssociateParkHoursView(Hours(12, 20), AssociateVerificationInfo.NotCheckedYet, OfficialIsClosed: false, OfficialOpening: new TimeOnly(12, 0), OfficialClosing: new TimeOnly(20, 0)),
        };

    /// <summary>Hourly forecast from 9 AM EDT today for <paramref name="hours"/> hours at a constant temperature.</summary>
    private static WeatherSnapshot Snapshot(int temperature, int hours = 72, DateTimeOffset? retrievedAt = null)
    {
        var start = new DateTimeOffset(2026, 10, 17, 13, 0, 0, TimeSpan.Zero);
        var periods = Enumerable.Range(0, hours)
            .Select(i => new HourlyForecastPeriod(start.AddHours(i), start.AddHours(i + 1), true, temperature, 10, null, null, null, null, "Partly Cloudy"))
            .ToArray();
        var at = retrievedAt ?? Now.AddMinutes(-20);
        return WeatherUiFixtures.Normal() with
        {
            HourlyStatus = WeatherUiFixtures.Fresh(at),
            Hourly = new HourlyWeatherForecast("GSP", 116, 59, new DateTimeOffset(2026, 10, 17, 10, 13, 0, TimeSpan.Zero), periods),
        };
    }

    /// <summary>The real export service, recording which outlook each generation received.</summary>
    private sealed class RecordingExportService : IAssociatePassdownPdfExportService
    {
        private readonly AssociatePassdownPdfExportService _inner = new(new AssociatePassdownPdfGenerator());

        public List<(DateOnly Date, PdfWeatherOutlook? Weather)> Calls { get; } = [];

        /// <summary>Runs inside each generation, after the request was captured and before the PDF is composed.</summary>
        public Action? DuringGeneration { get; set; }

        public AssociatePassdownPdfResult GeneratePdf(AssociateWorkspace workspace, DateTimeOffset generatedAt, AssociatePassdownTemplate template = AssociatePassdownTemplate.Classic, CancellationToken cancellationToken = default) =>
            GeneratePdf(workspace, generatedAt, template, null, cancellationToken);

        public AssociatePassdownPdfResult GeneratePdf(AssociateWorkspace workspace, DateTimeOffset generatedAt, AssociatePassdownTemplate template, PdfWeatherOutlook? weather, CancellationToken cancellationToken = default)
        {
            Calls.Add((workspace.OperatingDate, weather));
            DuringGeneration?.Invoke();
            return _inner.GeneratePdf(workspace, generatedAt, template, weather, cancellationToken);
        }
    }

    private sealed class Rig
    {
        public FakeAssociatePassdownService Service { get; } = new() { WorkspaceFactory = WorkspaceFor };

        public ManualDebounceScheduler PreviewDebounce { get; } = new();

        public RecordingExportService Export { get; } = new();

        public FakePdfPrintService Printer { get; } = new();

        public FakePdfSaveFilePicker Picker { get; } = new();

        public InlinePreviewWorkScheduler Scheduler { get; } = new();

        public FakeWeatherMonitor Monitor { get; } = new();

        public FakeClock Clock { get; } = new(Now);

        public AssociateViewModel Create(bool withMonitor = true)
        {
            var viewModel = new AssociateViewModel(
                Service,
                Clock,
                autosaveDelay: new ManualDebounceScheduler().Delay,
                pdfExportService: Export,
                pdfSavePicker: Picker,
                templatePreferenceStore: new FakeAssociatePassdownTemplatePreferenceStore(),
                pdfRasterizer: new FakePdfRasterizer(),
                pdfPrintService: Printer,
                pdfPreviewLayoutStore: new InMemoryPdfPreviewLayoutPreferenceStore(),
                pdfPageImageFactory: new FakePdfPageImageFactory(),
                previewWorkScheduler: Scheduler,
                previewDelay: PreviewDebounce.Delay,
                weatherMonitor: withMonitor ? Monitor : null);
            viewModel.Preview.UpdateAvailableWidth(1500);
            viewModel.Preview.UpdateViewport(460, 700, 1.0);
            return viewModel;
        }

        public async Task SettleAsync(AssociateViewModel viewModel)
        {
            PreviewDebounce.ElapseLatest();
            await viewModel.Preview.PendingRefresh.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static async Task<(Rig Rig, AssociateViewModel ViewModel)> LoadedAsync(WeatherSnapshot? snapshot, bool withMonitor = true)
    {
        var rig = new Rig();
        rig.Monitor.Current = snapshot;
        var viewModel = rig.Create(withMonitor);
        await viewModel.PendingLoad;
        await viewModel.PendingTemplatePreferenceLoad;
        await rig.SettleAsync(viewModel);
        return (rig, viewModel);
    }

    private static string Text(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join("\n", document.GetPages().Select(page => page.Text));
    }

    [Fact]
    public async Task Preview_PrintsTheCachedOutlook_ForTheSelectedDatesParkHours()
    {
        var (rig, viewModel) = await LoadedAsync(Snapshot(71));

        var document = viewModel.Preview.CurrentDocument!;
        Assert.Equal(Today, document.Date);
        Assert.Equal(Today, document.Weather!.OperatingDate);
        Assert.Equal("71°F", document.Weather.TemperatureText);
        var text = Text(document.Bytes);
        Assert.Contains("WEATHER", text, StringComparison.Ordinal);
        Assert.Contains("71°F", text, StringComparison.Ordinal);
        Assert.Contains("NWS", text, StringComparison.Ordinal);
        Assert.Empty(rig.Service.SaveCalls);
    }

    [Fact]
    public async Task NoMonitor_NoSnapshot_StaleOrOffline_PrintNoWeather_AndStillGenerate()
    {
        var offlineStatus = new WeatherPartStatus(WeatherAvailability.Unavailable, null, Now, WeatherFailureKind.Network);
        var offline = new WeatherSnapshot(Now, true, offlineStatus, null, offlineStatus, null, offlineStatus, null, offlineStatus, []);

        foreach (var (snapshot, withMonitor) in new (WeatherSnapshot?, bool)[] { (Snapshot(71), false), (null, true), (Snapshot(71, retrievedAt: Now.AddHours(-4)), true), (offline, true) })
        {
            var (_, viewModel) = await LoadedAsync(snapshot, withMonitor);

            var document = viewModel.Preview.CurrentDocument!;
            Assert.Null(document.Weather);
            Assert.DoesNotContain("NWS", Text(document.Bytes), StringComparison.Ordinal);
            Assert.Equal(PdfPreviewStatus.Ready, viewModel.Preview.Status);
        }
    }

    [Fact]
    public async Task WeatherUpdate_WithNoEdit_Print_RegeneratesAndPrintsTheNewOutlook()
    {
        var (rig, viewModel) = await LoadedAsync(Snapshot(71));
        var generations = rig.Export.Calls.Count;

        rig.Monitor.Current = Snapshot(64);
        await viewModel.Preview.PrintCommand.ExecuteAsync(null);

        Assert.Equal(generations + 1, rig.Export.Calls.Count);
        var printed = Assert.Single(rig.Printer.Requests);
        Assert.Contains("64°F", Text(printed.PdfBytes), StringComparison.Ordinal);
        Assert.Same(printed.PdfBytes, viewModel.Preview.CurrentDocument!.Bytes); // the preview shows what was printed
    }

    [Fact]
    public async Task UnchangedWeather_PrintAndSave_ReuseThePreviewBytes()
    {
        var (rig, viewModel) = await LoadedAsync(Snapshot(71));
        var generations = rig.Export.Calls.Count;
        var previewBytes = viewModel.Preview.CurrentDocument!.Bytes;

        rig.Monitor.Current = Snapshot(71); // a refresh that changed nothing printable
        await viewModel.Preview.PrintCommand.ExecuteAsync(null);
        await viewModel.Preview.SavePdfCommand.ExecuteAsync(null);

        Assert.Equal(generations, rig.Export.Calls.Count);
        Assert.Same(previewBytes, rig.Printer.Requests.Single().PdfBytes);
        Assert.Same(previewBytes, rig.Picker.Calls.Single().Bytes);
    }

    [Fact]
    public async Task WeatherRefreshDuringGeneration_CannotChangeThatDocument()
    {
        var (rig, viewModel) = await LoadedAsync(Snapshot(71));

        // A weather refresh lands while this generation is composing the document.
        rig.Export.DuringGeneration = () => rig.Monitor.Current = Snapshot(55);
        viewModel.ProjectedAttendanceText = "12000";
        await rig.SettleAsync(viewModel);
        rig.Export.DuringGeneration = null;

        var document = viewModel.Preview.CurrentDocument!;
        Assert.Equal("71°F", document.Weather!.TemperatureText);
        Assert.Contains("71°F", Text(document.Bytes), StringComparison.Ordinal);
        Assert.DoesNotContain("55°F", Text(document.Bytes), StringComparison.Ordinal);

        // The next generation captures the refreshed forecast.
        await viewModel.Preview.PrintCommand.ExecuteAsync(null);
        Assert.Contains("55°F", Text(rig.Printer.Requests.Single().PdfBytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangingTheDate_UsesThatDatesForecast_AndOmitsDatesOutsideTheForecast()
    {
        var (rig, viewModel) = await LoadedAsync(Snapshot(71, hours: 60));

        viewModel.SelectedDate = Today.AddDays(1);
        await viewModel.PendingLoad;
        await rig.SettleAsync(viewModel);
        Assert.Equal(Today.AddDays(1), viewModel.Preview.CurrentDocument!.Weather!.OperatingDate);

        viewModel.SelectedDate = Today.AddDays(5); // beyond the 60-hour synthetic forecast
        await viewModel.PendingLoad;
        await rig.SettleAsync(viewModel);
        Assert.Null(viewModel.Preview.CurrentDocument!.Weather);
        Assert.DoesNotContain("NWS", Text(viewModel.Preview.CurrentDocument.Bytes), StringComparison.Ordinal);

        viewModel.SelectedDate = Today.AddDays(-1); // a past date never shows a forecast
        await viewModel.PendingLoad;
        await rig.SettleAsync(viewModel);
        Assert.Null(viewModel.Preview.CurrentDocument!.Weather);

        Assert.All(rig.Export.Calls.Where(call => call.Weather is not null), call => Assert.Equal(call.Date, call.Weather!.OperatingDate));
    }

    [Fact]
    public async Task CreatePdf_UsesTheSameCapturedOutlook()
    {
        var (rig, viewModel) = await LoadedAsync(Snapshot(68));

        await viewModel.CreatePdfCommand.ExecuteAsync(null);

        Assert.Equal("68°F", rig.Export.Calls[^1].Weather!.TemperatureText);
        Assert.Contains("68°F", Text(rig.Picker.Calls.Single().Bytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Passdown_NeverRequestsSubscribesToOrStartsWeather()
    {
        var (rig, viewModel) = await LoadedAsync(Snapshot(71));

        viewModel.ProjectedAttendanceText = "15000";
        await rig.SettleAsync(viewModel);
        await viewModel.Preview.PrintCommand.ExecuteAsync(null);

        Assert.Equal(0, rig.Monitor.RefreshNowCount);
        Assert.Equal(0, rig.Monitor.StartCount);
        Assert.Equal(0, rig.Monitor.ChangedSubscriberCount);
    }

    [Fact]
    public void OutlookSource_IsPure_AndFollowsTheWorkspacesOwnDate()
    {
        var snapshot = Snapshot(70);
        var first = PassdownWeatherOutlookSource.For(snapshot, WorkspaceFor(Today), Now);
        var second = PassdownWeatherOutlookSource.For(snapshot, WorkspaceFor(Today), Now);

        Assert.Equal(first, second);
        Assert.Equal(Today, first!.OperatingDate);
        Assert.Null(PassdownWeatherOutlookSource.For(null, WorkspaceFor(Today), Now));
        Assert.Null(PassdownWeatherOutlookSource.For(snapshot, FakeAssociatePassdownService.DefaultWorkspace(Today), Now)); // hours not checked
    }

    [Fact]
    public void Composition_GivesThePassdownTheOneWeatherMonitor()
    {
        // Resolving AssociateViewModel itself would load a date from the real profile, so this checks the two halves:
        // the container registers the one monitor, and the (only) constructor accepts it as an optional dependency,
        // which Microsoft.Extensions.DependencyInjection fills from the registration.
        var provider = App.BuildServiceProvider();
        Assert.Same(provider.GetRequiredService<IWeatherMonitor>(), provider.GetRequiredService<IWeatherMonitor>());

        var constructor = Assert.Single(typeof(AssociateViewModel).GetConstructors());
        var parameter = Assert.Single(constructor.GetParameters(), p => p.ParameterType == typeof(IWeatherMonitor));
        Assert.True(parameter.HasDefaultValue);
    }
}
