using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Domain.Weather;

namespace Carowinds.Passdown.Application.Tests.Weather;

/// <summary>
/// Phase 37C: the manual (Refresh Weather) mode, NC/SC alert coverage in the snapshot, and the one
/// <see cref="WeatherMonitor"/> that activates the Phase 37B coordinator for the interactive app.
/// </summary>
public class WeatherMonitorTests
{
    private readonly FakeClock _clock = new(WeatherTestData.Start);
    private readonly InMemoryWeatherCacheStore _store = new();
    private readonly FakeWeatherProvider _provider;

    public WeatherMonitorTests() => _provider = FakeWeatherProvider.AllSucceed(() => _clock.UtcNow);

    private WeatherRefreshCoordinator Coordinator() => new(_provider.AsProvider(), _store, _clock, WeatherTestData.VerifiedLocation);

    // ---- Manual mode: minimum intervals and backoff, not cache expiry ----

    [Fact]
    public async Task ManualRefresh_WithinTheAlertFloor_RequestsNothing()
    {
        var coordinator = Coordinator();
        await coordinator.RefreshDueAsync();
        _provider.Calls.Clear();

        _clock.UtcNow = WeatherTestData.Start.AddSeconds(59);
        var report = await coordinator.RefreshAsync(WeatherRefreshMode.Manual);

        Assert.Equal(0, report.RequestCount);
        Assert.Empty(_provider.Calls);
    }

    [Fact]
    public async Task ManualRefresh_AfterTheAlertFloor_AsksForAlertsOnly_WhileScheduledAsksForNothing()
    {
        var coordinator = Coordinator();
        await coordinator.RefreshDueAsync();
        _provider.Calls.Clear();
        _clock.UtcNow = WeatherTestData.Start.AddMinutes(2);

        Assert.Equal(0, (await coordinator.RefreshAsync(WeatherRefreshMode.Scheduled)).RequestCount);

        var report = await coordinator.RefreshAsync(WeatherRefreshMode.Manual);

        Assert.Equal(["alerts:test-a", "alerts:test-b"], _provider.Calls);
        Assert.Equal(2, report.RequestCount);
    }

    [Fact]
    public async Task ManualRefresh_AfterThirtyMinutes_AlsoAsksForTheForecasts_ButNeverThePointsOrStations()
    {
        var coordinator = Coordinator();
        await coordinator.RefreshDueAsync();
        _provider.Calls.Clear();
        _clock.UtcNow = WeatherTestData.Start.AddMinutes(30);

        await coordinator.RefreshAsync(WeatherRefreshMode.Manual);

        Assert.Contains("forecast", _provider.Calls);
        Assert.Contains("hourly", _provider.Calls);
        Assert.DoesNotContain(_provider.Calls, call => call.StartsWith("points:", StringComparison.Ordinal) || call == "stations");
    }

    [Fact]
    public async Task ManualRefresh_RespectsFailureBackoff()
    {
        var coordinator = Coordinator();
        await coordinator.RefreshDueAsync();
        _clock.UtcNow = WeatherTestData.Start.AddMinutes(2);
        _provider.Alerts = (_, _) => FakeWeatherProvider.Fail<IReadOnlyList<WeatherAlert>>(WeatherFailureKind.Network);
        await coordinator.RefreshAsync(WeatherRefreshMode.Manual); // fails → 30 s backoff
        _provider.Calls.Clear();

        _clock.UtcNow = WeatherTestData.Start.AddMinutes(2).AddSeconds(20);
        Assert.Equal(0, (await coordinator.RefreshAsync(WeatherRefreshMode.Manual)).RequestCount);

        _clock.UtcNow = WeatherTestData.Start.AddMinutes(2).AddSeconds(31);
        Assert.Equal(2, (await coordinator.RefreshAsync(WeatherRefreshMode.Manual)).RequestCount);
    }

