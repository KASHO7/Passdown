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
public partial class Phase37FPassdownWeatherIntegrationTests
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

        public InMemoryPassdownWeatherOutlookPreferenceStore Preferences { get; set; } = new();

        /// <summary>Phase 37D: the shared preference (the composition root's singleton); null keeps a private one over <see cref="Preferences"/>.</summary>
        public PassdownWeatherOutlookSettingsViewModel? Shared { get; set; }

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
                weatherMonitor: withMonitor ? Monitor : null,
                weatherOutlookPreferenceStore: Preferences,
                weatherOutlookPreference: Shared);
            viewModel.Preview.UpdateAvailableWidth(1500);
            viewModel.Preview.UpdateViewport(460, 700, 1.0);
            return viewModel;
        }

        /// <summary>
        /// Elapses pending preview delays (quiet window, page-render debounce) newest first until the refresh in
        /// flight has finished — with real timers they all elapse; a single "latest" can leave the other pending.
        /// </summary>
        public async Task SettleAsync(AssociateViewModel viewModel)
        {
            for (var i = 0; i < 10 && !viewModel.Preview.PendingRefresh.IsCompleted; i++)
            {
                PreviewDebounce.ElapseLatest();
            }

            await viewModel.Preview.PendingRefresh.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static async Task<(Rig Rig, AssociateViewModel ViewModel)> LoadedAsync(WeatherSnapshot? snapshot, bool withMonitor = true, PassdownWeatherOutlookPreferences? saved = null)
    {
        var rig = new Rig();
        if (saved is not null)
        {
            rig.Preferences = new InMemoryPassdownWeatherOutlookPreferenceStore(saved);
        }

        rig.Monitor.Current = snapshot;
        var viewModel = rig.Create(withMonitor);
        await viewModel.PendingLoad;
        await viewModel.PendingTemplatePreferenceLoad;
        await viewModel.PendingWeatherOutlookPreferenceLoad;
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

        // Phase 37F follow-up: the device preference is the local file store, and the constructor accepts it.
        var store = Assert.IsType<Carowinds.Passdown.Infrastructure.Preferences.FilePassdownWeatherOutlookPreferenceStore>(provider.GetRequiredService<IPassdownWeatherOutlookPreferenceStore>());
        Assert.Equal(Carowinds.Passdown.Infrastructure.Preferences.FilePassdownWeatherOutlookPreferenceStore.DefaultPath, store.ConfigFilePath);
        Assert.True(Assert.Single(constructor.GetParameters(), p => p.ParameterType == typeof(IPassdownWeatherOutlookPreferenceStore)).HasDefaultValue);
    }

    // ---------------------------------------------------------------- Phase 37F follow-up: Include Weather Outlook

    [Fact]
    public async Task IncludeWeatherOutlook_IsOnByDefault_WhenNothingIsSaved()
    {
        var (rig, viewModel) = await LoadedAsync(Snapshot(71));

        Assert.True(viewModel.IncludeWeatherOutlook);
        Assert.Equal(0, rig.Preferences.SaveCount); // loading the default writes nothing
        Assert.NotNull(viewModel.Preview.CurrentDocument!.Weather);
    }

    [Fact]
    public async Task TurningItOff_SavesTheDevicePreference_RefreshesThePreviewAtOnce_AndPrintsNoWeather()
    {
        var (rig, viewModel) = await LoadedAsync(Snapshot(71));
        var generations = rig.Export.Calls.Count;
        var leading = viewModel.Preview.LeadingRefreshCount;

        viewModel.IncludeWeatherOutlook = false;

        // Leading edge of the existing scheduling: generated immediately, no added delay or extra pass.
        Assert.Equal(leading + 1, viewModel.Preview.LeadingRefreshCount);
        Assert.Equal(generations + 1, rig.Export.Calls.Count);
        await rig.SettleAsync(viewModel);
        Assert.Equal(generations + 1, rig.Export.Calls.Count);

        await viewModel.PendingWeatherOutlookPreferenceSave;
        Assert.False(rig.Preferences.Current.IncludeWeatherOutlook);
        Assert.Equal(1, rig.Preferences.SaveCount);

        var document = viewModel.Preview.CurrentDocument!;
        Assert.Null(document.Weather);
        Assert.Null(rig.Export.Calls[^1].Weather);
        Assert.DoesNotContain("NWS", Text(document.Bytes), StringComparison.Ordinal);
        Assert.DoesNotContain("WEATHER", Text(document.Bytes), StringComparison.Ordinal);

        Assert.Empty(rig.Service.SaveCalls); // a device preference, never draft data
        Assert.Equal(0, rig.Monitor.RefreshNowCount);
    }

    [Fact]
    public async Task TurningItBackOn_RestoresTheOutlook()
    {
        var (rig, viewModel) = await LoadedAsync(Snapshot(71), saved: new PassdownWeatherOutlookPreferences(false));
        Assert.Null(viewModel.Preview.CurrentDocument!.Weather);

        viewModel.IncludeWeatherOutlook = true;
        await rig.SettleAsync(viewModel);

        Assert.Equal("71°F", viewModel.Preview.CurrentDocument!.Weather!.TemperatureText);
        Assert.True(rig.Preferences.Current.IncludeWeatherOutlook);
    }

    [Fact]
    public async Task SavedOff_IsRestoredAfterRestart_AndTheFirstPreviewHasNoWeather()
    {
        var (rig, viewModel) = await LoadedAsync(Snapshot(71), saved: new PassdownWeatherOutlookPreferences(false));

        Assert.False(viewModel.IncludeWeatherOutlook);
        Assert.Equal(0, rig.Preferences.SaveCount); // restoring the saved value does not write it back
        Assert.Null(viewModel.Preview.CurrentDocument!.Weather);
        Assert.DoesNotContain("NWS", Text(viewModel.Preview.CurrentDocument.Bytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Off_PrintSaveAndCreatePdf_AllMatchThePreview()
    {
        var (rig, viewModel) = await LoadedAsync(Snapshot(71));
        viewModel.IncludeWeatherOutlook = false;
        await rig.SettleAsync(viewModel);
        var generations = rig.Export.Calls.Count;
        var previewBytes = viewModel.Preview.CurrentDocument!.Bytes;

        await viewModel.Preview.PrintCommand.ExecuteAsync(null);
        await viewModel.Preview.SavePdfCommand.ExecuteAsync(null);
        Assert.Equal(generations, rig.Export.Calls.Count); // the previewed bytes are reused
        Assert.Same(previewBytes, rig.Printer.Requests.Single().PdfBytes);
        Assert.Same(previewBytes, rig.Picker.Calls.Single().Bytes);

        await viewModel.CreatePdfCommand.ExecuteAsync(null);
        Assert.Null(rig.Export.Calls[^1].Weather);
        Assert.DoesNotContain("NWS", Text(rig.Picker.Calls[^1].Bytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task On_PrintSaveAndCreatePdf_AllMatchThePreview()
    {
        var (rig, viewModel) = await LoadedAsync(Snapshot(71));
        var previewBytes = viewModel.Preview.CurrentDocument!.Bytes;

        await viewModel.Preview.PrintCommand.ExecuteAsync(null);
        await viewModel.Preview.SavePdfCommand.ExecuteAsync(null);
        await viewModel.CreatePdfCommand.ExecuteAsync(null);

        Assert.Same(previewBytes, rig.Printer.Requests.Single().PdfBytes);
        Assert.Same(previewBytes, rig.Picker.Calls[0].Bytes);
        Assert.Equal(viewModel.Preview.CurrentDocument!.Weather, rig.Export.Calls[^1].Weather);
        Assert.Contains("71°F", Text(rig.Picker.Calls[^1].Bytes), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic, true)]
    [InlineData(AssociatePassdownTemplate.Classic, false)]
    [InlineData(AssociatePassdownTemplate.Modern, true)]
    [InlineData(AssociatePassdownTemplate.Modern, false)]
    [InlineData(AssociatePassdownTemplate.Compact, true)]
    [InlineData(AssociatePassdownTemplate.Compact, false)]
    public async Task EveryTemplate_FollowsTheSetting(AssociatePassdownTemplate template, bool include)
    {
        var (rig, viewModel) = await LoadedAsync(Snapshot(71), saved: new PassdownWeatherOutlookPreferences(include));

        viewModel.SelectTemplateCommand.Execute(template);
        await rig.SettleAsync(viewModel);

        var document = viewModel.Preview.CurrentDocument!;
        Assert.Equal(template, document.Template);
        Assert.Equal(include, Text(document.Bytes).Contains("NWS", StringComparison.Ordinal));
        Assert.Equal(include, document.Weather is not null);
    }

    [Fact]
    public async Task On_WithMissingOrStaleForecast_StillGeneratesWithoutWeather()
    {
        foreach (var snapshot in new[] { null, Snapshot(71, retrievedAt: Now.AddHours(-4)) })
        {
            var (_, viewModel) = await LoadedAsync(snapshot);

            Assert.True(viewModel.IncludeWeatherOutlook);
            Assert.Equal(PdfPreviewStatus.Ready, viewModel.Preview.Status);
            Assert.Null(viewModel.Preview.CurrentDocument!.Weather);
        }
    }

    [Fact]
    public async Task Off_StaysOffAcrossDateChanges()
    {
        var (rig, viewModel) = await LoadedAsync(Snapshot(71, hours: 60));
        viewModel.IncludeWeatherOutlook = false;
        await rig.SettleAsync(viewModel);

        viewModel.SelectedDate = Today.AddDays(1);
        await viewModel.PendingLoad;
        await rig.SettleAsync(viewModel);

        Assert.False(viewModel.IncludeWeatherOutlook);
        Assert.Equal(Today.AddDays(1), viewModel.Preview.CurrentDocument!.Date);
        Assert.Null(viewModel.Preview.CurrentDocument.Weather);

        viewModel.IncludeWeatherOutlook = true;
        await rig.SettleAsync(viewModel);
        Assert.Equal(Today.AddDays(1), viewModel.Preview.CurrentDocument!.Weather!.OperatingDate);
    }

    /// <summary>Counts reads of <see cref="Current"/>; never refreshes.</summary>
    private sealed class CountingWeatherMonitor(WeatherSnapshot snapshot) : IWeatherMonitor
    {
        public int CurrentReads { get; private set; }

        public WeatherSnapshot? Current
        {
            get
            {
                CurrentReads++;
                return snapshot;
            }
        }

        public bool IsRefreshing => false;

        public event EventHandler? Changed { add { } remove { } }

        public void Start() => throw new InvalidOperationException("The Passdown must never start weather.");

        public Task StopAsync() => Task.CompletedTask;

        public Task<WeatherManualRefreshResult> RefreshNowAsync() => throw new InvalidOperationException("The Passdown must never refresh weather.");
    }

    [Fact]
    public async Task Off_DoesNotEvenReadTheWeather_AndNothingRequestsIt()
    {
        var monitor = new CountingWeatherMonitor(Snapshot(71));
        var rig = new Rig { Preferences = new InMemoryPassdownWeatherOutlookPreferenceStore(new PassdownWeatherOutlookPreferences(false)) };
        var viewModel = new AssociateViewModel(
            rig.Service, rig.Clock,
            autosaveDelay: new ManualDebounceScheduler().Delay,
            pdfExportService: rig.Export,
            pdfSavePicker: rig.Picker,
            templatePreferenceStore: new FakeAssociatePassdownTemplatePreferenceStore(),
            pdfRasterizer: new FakePdfRasterizer(),
            pdfPrintService: rig.Printer,
            pdfPreviewLayoutStore: new InMemoryPdfPreviewLayoutPreferenceStore(),
            pdfPageImageFactory: new FakePdfPageImageFactory(),
            previewWorkScheduler: rig.Scheduler,
            previewDelay: rig.PreviewDebounce.Delay,
            weatherMonitor: monitor,
            weatherOutlookPreferenceStore: rig.Preferences);
        viewModel.Preview.UpdateAvailableWidth(1500);
        viewModel.Preview.UpdateViewport(460, 700, 1.0);
        await viewModel.PendingLoad;
        await viewModel.PendingWeatherOutlookPreferenceLoad;
        await rig.SettleAsync(viewModel);

        await viewModel.Preview.PrintCommand.ExecuteAsync(null);
        await viewModel.CreatePdfCommand.ExecuteAsync(null);

        Assert.Equal(0, monitor.CurrentReads);
        Assert.All(rig.Export.Calls, call => Assert.Null(call.Weather));
    }

    [Fact]
    public void Checkbox_IsInIncludeInPassdown_DirectlyBelowShows_AndNotInTheActionsMenu()
    {
        // Compiled bindings verify the path at build time; the live two-way binding, position and keyboard order are
        // checked in a real window (Phase33PreviewWindowTests.WeatherOutlookCheckbox_…, run alone, Light and Dark).
        var markup = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Carowinds.Passdown.Desktop", "Views", "AssociateView.axaml"));
        var includeStart = markup.IndexOf("x:Name=\"IncludeChoices\"", StringComparison.Ordinal);
        var include = markup[includeStart..markup.IndexOf("</WrapPanel>", includeStart, StringComparison.Ordinal)];

        var start = include.IndexOf("<CheckBox x:Name=\"IncludeWeatherOutlookCheckBox\"", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var element = include[start..include.IndexOf("/>", start, StringComparison.Ordinal)];
        Assert.Contains("Content=\"Weather Outlook\"", element, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding IncludeWeatherOutlook}\"", element, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Include Weather Outlook in the Passdown\"", element, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HelpText=", element, StringComparison.Ordinal);

        // Shows, then the one empty non-interactive cell, then Weather Outlook — the last item, two cells after Shows.
        var shows = include.IndexOf("x:Name=\"IncludeShowsCheckBox\"", StringComparison.Ordinal);
        var spacer = include.IndexOf("x:Name=\"IncludeWeatherOutlookSpacer\"", StringComparison.Ordinal);
        Assert.True(shows < spacer && spacer < start);
        var spacerElement = include[spacer..include.IndexOf("/>", spacer, StringComparison.Ordinal)];
        Assert.Contains("Focusable=\"False\"", spacerElement, StringComparison.Ordinal);
        Assert.Contains("IsHitTestVisible=\"False\"", spacerElement, StringComparison.Ordinal);
        Assert.DoesNotContain("<CheckBox", include[(start + 1)..], StringComparison.Ordinal);

        // One control for the preference, and none left in the Actions menu.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(markup, System.Text.RegularExpressions.Regex.Escape("{Binding IncludeWeatherOutlook")));
        Assert.DoesNotContain("IncludeWeatherOutlookMenuItem", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Include Weather Outlook\"", markup, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CarowindsPassdown.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>A preference store whose load finishes only when the test says so (a slow disk at startup).</summary>
    private sealed class HeldPreferenceStore(bool saved) : IPassdownWeatherOutlookPreferenceStore
    {
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<PassdownWeatherOutlookPreferences> LoadAsync(CancellationToken cancellationToken = default)
        {
            await Gate.Task;
            return new PassdownWeatherOutlookPreferences(saved);
        }

        public Task SaveAsync(PassdownWeatherOutlookPreferences preferences, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SlowPreferenceLoad_ThePreviewWaits_ThenUsesTheSavedChoice(bool saved)
    {
        var monitor = new CountingWeatherMonitor(Snapshot(71));
        var store = new HeldPreferenceStore(saved);
        var rig = new Rig();
        var viewModel = new AssociateViewModel(
            rig.Service, rig.Clock,
            autosaveDelay: new ManualDebounceScheduler().Delay,
            pdfExportService: rig.Export,
            pdfSavePicker: rig.Picker,
            templatePreferenceStore: new FakeAssociatePassdownTemplatePreferenceStore(),
            pdfRasterizer: new FakePdfRasterizer(),
            pdfPrintService: rig.Printer,
            pdfPreviewLayoutStore: new InMemoryPdfPreviewLayoutPreferenceStore(),
            pdfPageImageFactory: new FakePdfPageImageFactory(),
            previewWorkScheduler: rig.Scheduler,
            previewDelay: rig.PreviewDebounce.Delay,
            weatherMonitor: monitor,
            weatherOutlookPreferenceStore: store);
        viewModel.Preview.UpdateAvailableWidth(1500);
        viewModel.Preview.UpdateViewport(460, 700, 1.0);
        await viewModel.PendingLoad;
        await rig.SettleAsync(viewModel);

        // The date is loaded but the saved choice is not: nothing was generated and the weather was not read.
        Assert.Empty(rig.Export.Calls);
        Assert.Equal(0, monitor.CurrentReads);
        Assert.Equal(PdfPreviewStatus.Waiting, viewModel.Preview.Status);

        store.Gate.SetResult();
        await viewModel.PendingWeatherOutlookPreferenceLoad.WaitAsync(TimeSpan.FromSeconds(10));
        await rig.SettleAsync(viewModel);

        Assert.Equal(saved, viewModel.IncludeWeatherOutlook);
        Assert.Single(rig.Export.Calls);
        Assert.Equal(saved, viewModel.Preview.CurrentDocument!.Weather is not null);
        Assert.Equal(saved ? 1 : 0, monitor.CurrentReads);
    }
}
