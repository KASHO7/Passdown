using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Domain.Weather;

namespace Carowinds.Passdown.Application.Tests.Weather;

public class WeatherRefreshCoordinatorTests
{
    private readonly FakeClock _clock = new(WeatherTestData.Start);
    private readonly InMemoryWeatherCacheStore _store = new();
    private readonly FakeWeatherProvider _provider;

    public WeatherRefreshCoordinatorTests() => _provider = FakeWeatherProvider.AllSucceed(() => _clock.UtcNow);

    private WeatherRefreshCoordinator Coordinator(WeatherLocation? location = null) =>
        new(_provider.AsProvider(), _store, _clock, location ?? WeatherTestData.VerifiedLocation);

    [Fact]
    public async Task UnverifiedLocation_MakesNoRequest_AndHasNoDueTime()
    {
        var coordinator = Coordinator(WeatherTestData.PendingLocation);

        var report = await coordinator.RefreshDueAsync();

        Assert.False(report.Permitted);
        Assert.Equal(0, report.RequestCount);
        Assert.Empty(_provider.Calls);
        Assert.Equal(0, _store.SaveCount);
        Assert.Null(await coordinator.GetNextDueAtAsync());
        Assert.False((await coordinator.GetSnapshotAsync()).RetrievalPermitted);
    }

    [Fact]
    public async Task FirstRefresh_RequestsEachEndpointOnce_ForBothPoints_AndSavesOnce()
    {
        var coordinator = Coordinator();

        var report = await coordinator.RefreshDueAsync();

        Assert.Equal(
            ["points:test-a", "points:test-b", "stations", "forecast", "hourly", "observation:KCLT", "alerts:test-a", "alerts:test-b"],
            _provider.Calls);
        Assert.Equal(8, report.RequestCount);
        Assert.Empty(report.Failures);
        Assert.Equal(1, _store.SaveCount);

        var snapshot = await coordinator.GetSnapshotAsync();
        Assert.Equal(WeatherAvailability.Fresh, snapshot.ForecastStatus.Availability);
        Assert.Equal(WeatherAvailability.Fresh, snapshot.HourlyStatus.Availability);
        Assert.Equal(WeatherAvailability.Fresh, snapshot.ObservationStatus.Availability);
        Assert.Equal(WeatherAvailability.Fresh, snapshot.AlertsStatus.Availability);
        Assert.Equal("KCLT", snapshot.Observation!.StationId);
    }