    [Fact]
    public async Task ManualRefresh_DoesNotAskForAnObservationTheStationCannotHaveReplacedYet()
    {
        var coordinator = Coordinator();
        await coordinator.RefreshDueAsync(); // observation is 10 min old; stations report again at 20 min
        _provider.Calls.Clear();
        _clock.UtcNow = WeatherTestData.Start.AddMinutes(5);

        await coordinator.RefreshAsync(WeatherRefreshMode.Manual);

        Assert.DoesNotContain(_provider.Calls, call => call.StartsWith("observation:", StringComparison.Ordinal));
    }

    [Fact]
    public void EarliestAt_IsTheFloorOrBackoff_AndUncheckedIsImmediate()
    {
        var policy = new WeatherRefreshPolicy();
        var checkedAt = WeatherTestData.Start;

        Assert.Equal(DateTimeOffset.MinValue, policy.Alerts.EarliestAt(new WeatherEndpointState<IReadOnlyList<WeatherAlert>>()));
        Assert.Equal(checkedAt.AddSeconds(60), policy.Alerts.EarliestAt(new WeatherEndpointState<IReadOnlyList<WeatherAlert>> { CheckedAt = checkedAt, ExpiresAt = checkedAt.AddHours(1) }));
        Assert.Equal(checkedAt.AddMinutes(10), policy.Alerts.EarliestAt(new WeatherEndpointState<IReadOnlyList<WeatherAlert>> { CheckedAt = checkedAt, NextAttemptNotBefore = checkedAt.AddMinutes(10) }));
    }

    // ---- Alert coverage: which portion of the park each alert applies to ----

    [Fact]
    public async Task AlertCoverage_ListsThePointsWhoseQueryReturnedTheAlert()
    {
        var start = WeatherTestData.Start;
        var both = WeatherTestData.Alert("both", start.AddHours(-1), start.AddHours(4));
        var scOnly = WeatherTestData.Alert("sc-only", start.AddHours(-1), start.AddHours(4)) with { Event = "Special Weather Statement" };
        _provider.Alerts = (point, _) => FakeWeatherProvider.Ok<IReadOnlyList<WeatherAlert>>(
            point.Id == "test-a" ? [both] : [both, scOnly], start.AddMinutes(5));
        var coordinator = Coordinator();

        await coordinator.RefreshDueAsync();
        var snapshot = await coordinator.GetSnapshotAsync();

        Assert.Equal(2, snapshot.ActiveAlerts.Count);
        Assert.Equal(["test-a", "test-b"], snapshot.AlertCoverage["both"]);
        Assert.Equal(["test-b"], snapshot.AlertCoverage["sc-only"]);
    }

    [Fact]
    public async Task AlertCoverage_AnUpdateKeepsThePointsOfTheMessageItReplaced()
    {
        var start = WeatherTestData.Start;
        var original = WeatherTestData.Alert("v1", start.AddHours(-2), start.AddHours(4));
        var update = WeatherTestData.Alert("v2", start.AddHours(-1), start.AddHours(6)) with { MessageType = "Update", ReferenceIds = ["v1"] };
        _provider.Alerts = (point, _) => FakeWeatherProvider.Ok<IReadOnlyList<WeatherAlert>>(
            point.Id == "test-a" ? [original] : [update], start.AddMinutes(5));
        var coordinator = Coordinator();

        await coordinator.RefreshDueAsync();
        var snapshot = await coordinator.GetSnapshotAsync();

        var active = Assert.Single(snapshot.ActiveAlerts);
        Assert.Equal("v2", active.Id);
        Assert.Equal(["test-a", "test-b"], snapshot.AlertCoverage["v2"]);
    }

    [Fact]
    public async Task AlertCoverage_HasNoEntryForAnExpiredAlert()
    {
        var start = WeatherTestData.Start;
        _provider.Alerts = (_, _) => FakeWeatherProvider.Ok<IReadOnlyList<WeatherAlert>>(
            [WeatherTestData.Alert("old", start.AddHours(-3), start.AddMinutes(-1))], start.AddMinutes(5));
        var coordinator = Coordinator();

        await coordinator.RefreshDueAsync();
        var snapshot = await coordinator.GetSnapshotAsync();

        Assert.Empty(snapshot.ActiveAlerts);
        Assert.Empty(snapshot.AlertCoverage);
    }

