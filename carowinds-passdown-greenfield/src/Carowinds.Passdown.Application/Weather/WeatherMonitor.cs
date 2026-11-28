using Carowinds.Passdown.Application.Time;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Application.Weather;

/// <summary>What a Refresh Weather request did (Phase 37C), for honest feedback: success is never reported for a failure.</summary>
public enum WeatherManualRefreshOutcome
{
    /// <summary>Every request it made succeeded (or was confirmed unchanged).</summary>
    Completed,

    /// <summary>Some requests succeeded and some failed; failed parts keep their last-known-good values.</summary>
    PartlyFailed,

    /// <summary>Every request failed with a network error or timeout.</summary>
    NetworkUnavailable,

    /// <summary>Every request failed and at least one failure came from NWS (error status, rate limit, unusable response).</summary>
    ProviderError,

    /// <summary>A refresh (scheduled or manual) was already running; this request joined nothing new.</summary>
    AlreadyRefreshing,

    /// <summary>Everything was checked within its minimum interval (or is waiting out a failure backoff): no request was made.</summary>
    RecentlyChecked,

    /// <summary>Retrieval is not permitted (unverified location) or the monitor is not running.</summary>
    NotPermitted,

    /// <summary>The app was closing.</summary>
    Cancelled,
}

/// <summary>The outcome plus, for <see cref="WeatherManualRefreshOutcome.RecentlyChecked"/>, when the next scheduled check is due.</summary>
public sealed record WeatherManualRefreshResult(WeatherManualRefreshOutcome Outcome, DateTimeOffset? NextCheckAt = null);

/// <summary>
/// Phase 37C: the interactive app's one weather activity. Read-only surfaces (the Dashboard card) observe
/// <see cref="Current"/> and <see cref="Changed"/>; they never call the provider themselves, so navigating to
/// the Dashboard never causes a request.
/// </summary>
public interface IWeatherMonitor
{
    /// <summary>The latest snapshot built from the local cache, or null until the cache has been read once.</summary>
    WeatherSnapshot? Current { get; }

    /// <summary>True while a refresh (scheduled or manual) is running.</summary>
    bool IsRefreshing { get; }

    /// <summary>Raised on a background thread whenever <see cref="Current"/> or <see cref="IsRefreshing"/> changes.</summary>
    event EventHandler? Changed;

    /// <summary>Starts the background loop once; returns immediately. Never called by the headless scheduled-refresh host.</summary>
    void Start();

    /// <summary>Cancels the loop and any refresh in flight, and waits for them to stop.</summary>
    Task StopAsync();

    /// <summary>Refresh Weather: asks for whatever is past its minimum interval. Never runs two refreshes at once.</summary>
    Task<WeatherManualRefreshResult> RefreshNowAsync();
}

/// <summary>
/// The <see cref="IWeatherMonitor"/> over the Phase 37B <see cref="WeatherRefreshCoordinator"/> (one instance for the
/// interactive app's lifetime). The loop reads the cache first, so cached weather appears while offline and before
/// any request; after <see cref="StartupDelay"/> it asks the coordinator for whatever is due (alerts about every five
/// minutes, decision D5; everything else under the provider's cache headers, the policy floors and backoff), then
/// sleeps until the next endpoint is due — waking at least every <see cref="MaxWait"/> to rebuild the snapshot, so
/// freshness labels age even when nothing is requested. Everything runs on the thread pool; nothing waits for it.
/// </summary>
public sealed class WeatherMonitor : IWeatherMonitor
{
    /// <summary>How long after <see cref="Start"/> the first request may be made, so startup work goes first.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);

    /// <summary>The longest the loop sleeps; the snapshot is rebuilt at least this often.</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(1);

    /// <summary>The shortest the loop sleeps between passes, whatever the schedule says (no busy loop).</summary>
    public static readonly TimeSpan MinWait = TimeSpan.FromSeconds(15);

    private readonly WeatherRefreshCoordinator _coordinator;
    private readonly IClock _clock;
    private readonly ILogger<WeatherMonitor>? _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly TimeSpan _startupDelay;
    private readonly Lock _gate = new();
    private CancellationTokenSource? _stop;
    private Task _loop = Task.CompletedTask;
    private Task<WeatherRefreshReport>? _refresh;
    private volatile WeatherSnapshot? _current;
    private volatile bool _isRefreshing;

