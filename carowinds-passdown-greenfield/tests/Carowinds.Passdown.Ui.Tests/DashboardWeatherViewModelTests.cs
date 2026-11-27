using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 37C: the Weather card view model observes the shared monitor only. Loading or reloading the Dashboard,
/// expanding the card or receiving updates never asks for weather; Refresh Weather is the only request, and its
/// feedback never claims success for a failure.
/// </summary>
public class DashboardWeatherViewModelTests
{
    private readonly FakeWeatherMonitor _monitor = new() { Current = WeatherUiFixtures.Normal() };
    private readonly FakeClock _clock = new(WeatherUiFixtures.Morning);

    private DashboardWeatherViewModel Create(IUiDispatcher? dispatcher = null, IWeatherLinkLauncher? links = null) =>
        new(_monitor, _clock, dispatcher ?? new ImmediateUiDispatcher(), links);

    [Fact]
    public async Task DashboardLoadAndReload_NeverRefreshWeather_AndUseParkHoursForTheRainWindow()
    {
        var weather = Create();
        var service = new FakeOperationsDashboardService { SnapshotFactory = _ => Phase22CDashboardFixtures.VerifiedOperatingDay(WeatherUiFixtures.Day) };
        var dashboard = new OperationsDashboardViewModel(service, uiDispatcher: new ImmediateUiDispatcher(), weather: weather);
        await dashboard.PendingLoad;

        await dashboard.ReloadCommand.ExecuteAsync(null);
        await dashboard.ReloadCommand.ExecuteAsync(null);

        Assert.Same(weather, dashboard.Weather);
        Assert.Equal(0, _monitor.RefreshNowCount);
        Assert.Equal(0, _monitor.StartCount);
        Assert.Equal(1, _monitor.ChangedSubscriberCount);
        Assert.StartsWith("Highest hourly rain chance, 10 AM–10 PM park hours", weather.Presentation.ForecastSourceText, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAWeatherMonitor_TheDashboardHasNoWeatherCard()
    {
        var dashboard = new OperationsDashboardViewModel(new FakeOperationsDashboardService(), uiDispatcher: new ImmediateUiDispatcher());

        Assert.Null(dashboard.Weather);
        Assert.False(dashboard.HasWeather);
    }

    [Fact]
    public void MonitorChanges_RebuildTheCard_CoalescedToOnePendingRebuild()
    {
        var queued = new QueueDispatcher();
        var weather = Create(queued);
        _monitor.Current = WeatherUiFixtures.TwoAlerts();

        _monitor.RaiseChanged();
        _monitor.RaiseChanged();
        _monitor.RaiseChanged();

        Assert.Single(queued.Pending);
        Assert.False(weather.Presentation.HasAlertStrip); // not rebuilt until the UI thread runs it
        queued.RunAll();
        Assert.True(weather.Presentation.HasAlertStrip);

        _monitor.RaiseChanged();
        Assert.Single(queued.Pending); // a later change posts again
        Assert.Equal(0, _monitor.RefreshNowCount);
    }

    [Fact]
    public void Rebuild_AgesTheFreshnessLabels_WithoutAnyRequest()
    {
        var weather = Create();
        Assert.False(weather.Presentation.HeaderIsStale);

        _clock.UtcNow = WeatherUiFixtures.Morning.AddMinutes(30);
        _monitor.Current = WeatherUiFixtures.Normal() with
        {
            AlertsStatus = new WeatherPartStatus(WeatherAvailability.Stale, WeatherUiFixtures.Morning, WeatherUiFixtures.Morning, WeatherFailureKind.None),
        };
        weather.Rebuild();

        Assert.True(weather.Presentation.HeaderIsStale);
        Assert.Equal(0, _monitor.RefreshNowCount);
    }

    [Fact]
    public void Expanding_IsLocalOnly_AndRenamesTheToggle()
    {
        var weather = Create();
        Assert.False(weather.IsExpanded);
        Assert.Equal("Weather details", weather.ToggleText);

        weather.IsExpanded = true;

        Assert.Equal("Hide weather details", weather.ToggleText);
        Assert.Equal(0, _monitor.RefreshNowCount);
    }

    [Fact]
    public async Task RefreshWeather_AsksTheMonitorOnce_AndReportsTheOutcome()
    {
        var weather = Create();

        await weather.RefreshWeatherCommand.ExecuteAsync(null);

        Assert.Equal(1, _monitor.RefreshNowCount);
        Assert.Equal("Weather updated 9:02 AM ET.", weather.RefreshMessage);
        Assert.False(weather.RefreshMessageIsWarning);
    }

    [Fact]
    public async Task RefreshWeather_CannotRunTwiceAtOnce()
    {
        var gate = new TaskCompletionSource<WeatherManualRefreshResult>();
        _monitor.OnRefreshNow = () => gate.Task;
        var weather = Create();

        var first = weather.RefreshWeatherCommand.ExecuteAsync(null);

        Assert.True(weather.IsRefreshing);
        Assert.False(weather.RefreshWeatherCommand.CanExecute(null));
        Assert.Equal("Checking the National Weather Service…", weather.RefreshMessage);

        gate.SetResult(new WeatherManualRefreshResult(WeatherManualRefreshOutcome.Completed));
        await first;
        Assert.True(weather.RefreshWeatherCommand.CanExecute(null));
        Assert.Equal(1, _monitor.RefreshNowCount);
    }

    [Fact]
    public async Task RefreshWeather_Failure_IsNeverReportedAsSuccess()
    {
        _monitor.OnRefreshNow = () => Task.FromResult(new WeatherManualRefreshResult(WeatherManualRefreshOutcome.NetworkUnavailable));
        var weather = Create();

        await weather.RefreshWeatherCommand.ExecuteAsync(null);

        Assert.True(weather.RefreshMessageIsWarning);
        Assert.StartsWith("Could not reach the National Weather Service.", weather.RefreshMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("updated", weather.RefreshMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefreshWeather_ThatThrows_IsReportedAsAnError()
    {
        _monitor.OnRefreshNow = () => Task.FromException<WeatherManualRefreshResult>(new InvalidOperationException("synthetic"));
        var weather = Create();

        await weather.RefreshWeatherCommand.ExecuteAsync(null);

        Assert.True(weather.RefreshMessageIsWarning);
        Assert.False(weather.IsRefreshing);
    }

    [Theory]
    [InlineData(WeatherManualRefreshOutcome.Completed, "Weather updated 9:02 AM ET.", false)]
    [InlineData(WeatherManualRefreshOutcome.PartlyFailed, "Weather partly updated.", true)]
    [InlineData(WeatherManualRefreshOutcome.NetworkUnavailable, "Could not reach the National Weather Service.", true)]
    [InlineData(WeatherManualRefreshOutcome.ProviderError, "The National Weather Service returned an error.", true)]
    [InlineData(WeatherManualRefreshOutcome.AlreadyRefreshing, "Weather is already being refreshed.", false)]
    [InlineData(WeatherManualRefreshOutcome.RecentlyChecked, "Weather was checked recently; nothing new to request yet. Next automatic check 9:07 AM ET.", false)]
    [InlineData(WeatherManualRefreshOutcome.NotPermitted, "Weather refresh is not available right now.", true)]
    [InlineData(WeatherManualRefreshOutcome.Cancelled, "Weather refresh was stopped.", true)]
    public void RefreshMessages_ForEveryOutcome(WeatherManualRefreshOutcome outcome, string expectedStart, bool warning)
    {
        var (message, isWarning) = DashboardWeatherViewModel.Message(
            new WeatherManualRefreshResult(outcome, WeatherUiFixtures.Morning.AddMinutes(5)), WeatherUiFixtures.Morning);

        Assert.StartsWith(expectedStart, message, StringComparison.Ordinal);
        Assert.Equal(warning, isWarning);
    }

    [Fact]
    public async Task OpenLink_GoesThroughTheWeatherLauncher_AndReportsAFailure()
    {
        var links = new RecordingWeatherLinkLauncher { Result = false };
        var weather = Create(links: links);

        await weather.OpenLinkCommand.ExecuteAsync(weather.Presentation.ForecastPage);

        Assert.Equal(weather.Presentation.ForecastPage, Assert.Single(links.Opened));
        Assert.Equal("The weather.gov page could not be opened.", weather.RefreshMessage);
    }

    private sealed class QueueDispatcher : IUiDispatcher
    {
        public List<Action> Pending { get; } = [];

        public void Post(Action action) => Pending.Add(action);

        public void RunAll()
        {
            var actions = Pending.ToArray();
            Pending.Clear();
            foreach (var action in actions)
            {
                action();
            }
        }
    }

    private sealed class RecordingWeatherLinkLauncher : IWeatherLinkLauncher
    {
        public List<Uri> Opened { get; } = [];

        public bool Result { get; set; } = true;

        public Task<bool> OpenAsync(Uri weatherPage, CancellationToken cancellationToken = default)
        {
            Opened.Add(weatherPage);
            return Task.FromResult(Result);
        }
    }
}