    // ---- Classification for Refresh Weather feedback ----

    [Theory]
    [InlineData(2, new WeatherFailureKind[0], WeatherManualRefreshOutcome.Completed)]
    [InlineData(2, new[] { WeatherFailureKind.Network }, WeatherManualRefreshOutcome.PartlyFailed)]
    [InlineData(2, new[] { WeatherFailureKind.Network, WeatherFailureKind.Timeout }, WeatherManualRefreshOutcome.NetworkUnavailable)]
    [InlineData(2, new[] { WeatherFailureKind.Network, WeatherFailureKind.ProviderError }, WeatherManualRefreshOutcome.ProviderError)]
    [InlineData(1, new[] { WeatherFailureKind.RateLimited }, WeatherManualRefreshOutcome.ProviderError)]
    public void Classify_NeverReportsSuccessForAFailure(int requests, WeatherFailureKind[] failures, WeatherManualRefreshOutcome expected)
    {
        var report = new WeatherRefreshReport(true, requests, false, failures.Select((kind, i) => new WeatherEndpointFailure($"e{i}", kind)).ToArray());

        Assert.Equal(expected, WeatherMonitor.Classify(report));
    }

    // ---- The monitor ----

    [Fact]
    public async Task Start_PublishesTheCachedSnapshot_BeforeAnyRequest()
    {
        await Coordinator().RefreshDueAsync(); // fills the store
        _provider.Calls.Clear();
        var delays = new ControlledDelay();
        var monitor = new WeatherMonitor(Coordinator(), _clock, delayAsync: delays.DelayAsync);
        var changed = new TaskCompletionSource();
        monitor.Changed += (_, _) => changed.TrySetResult();

        monitor.Start();
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await delays.WaitForRequestAsync(1);

        Assert.NotNull(monitor.Current);
        Assert.Equal("KCLT", monitor.Current!.Observation!.StationId);
        Assert.Empty(_provider.Calls); // still waiting out the startup delay
        Assert.Equal(WeatherMonitor.StartupDelay, delays.Requested[0]);

        await monitor.StopAsync();
    }

    [Fact]
    public async Task Loop_RefreshesAfterTheStartupDelay_ThenSleepsUntilTheNextDueEndpoint()
    {
        var delays = new ControlledDelay();
        var monitor = new WeatherMonitor(Coordinator(), _clock, delayAsync: delays.DelayAsync);

        monitor.Start();
        await delays.WaitForRequestAsync(1);
        delays.ReleaseNext();
        await delays.WaitForRequestAsync(2);

        Assert.Equal(8, _provider.Calls.Count);
        Assert.Equal(WeatherMonitor.MaxWait, delays.Requested[1]); // alerts due in 5 min → wakes within a minute to age labels
        Assert.Equal(WeatherAvailability.Fresh, monitor.Current!.AlertsStatus.Availability);
        Assert.False(monitor.IsRefreshing);

        await monitor.StopAsync();
        Assert.True(monitor.Loop.IsCompleted);
    }

    [Fact]
    public async Task Loop_NeverSleepsLessThanTheMinimum_WhenSomethingIsOverdue()
    {
        _provider.Alerts = (_, _) => FakeWeatherProvider.Fail<IReadOnlyList<WeatherAlert>>(WeatherFailureKind.Network);
        var delays = new ControlledDelay();
        var monitor = new WeatherMonitor(Coordinator(), _clock, delayAsync: delays.DelayAsync);

        monitor.Start();
        await delays.WaitForRequestAsync(1);
        delays.ReleaseNext();
        await delays.WaitForRequestAsync(2);

        Assert.True(delays.Requested[1] >= WeatherMonitor.MinWait);
        await monitor.StopAsync();
    }