    public WeatherMonitor(
        WeatherRefreshCoordinator coordinator,
        IClock clock,
        ILogger<WeatherMonitor>? logger = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        TimeSpan? startupDelay = null)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(clock);
        _coordinator = coordinator;
        _clock = clock;
        _logger = logger;
        _delayAsync = delayAsync ?? Task.Delay;
        _startupDelay = startupDelay ?? StartupDelay;
    }

    public WeatherSnapshot? Current => _current;

    public bool IsRefreshing => _isRefreshing;

    public event EventHandler? Changed;

    /// <summary>The background loop, for tests and shutdown diagnostics.</summary>
    public Task Loop => _loop;

    public void Start()
    {
        lock (_gate)
        {
            if (_stop is not null)
            {
                return;
            }

            _stop = new CancellationTokenSource();
            var token = _stop.Token;
            _loop = Task.Run(() => RunLoopAsync(token), CancellationToken.None);
        }
    }

    public async Task StopAsync()
    {
        Task loop;
        Task? refresh;
        lock (_gate)
        {
            if (_stop is null)
            {
                return;
            }

            _stop.Cancel();
            loop = _loop;
            refresh = _refresh;
        }

        try
        {
            await Task.WhenAll(loop, refresh ?? Task.CompletedTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected: the loop and any request in flight observe the cancellation; nothing cached is replaced.
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Weather monitor stopped with an error.");
        }
    }

    public async Task<WeatherManualRefreshResult> RefreshNowAsync()
    {
        CancellationToken token;
        lock (_gate)
        {
            if (_stop is null || _stop.IsCancellationRequested)
            {
                return new WeatherManualRefreshResult(WeatherManualRefreshOutcome.NotPermitted);
            }

            if (_isRefreshing)
            {
                return new WeatherManualRefreshResult(WeatherManualRefreshOutcome.AlreadyRefreshing);
            }

            token = _stop.Token;
        }

        try
        {
            var report = await RunRefreshAsync(WeatherRefreshMode.Manual, token).ConfigureAwait(false);
            if (report is null)
            {
                return new WeatherManualRefreshResult(WeatherManualRefreshOutcome.AlreadyRefreshing);
            }

            if (!report.Permitted)
            {
                return new WeatherManualRefreshResult(WeatherManualRefreshOutcome.NotPermitted);
            }

            if (report.RequestCount == 0)
            {
                var next = await _coordinator.GetNextDueAtAsync(token).ConfigureAwait(false);
                return new WeatherManualRefreshResult(WeatherManualRefreshOutcome.RecentlyChecked, next);
            }

            return new WeatherManualRefreshResult(Classify(report));
        }
        catch (OperationCanceledException)
        {
            return new WeatherManualRefreshResult(WeatherManualRefreshOutcome.Cancelled);
        }
    }

    /// <summary>Maps a refresh's failures to the feedback a person sees.</summary>
    public static WeatherManualRefreshOutcome Classify(WeatherRefreshReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Failures.Count == 0)
        {
            return WeatherManualRefreshOutcome.Completed;
        }

        if (report.Failures.Count < report.RequestCount)
        {
            return WeatherManualRefreshOutcome.PartlyFailed;
        }

        return report.Failures.All(failure => failure.Kind is WeatherFailureKind.Network or WeatherFailureKind.Timeout)
            ? WeatherManualRefreshOutcome.NetworkUnavailable
            : WeatherManualRefreshOutcome.ProviderError;
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        try
        {
            await PublishAsync(token).ConfigureAwait(false);
            await _delayAsync(_startupDelay, token).ConfigureAwait(false);

            while (!token.IsCancellationRequested)
            {
                TimeSpan wait;
                try
                {
                    var next = await _coordinator.GetNextDueAtAsync(token).ConfigureAwait(false);
                    if (next is { } due && due <= _clock.UtcNow)
                    {
                        await RunRefreshAsync(WeatherRefreshMode.Scheduled, token).ConfigureAwait(false);
                        next = await _coordinator.GetNextDueAtAsync(token).ConfigureAwait(false);
                    }

                    await PublishAsync(token).ConfigureAwait(false);
                    wait = Clamp(next is { } n ? n - _clock.UtcNow : MaxWait);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // A defect must never stop weather for the session or reach the UI thread; try again later.
                    _logger?.LogWarning(ex, "Weather refresh pass failed; trying again in {Minutes} min.", MaxWait.TotalMinutes);
                    wait = MaxWait;
                }

                await _delayAsync(wait, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Stopped (app closing).
        }
    }

    /// <summary>Runs one refresh unless one is already running (then returns null without waiting).</summary>
    private async Task<WeatherRefreshReport?> RunRefreshAsync(WeatherRefreshMode mode, CancellationToken token)
    {
        Task<WeatherRefreshReport> refresh;
        lock (_gate)
        {
            if (_isRefreshing)
            {
                return null;
            }

            _isRefreshing = true;
        }

        RaiseChanged();
        try
        {
            refresh = _coordinator.RefreshAsync(mode, token);
            lock (_gate)
            {
                _refresh = refresh;
            }

            var report = await refresh.ConfigureAwait(false);
            if (report.Failures.Count > 0)
            {
                _logger?.LogInformation(
                    "Weather {Mode} refresh: {RequestCount} request(s), {FailureCount} failed ({Failures}).",
                    mode, report.RequestCount, report.Failures.Count, string.Join(", ", report.Failures.Select(failure => $"{failure.Endpoint}={failure.Kind}")));
            }

            return report;
        }
        finally
        {
            lock (_gate)
            {
                _isRefreshing = false;
            }

            try
            {
                await PublishAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Weather snapshot could not be rebuilt after a refresh.");
                RaiseChanged();
            }
        }
    }

    private async Task PublishAsync(CancellationToken token)
    {
        _current = await _coordinator.GetSnapshotAsync(token).ConfigureAwait(false);
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "A weather change listener failed.");
        }
    }

    private static TimeSpan Clamp(TimeSpan wait) => wait < MinWait ? MinWait : wait > MaxWait ? MaxWait : wait;
}
