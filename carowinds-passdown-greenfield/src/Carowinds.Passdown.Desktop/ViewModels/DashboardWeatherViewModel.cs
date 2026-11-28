using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// Phase 37C: the Dashboard Weather card (owner-approved D4 mock-up). One instance for the app's lifetime, owned
/// by <see cref="OperationsDashboardViewModel"/>. It only observes the shared <see cref="IWeatherMonitor"/>: showing
/// the Dashboard, navigating, or expanding the card never makes a request. The one request it can start is Refresh
/// Weather in the expanded details (decision D6), which goes through the monitor's single flight and minimum
/// intervals. Expansion is in-memory only, collapsed at start, never saved or synced.
/// </summary>
public partial class DashboardWeatherViewModel : ViewModelBase
{
    private readonly IWeatherMonitor _monitor;
    private readonly IClock _clock;
    private readonly IUiDispatcher _dispatcher;
    private readonly IWeatherLinkLauncher? _linkLauncher;
    private DashboardParkStatusSummary? _parkStatus;
    private DateOnly? _operatingDate;
    private int _rebuildPosted;
    private bool _manualInFlight;

    public DashboardWeatherViewModel(IWeatherMonitor monitor, IClock clock, IUiDispatcher? uiDispatcher = null, IWeatherLinkLauncher? linkLauncher = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(clock);
        _monitor = monitor;
        _clock = clock;
        _dispatcher = uiDispatcher ?? new AvaloniaUiDispatcher();
        _linkLauncher = linkLauncher;
        Presentation = DashboardWeatherPresentation.Build(monitor.Current, null, null, clock.UtcNow);
        IsRefreshing = monitor.IsRefreshing;
        _monitor.Changed += OnMonitorChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlertRows))]
    public partial DashboardWeatherPresentation Presentation { get; private set; }

    /// <summary>The expanded details' alerts, each with its own weather.gov command (rebuilt with <see cref="Presentation"/>).</summary>
    public IReadOnlyList<DashboardWeatherAlertRow> AlertRows =>
        Presentation.Alerts.Select(item => new DashboardWeatherAlertRow(item, OpenLinkCommand)).ToArray();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleText))]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshWeatherCommand))]
    public partial bool IsRefreshing { get; private set; }

    /// <summary>The result of the last Refresh Weather press, in words (never "updated" for a failure).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRefreshMessage))]
    public partial string? RefreshMessage { get; private set; }

    [ObservableProperty]
    public partial bool RefreshMessageIsWarning { get; private set; }

    public bool HasRefreshMessage => !string.IsNullOrEmpty(RefreshMessage);

    public string ToggleText => IsExpanded ? "Hide weather details" : "Weather details";

    /// <summary>Today's park hours and operating date from the Dashboard snapshot (for the rain-chance window).</summary>
    public void UpdateParkHours(OperationsDashboardSnapshot? snapshot)
    {
        _parkStatus = snapshot?.ParkStatus;
        _operatingDate = snapshot?.OperatingDate;
        Rebuild();
    }

    /// <summary>Rebuilds the card from the monitor's latest snapshot and the current time (UI thread).</summary>
    public void Rebuild()
    {
        Interlocked.Exchange(ref _rebuildPosted, 0);
        IsRefreshing = _manualInFlight || _monitor.IsRefreshing;
        Presentation = DashboardWeatherPresentation.Build(_monitor.Current, _parkStatus, _operatingDate, _clock.UtcNow);
    }

    [RelayCommand(CanExecute = nameof(CanRefreshWeather))]
    private async Task RefreshWeatherAsync()
    {
        RefreshMessageIsWarning = false;
        RefreshMessage = "Checking the National Weather Service…";
        _manualInFlight = true;
        IsRefreshing = true;
        WeatherManualRefreshResult result;
        try
        {
            result = await _monitor.RefreshNowAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            result = new WeatherManualRefreshResult(WeatherManualRefreshOutcome.ProviderError);
        }
        finally
        {
            _manualInFlight = false;
        }

        Rebuild();
        (RefreshMessage, RefreshMessageIsWarning) = Message(result, _clock.UtcNow);
    }

    private bool CanRefreshWeather() => !IsRefreshing;

    [RelayCommand]
    private async Task OpenLinkAsync(Uri? page)
    {
        if (page is null || _linkLauncher is null)
        {
            return;
        }

        if (!await _linkLauncher.OpenAsync(page).ConfigureAwait(true))
        {
            RefreshMessageIsWarning = true;
            RefreshMessage = "The weather.gov page could not be opened.";
        }
    }

    /// <summary>The words for each Refresh Weather outcome.</summary>
    public static (string Message, bool IsWarning) Message(WeatherManualRefreshResult result, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Outcome switch
        {
            WeatherManualRefreshOutcome.Completed => ($"Weather updated {EasternTimestampFormat.Time(nowUtc)}.", false),
            WeatherManualRefreshOutcome.PartlyFailed => ("Weather partly updated. Some NWS information could not be refreshed; the last data received is shown for it.", true),
            WeatherManualRefreshOutcome.NetworkUnavailable => ("Could not reach the National Weather Service. Check the network connection. The last weather received is still shown.", true),
            WeatherManualRefreshOutcome.ProviderError => ("The National Weather Service returned an error. The last weather received is still shown.", true),
            WeatherManualRefreshOutcome.AlreadyRefreshing => ("Weather is already being refreshed.", false),
            WeatherManualRefreshOutcome.RecentlyChecked => (result.NextCheckAt is { } next && next > nowUtc
                ? $"Weather was checked recently; nothing new to request yet. Next automatic check {EasternTimestampFormat.Time(next)}."
                : "Weather was checked recently; nothing new to request yet.", false),
            WeatherManualRefreshOutcome.NotPermitted => ("Weather refresh is not available right now.", true),
            WeatherManualRefreshOutcome.Cancelled => ("Weather refresh was stopped.", true),
            _ => ("Weather refresh finished.", false),
        };
    }

    private void OnMonitorChanged(object? sender, EventArgs e)
    {
        // Coalesced: at most one rebuild waits on the UI thread however often the monitor reports.
        if (Interlocked.Exchange(ref _rebuildPosted, 1) == 0)
        {
            _dispatcher.Post(Rebuild);
        }
    }
}

/// <summary>One alert in the expanded Weather details, with the command that opens its NWS page (through the weather allowlist).</summary>
public sealed class DashboardWeatherAlertRow(DashboardWeatherAlertItem item, IAsyncRelayCommand<Uri?> openLink)
{
    public DashboardWeatherAlertItem Item { get; } = item;

    public IRelayCommand OpenCommand { get; } = new AsyncRelayCommand(() => openLink.ExecuteAsync(item.Link));
}