    [Fact]
    public async Task RefreshNow_BeforeStart_IsNotPermitted_AndMakesNoRequest()
    {
        var monitor = new WeatherMonitor(Coordinator(), _clock);

        var result = await monitor.RefreshNowAsync();

        Assert.Equal(WeatherManualRefreshOutcome.NotPermitted, result.Outcome);
        Assert.Empty(_provider.Calls);
    }

    [Fact]
    public async Task RefreshNow_WhileARefreshRuns_ReportsAlreadyRefreshing_AndStartsNoSecondRefresh()
    {
        var delays = new ControlledDelay();
        var gate = new TaskCompletionSource<WeatherFetchResult<WeatherPointMetadata>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _provider.Point = _ => gate.Task;
        var monitor = new WeatherMonitor(Coordinator(), _clock, delayAsync: delays.DelayAsync);
        monitor.Start();
        await delays.WaitForRequestAsync(1);

        var first = monitor.RefreshNowAsync();
        await WaitUntilAsync(() => monitor.IsRefreshing);
        var second = await monitor.RefreshNowAsync();

        Assert.Equal(WeatherManualRefreshOutcome.AlreadyRefreshing, second.Outcome);
        Assert.Equal(1, _provider.Calls.Count(call => call == "points:test-a"));

        gate.SetResult(WeatherFetchResult<WeatherPointMetadata>.Retrieved(WeatherTestData.Grid(), null, null, _clock.UtcNow.AddHours(24)));
        Assert.Equal(WeatherManualRefreshOutcome.Completed, (await first.WaitAsync(TimeSpan.FromSeconds(5))).Outcome);
        await monitor.StopAsync();
    }

    [Fact]
    public async Task RefreshNow_RightAfterARefresh_ReportsRecentlyChecked_WithTheNextCheck()
    {
        var delays = new ControlledDelay();
        var monitor = new WeatherMonitor(Coordinator(), _clock, delayAsync: delays.DelayAsync);
        monitor.Start();
        await delays.WaitForRequestAsync(1);
        Assert.Equal(WeatherManualRefreshOutcome.Completed, (await monitor.RefreshNowAsync()).Outcome);

        var again = await monitor.RefreshNowAsync();

        Assert.Equal(WeatherManualRefreshOutcome.RecentlyChecked, again.Outcome);
        Assert.Equal(WeatherTestData.Start.AddMinutes(5), again.NextCheckAt);
        await monitor.StopAsync();
    }

    [Fact]
    public async Task RefreshNow_WhenEveryRequestFailsOnTheNetwork_ReportsNetworkUnavailable_AndKeepsCachedValues()
    {
        await Coordinator().RefreshDueAsync();
        var delays = new ControlledDelay();
        var monitor = new WeatherMonitor(Coordinator(), _clock, delayAsync: delays.DelayAsync);
        monitor.Start();
        await delays.WaitForRequestAsync(1);
        _clock.UtcNow = WeatherTestData.Start.AddMinutes(2);
        _provider.Alerts = (_, _) => FakeWeatherProvider.Fail<IReadOnlyList<WeatherAlert>>(WeatherFailureKind.Timeout);

        var result = await monitor.RefreshNowAsync();

        Assert.Equal(WeatherManualRefreshOutcome.NetworkUnavailable, result.Outcome);
        Assert.NotNull(monitor.Current!.Forecast);
        Assert.True(monitor.Current.AlertsStatus.LastRefreshFailed);
        await monitor.StopAsync();
    }