    [Fact]
    public async Task ImmediateSecondRefresh_RequestsNothing_AndDoesNotRewriteTheCache()
    {
        var coordinator = Coordinator();
        await coordinator.RefreshDueAsync();
        _provider.Calls.Clear();

        var report = await coordinator.RefreshDueAsync();

        Assert.Equal(0, report.RequestCount);
        Assert.Empty(_provider.Calls);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task AfterFiveMinutes_OnlyAlertsAreDue_ForBothPoints_WithTheirETag()
    {
        var coordinator = Coordinator();
        await coordinator.RefreshDueAsync();
        _provider.Calls.Clear();
        _provider.Conditionals.Clear();

        _clock.UtcNow = WeatherTestData.Start.AddMinutes(4).AddSeconds(59);
        Assert.Equal(0, (await coordinator.RefreshDueAsync()).RequestCount);

        _clock.UtcNow = WeatherTestData.Start.AddMinutes(5);
        var report = await coordinator.RefreshDueAsync();

        Assert.Equal(["alerts:test-a", "alerts:test-b"], _provider.Calls);
        Assert.Equal(2, report.RequestCount);
        Assert.All(_provider.Conditionals, call => Assert.Equal("W/\"alerts-1\"", call.Conditional.ETag));
    }

    [Fact]
    public async Task NextDueAt_IsTheAlertInterval()
    {
        var coordinator = Coordinator();
        await coordinator.RefreshDueAsync();

        Assert.Equal(WeatherTestData.Start.AddMinutes(5), await coordinator.GetNextDueAtAsync());
    }

    [Fact]
    public async Task ForecastIsRequestedWhenItsCacheExpires_Conditionally()
    {
        var coordinator = Coordinator();
        await coordinator.RefreshDueAsync();
        _provider.Conditionals.Clear();

        _clock.UtcNow = WeatherTestData.Start.AddHours(1);
        _provider.Forecast = _ => Task.FromResult(WeatherFetchResult<WeatherForecast>.NotModified(null, null, _clock.UtcNow.AddHours(1)));
        await coordinator.RefreshDueAsync();

        var forecastCall = Assert.Single(_provider.Conditionals, call => call.Endpoint == "forecast");
        Assert.Equal(WeatherTestData.Start.AddHours(-1), forecastCall.Conditional.LastModified);
        var snapshot = await coordinator.GetSnapshotAsync();
        Assert.Equal(WeatherAvailability.Fresh, snapshot.ForecastStatus.Availability);
        Assert.Equal(_clock.UtcNow, snapshot.ForecastStatus.CheckedAt);
        Assert.NotNull(snapshot.Forecast);
    }

    [Fact]
    public async Task FailedRefresh_KeepsLastKnownGood_AndReportsTheFailure()
    {
        var coordinator = Coordinator();
        await coordinator.RefreshDueAsync();
        var before = (await coordinator.GetSnapshotAsync()).Forecast;

        _clock.UtcNow = WeatherTestData.Start.AddHours(1);
        _provider.Forecast = _ => FakeWeatherProvider.Fail<WeatherForecast>(WeatherFailureKind.Network);
        var report = await coordinator.RefreshDueAsync();

        Assert.Contains(new WeatherEndpointFailure("forecast", WeatherFailureKind.Network), report.Failures);
        var snapshot = await coordinator.GetSnapshotAsync();
        Assert.True(snapshot.ForecastStatus.LastRefreshFailed);
        Assert.Equal(WeatherTestData.Start, snapshot.ForecastStatus.CheckedAt);
        Assert.Equal(before!.Periods.Select(p => p.TemperatureF), snapshot.Forecast!.Periods.Select(p => p.TemperatureF));
    }

    [Fact]
    public async Task FailuresBackOff_AndRetryAfterIsHonored()
    {
        _provider.Alerts = (_, _) => FakeWeatherProvider.Fail<IReadOnlyList<WeatherAlert>>(WeatherFailureKind.RateLimited, TimeSpan.FromMinutes(10));
        var coordinator = Coordinator();
        await coordinator.RefreshDueAsync();
        _provider.Calls.Clear();

        _clock.UtcNow = WeatherTestData.Start.AddMinutes(9);
        await coordinator.RefreshDueAsync();
        Assert.DoesNotContain(_provider.Calls, call => call.StartsWith("alerts:", StringComparison.Ordinal));

        _clock.UtcNow = WeatherTestData.Start.AddMinutes(10);
        await coordinator.RefreshDueAsync();
        Assert.Equal(2, _provider.Calls.Count(call => call.StartsWith("alerts:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ConsecutiveFailuresWithoutRetryAfter_UseTheBackoffSteps()
    {
        _provider.Forecast = _ => FakeWeatherProvider.Fail<WeatherForecast>(WeatherFailureKind.ProviderError);
        var coordinator = Coordinator();
        await coordinator.RefreshDueAsync(); // failure 1 → 30 s

        int ForecastCalls() => _provider.Calls.Count(call => call == "forecast");

        _clock.UtcNow = WeatherTestData.Start.AddSeconds(29);
        await coordinator.RefreshDueAsync();
        Assert.Equal(1, ForecastCalls());

        _clock.UtcNow = WeatherTestData.Start.AddSeconds(30);
        await coordinator.RefreshDueAsync(); // failure 2 → 2 min
        Assert.Equal(2, ForecastCalls());

        _clock.UtcNow = WeatherTestData.Start.AddSeconds(30 + 119);
        await coordinator.RefreshDueAsync();
        Assert.Equal(2, ForecastCalls());

        _clock.UtcNow = WeatherTestData.Start.AddSeconds(30 + 120);
        await coordinator.RefreshDueAsync();
        Assert.Equal(3, ForecastCalls());
    }

    [Fact]
    public async Task FailureBeforeAnyValue_IsUnavailable_NotFabricated()
    {
        _provider.Forecast = _ => FakeWeatherProvider.Fail<WeatherForecast>(WeatherFailureKind.Timeout);
        _provider.Observation = _ => FakeWeatherProvider.Fail<WeatherObservation>(WeatherFailureKind.Timeout);
        var coordinator = Coordinator();

        await coordinator.RefreshDueAsync();
        var snapshot = await coordinator.GetSnapshotAsync();

        Assert.Equal(WeatherAvailability.Unavailable, snapshot.ForecastStatus.Availability);
        Assert.Null(snapshot.Forecast);
        Assert.Equal(WeatherAvailability.Unavailable, snapshot.ObservationStatus.Availability);
        Assert.Null(snapshot.Observation);
    }

    [Fact]
    public async Task NotModifiedWithoutACachedValue_IsTreatedAsAFailure()
    {
        _provider.Forecast = _ => Task.FromResult(WeatherFetchResult<WeatherForecast>.NotModified("x", null, null));
        var coordinator = Coordinator();

        await coordinator.RefreshDueAsync();
        var snapshot = await coordinator.GetSnapshotAsync();

        Assert.Equal(WeatherFailureKind.InvalidResponse, snapshot.ForecastStatus.LastFailure);
        Assert.Null(snapshot.Forecast);
    }

    [Fact]
    public async Task GridChange_DiscardsTheOldForecastAndRequestsTheNewGrid()
    {
        var coordinator = Coordinator();
        await coordinator.RefreshDueAsync();
        _provider.Calls.Clear();

        _clock.UtcNow = WeatherTestData.Start.AddHours(24);
        _provider.Point = _ => FakeWeatherProvider.Ok(WeatherTestData.Grid(117, 60), _clock.UtcNow.AddHours(24));
        _provider.Forecast = _ => FakeWeatherProvider.Fail<WeatherForecast>(WeatherFailureKind.Network);
        var report = await coordinator.RefreshDueAsync();

        Assert.True(report.GridChanged);
        Assert.Contains("stations", _provider.Calls);
        var snapshot = await coordinator.GetSnapshotAsync();
        Assert.Null(snapshot.Forecast);
    }

    [Fact]
    public async Task ForecastNotFound_LooksThePointUpAgainOnTheNextRefresh()
    {
        var coordinator = Coordinator();
        await coordinator.RefreshDueAsync();
        _provider.Calls.Clear();

        _clock.UtcNow = WeatherTestData.Start.AddHours(1);
        _provider.Forecast = _ => FakeWeatherProvider.Fail<WeatherForecast>(WeatherFailureKind.NotFound);
        await coordinator.RefreshDueAsync();
        Assert.DoesNotContain("points:test-a", _provider.Calls);

        _provider.Calls.Clear();
        _clock.UtcNow = WeatherTestData.Start.AddHours(1).AddMinutes(1);
        await coordinator.RefreshDueAsync();
        Assert.Contains("points:test-a", _provider.Calls);
        Assert.DoesNotContain("points:test-b", _provider.Calls);
    }

    [Fact]
    public async Task StaleNearestObservation_FallsBackToTheNextStation()
    {
        _provider.Observation = station => station.Id == "KCLT"
            ? FakeWeatherProvider.Ok(WeatherTestData.Observation("KCLT", _clock.UtcNow.AddHours(-2)), _clock.UtcNow.AddMinutes(5))
            : FakeWeatherProvider.Ok(WeatherTestData.Observation("KUZA", _clock.UtcNow.AddMinutes(-15)), _clock.UtcNow.AddMinutes(5));
        var coordinator = Coordinator();

        await coordinator.RefreshDueAsync();
        var snapshot = await coordinator.GetSnapshotAsync();

        Assert.Contains("observation:KCLT", _provider.Calls);
        Assert.Contains("observation:KUZA", _provider.Calls);
        Assert.DoesNotContain("observation:KAKH", _provider.Calls);
        Assert.Equal("KUZA", snapshot.Observation!.StationId);
        Assert.Equal(WeatherAvailability.Fresh, snapshot.ObservationStatus.Availability);
    }

    [Fact]
    public async Task ObservationWithoutTemperature_IsNotUsedAsCurrentConditions()
    {
        _provider.Observation = station => FakeWeatherProvider.Ok(WeatherTestData.Observation(station.Id, _clock.UtcNow.AddMinutes(-5), temperature: null), _clock.UtcNow.AddMinutes(5));
        var coordinator = Coordinator();

        await coordinator.RefreshDueAsync();
        var snapshot = await coordinator.GetSnapshotAsync();

        Assert.Null(snapshot.Observation);
        Assert.Equal(WeatherAvailability.Unavailable, snapshot.ObservationStatus.Availability);
    }

    [Fact]
    public async Task ConcurrentRefreshes_ShareOneRun()
    {
        var gate = new TaskCompletionSource<WeatherFetchResult<WeatherPointMetadata>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _provider.Point = _ => gate.Task;
        var coordinator = Coordinator();

        var first = coordinator.RefreshDueAsync();
        var second = coordinator.RefreshDueAsync();
        Assert.Same(first, second);

        gate.SetResult(WeatherFetchResult<WeatherPointMetadata>.Retrieved(WeatherTestData.Grid(), null, null, _clock.UtcNow.AddHours(24)));
        await Task.WhenAll(first, second);

        Assert.Equal(1, _provider.Calls.Count(call => call == "forecast"));
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        _provider.Point = _ => throw new OperationCanceledException();
        var coordinator = Coordinator();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.RefreshDueAsync());
    }

    [Fact]
    public async Task UnknownCacheFormat_IsReadAsEmpty()
    {
        var store = new InMemoryWeatherCacheStore(new WeatherCacheDocument { FormatVersion = 99, Forecast = new() { Value = WeatherTestData.Forecast(WeatherTestData.Start), CheckedAt = WeatherTestData.Start } });
        var coordinator = new WeatherRefreshCoordinator(_provider.AsProvider(), store, _clock, WeatherTestData.VerifiedLocation);

        var snapshot = await coordinator.GetSnapshotAsync();

        Assert.Equal(WeatherAvailability.NeverRetrieved, snapshot.ForecastStatus.Availability);
    }

    [Fact]
    public async Task OfflineReopen_ShowsTheCachedWeatherWithoutARequest()
    {
        await Coordinator().RefreshDueAsync();
        _provider.Calls.Clear();

        _clock.UtcNow = WeatherTestData.Start.AddMinutes(30);
        var reopened = new WeatherRefreshCoordinator(_provider.AsProvider(), _store, _clock, WeatherTestData.VerifiedLocation);
        var snapshot = await reopened.GetSnapshotAsync();

        Assert.Empty(_provider.Calls);
        Assert.NotNull(snapshot.Forecast);
        Assert.Equal(WeatherAvailability.Stale, snapshot.AlertsStatus.Availability);
    }

    [Fact]
    public async Task SaveFailure_KeepsTheInMemoryWeather()
    {
        _store.FailSaves = true;
        var coordinator = Coordinator();

        var report = await coordinator.RefreshDueAsync();
        var snapshot = await coordinator.GetSnapshotAsync();

        Assert.True(report.Permitted);
        Assert.NotNull(snapshot.Forecast);
    }
}