    [Fact]
    public async Task StopAsync_CancelsAnInFlightRequest_AndLaterRefreshNowIsNotPermitted()
    {
        var delays = new ControlledDelay();
        _provider.Point = _ => new TaskCompletionSource<WeatherFetchResult<WeatherPointMetadata>>().Task; // never answers
        var cancellable = new CancellationAwareProvider(_provider.AsProvider());
        var monitor = new WeatherMonitor(new WeatherRefreshCoordinator(cancellable, _store, _clock, WeatherTestData.VerifiedLocation), _clock, delayAsync: delays.DelayAsync);
        monitor.Start();
        await delays.WaitForRequestAsync(1);
        delays.ReleaseNext();
        await WaitUntilAsync(() => monitor.IsRefreshing);

        await monitor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(monitor.Loop.IsCompleted);
        Assert.Equal(WeatherManualRefreshOutcome.NotPermitted, (await monitor.RefreshNowAsync()).Outcome);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Start_IsIdempotent()
    {
        var delays = new ControlledDelay();
        var monitor = new WeatherMonitor(Coordinator(), _clock, delayAsync: delays.DelayAsync);

        monitor.Start();
        var loop = monitor.Loop;
        monitor.Start();

        Assert.Same(loop, monitor.Loop);
        await monitor.StopAsync();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition not met.");
            }

            await Task.Delay(5);
        }
    }

    /// <summary>A delay the test releases by hand; records every requested duration.</summary>
    private sealed class ControlledDelay
    {
        private readonly Lock _gate = new();
        private readonly Queue<TaskCompletionSource> _pending = new();

        public List<TimeSpan> Requested { get; } = [];

        public Task DelayAsync(TimeSpan duration, CancellationToken token)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                Requested.Add(duration);
                _pending.Enqueue(tcs);
            }

            token.Register(() => tcs.TrySetCanceled(token));
            return tcs.Task;
        }

        public void ReleaseNext()
        {
            lock (_gate)
            {
                _pending.Dequeue().TrySetResult();
            }
        }

        public Task WaitForRequestAsync(int count) => WaitUntilAsync(() =>
        {
            lock (_gate)
            {
                return Requested.Count >= count;
            }
        });
    }

    /// <summary>Makes a never-answering fake observe cancellation, like the real transport.</summary>
    private sealed class CancellationAwareProvider(IWeatherProvider inner) : IWeatherProvider
    {
        public Task<WeatherFetchResult<WeatherPointMetadata>> GetPointAsync(WeatherReferencePoint point, CancellationToken cancellationToken) =>
            inner.GetPointAsync(point, cancellationToken).WaitAsync(cancellationToken);

        public Task<WeatherFetchResult<WeatherForecast>> GetForecastAsync(WeatherPointMetadata point, WeatherConditionalHeaders conditional, CancellationToken cancellationToken) =>
            inner.GetForecastAsync(point, conditional, cancellationToken).WaitAsync(cancellationToken);

        public Task<WeatherFetchResult<HourlyWeatherForecast>> GetHourlyForecastAsync(WeatherPointMetadata point, WeatherConditionalHeaders conditional, CancellationToken cancellationToken) =>
            inner.GetHourlyForecastAsync(point, conditional, cancellationToken).WaitAsync(cancellationToken);

        public Task<WeatherFetchResult<IReadOnlyList<WeatherStation>>> GetStationsAsync(WeatherPointMetadata point, CancellationToken cancellationToken) =>
            inner.GetStationsAsync(point, cancellationToken).WaitAsync(cancellationToken);

        public Task<WeatherFetchResult<WeatherObservation>> GetLatestObservationAsync(WeatherStation station, WeatherReferencePoint distanceFrom, WeatherConditionalHeaders conditional, CancellationToken cancellationToken) =>
            inner.GetLatestObservationAsync(station, distanceFrom, conditional, cancellationToken).WaitAsync(cancellationToken);

        public Task<WeatherFetchResult<IReadOnlyList<WeatherAlert>>> GetActiveAlertsAsync(WeatherReferencePoint point, WeatherConditionalHeaders conditional, CancellationToken cancellationToken) =>
            inner.GetActiveAlertsAsync(point, conditional, cancellationToken).WaitAsync(cancellationToken);
    }
}
