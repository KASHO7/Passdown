using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Domain.Weather;
using Xunit.Abstractions;

namespace Carowinds.Passdown.Application.Tests.Weather;

/// <summary>
/// Phase 37E (release-readiness audit, test only): bounded, deterministic request accounting for one running app.
/// Drives the real <see cref="WeatherMonitor"/> loop and <see cref="WeatherRefreshCoordinator"/> with the default
/// <see cref="WeatherRefreshPolicy"/> over simulated hours (a virtual clock advanced by the loop's own waits), against
/// a scripted provider that models NWS behaviour: stations report hourly at :53, cache lifetimes as configured, and
/// failures or rate-limit answers where a scenario says so. No network; nothing here reaches NWS. Each simulated
/// device is independent, so several devices on one public IP make exactly N times these counts.
/// </summary>
public class WeatherRequestVolumeSimulationTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Start = WeatherTestData.Start; // 9:00 AM EDT

    private sealed record Call(DateTimeOffset At, string Endpoint);

    /// <summary>Runs the monitor for <paramref name="duration"/> of virtual time and returns every provider call.</summary>
    private static async Task<IReadOnlyList<Call>> SimulateAsync(TimeSpan duration, Action<FakeWeatherProvider, FakeClock> configure, Func<FakeClock, WeatherMonitor, Task>? during = null)
    {
        var clock = new FakeClock(Start);
        var provider = FakeWeatherProvider.AllSucceed(() => clock.UtcNow);
        provider.Observation = station => FakeWeatherProvider.Ok(WeatherTestData.Observation(station.Id, LastReport(clock.UtcNow)), clock.UtcNow.AddMinutes(5));
        configure(provider, clock);

        var calls = new List<Call>();
        var recorded = 0;
        var end = Start + duration;
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new WeatherRefreshCoordinator(provider.AsProvider(), new InMemoryWeatherCacheStore(), clock, WeatherTestData.VerifiedLocation);
        WeatherMonitor monitor = null!;
        monitor = new WeatherMonitor(
            coordinator,
            clock,
            delayAsync: (wait, token) =>
            {
                for (; recorded < provider.Calls.Count; recorded++)
                {
                    calls.Add(new Call(clock.UtcNow, provider.Calls[recorded]));
                }

                if (during is not null)
                {
                    during(clock, monitor).GetAwaiter().GetResult();
                    for (; recorded < provider.Calls.Count; recorded++)
                    {
                        calls.Add(new Call(clock.UtcNow, provider.Calls[recorded]));
                    }
                }

                if (clock.UtcNow >= end)
                {
                    finished.TrySetResult();
                    return Task.Delay(Timeout.InfiniteTimeSpan, token);
                }

                clock.UtcNow += wait;
                return Task.CompletedTask;
            });

        monitor.Start();
        var completed = await Task.WhenAny(finished.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        Assert.Same(finished.Task, completed);
        await monitor.StopAsync();
        return calls;
    }

    /// <summary>The station's most recent hourly report (:53) at or before <paramref name="now"/>.</summary>
    private static DateTimeOffset LastReport(DateTimeOffset now)
    {
        var top = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);
        var report = top.AddMinutes(53);
        return report <= now ? report : report.AddHours(-1);
    }

    /// <summary>Calls in each whole simulated hour after the first (the first includes the one-time point and station lookups).</summary>
    private int[] SteadyHourlyCounts(IReadOnlyList<Call> calls, int hours)
    {
        var counts = Enumerable.Range(1, hours - 1)
            .Select(hour => calls.Count(call => call.At >= Start.AddHours(hour) && call.At < Start.AddHours(hour + 1)))
            .ToArray();
        output.WriteLine($"first hour {calls.Count(call => call.At < Start.AddHours(1))}; later hours {string.Join(", ", counts)}");
        return counts;
    }

    private static int CountIn(IReadOnlyList<Call> calls, int hour, Func<string, bool> endpoint) =>
        calls.Count(call => call.At >= Start.AddHours(hour) && call.At < Start.AddHours(hour + 1) && endpoint(call.Endpoint));

    [Fact]
    public async Task TypicalNwsCacheLifetimes_StayWithinThePolicyBudget_PerHour()
    {
        var calls = await SimulateAsync(TimeSpan.FromHours(8), (_, _) => { });

        var perHour = SteadyHourlyCounts(calls, 8);
        Assert.All(perHour, count => Assert.InRange(count, 24, 34));
        for (var hour = 1; hour < 8; hour++)
        {
            Assert.Equal(24, CountIn(calls, hour, e => e.StartsWith("alerts:", StringComparison.Ordinal)));
            Assert.InRange(CountIn(calls, hour, e => e == "forecast"), 0, 2);
            Assert.InRange(CountIn(calls, hour, e => e == "hourly"), 0, 2);
            Assert.InRange(CountIn(calls, hour, e => e.StartsWith("observation:", StringComparison.Ordinal)), 0, 6);
        }

        // One-time lookups: each point once and the station list once in eight hours.
        Assert.Equal(2, calls.Count(call => call.Endpoint.StartsWith("points:", StringComparison.Ordinal)));
        Assert.Equal(1, calls.Count(call => call.Endpoint == "stations"));
    }

    [Fact]
    public async Task VeryShortProviderCacheLifetimes_AreCappedByThePolicyFloors()
    {
        // Worst case for successful answers: every response expires after one second, so only the floors limit requests.
        var calls = await SimulateAsync(TimeSpan.FromHours(6), (provider, clock) =>
        {
            provider.Point = _ => FakeWeatherProvider.Ok(WeatherTestData.Grid(), clock.UtcNow.AddSeconds(1));
            provider.Stations = () => FakeWeatherProvider.Ok(WeatherTestData.Stations, clock.UtcNow.AddSeconds(1));
            provider.Forecast = _ => FakeWeatherProvider.Ok(WeatherTestData.Forecast(clock.UtcNow), clock.UtcNow.AddSeconds(1));
            provider.Hourly = _ => FakeWeatherProvider.Ok(WeatherTestData.Hourly(clock.UtcNow), clock.UtcNow.AddSeconds(1));
            provider.Observation = station => FakeWeatherProvider.Ok(WeatherTestData.Observation(station.Id, LastReport(clock.UtcNow)), clock.UtcNow.AddSeconds(1));
            provider.Alerts = (_, _) => FakeWeatherProvider.Ok<IReadOnlyList<WeatherAlert>>([], clock.UtcNow.AddSeconds(1));
        });

        // Alerts 24 (target 5 min, two points), forecast and hourly each at most 2 (30-min floor) and the
        // observation at most 6 (10-min floor): never more than 34 in an hour.
        Assert.All(SteadyHourlyCounts(calls, 6), count => Assert.InRange(count, 24, 34));
        Assert.Equal(2, calls.Count(call => call.Endpoint.StartsWith("points:", StringComparison.Ordinal)));
        Assert.Equal(1, calls.Count(call => call.Endpoint == "stations"));
    }

    [Fact]
    public async Task NearestStationNotReporting_AddsTheSecondStation_ButStaysBounded()
    {
        // The nearest station's latest report is three hours old (not usable), so the next station is also asked.
        var calls = await SimulateAsync(TimeSpan.FromHours(6), (provider, clock) =>
            provider.Observation = station => FakeWeatherProvider.Ok(
                WeatherTestData.Observation(station.Id, station.Id == "KCLT" ? clock.UtcNow.AddHours(-3) : LastReport(clock.UtcNow)),
                clock.UtcNow.AddMinutes(5)));

        var perHour = SteadyHourlyCounts(calls, 6);
        Assert.All(perHour, count => Assert.InRange(count, 24, 40));
        Assert.Contains(calls, call => call.Endpoint == "observation:KUZA");
        Assert.DoesNotContain(calls, call => call.Endpoint == "observation:KAKH"); // only the two nearest stations
    }

    [Theory]
    [InlineData(WeatherFailureKind.RateLimited)]
    [InlineData(WeatherFailureKind.ProviderError)]
    [InlineData(WeatherFailureKind.Network)]
    public async Task PersistentFailureAfterTheFirstHour_BacksOffToAtMostTwoAttemptsPerEndpointPerHour(WeatherFailureKind kind)
    {
        var failFrom = Start.AddHours(1);
        var calls = await SimulateAsync(TimeSpan.FromHours(6), (provider, clock) =>
        {
            var forecast = provider.Forecast;
            var hourly = provider.Hourly;
            var observation = provider.Observation;
            var alerts = provider.Alerts;
            provider.Forecast = c => clock.UtcNow >= failFrom ? FakeWeatherProvider.Fail<WeatherForecast>(kind) : forecast(c);
            provider.Hourly = c => clock.UtcNow >= failFrom ? FakeWeatherProvider.Fail<HourlyWeatherForecast>(kind) : hourly(c);
            provider.Observation = s => clock.UtcNow >= failFrom ? FakeWeatherProvider.Fail<WeatherObservation>(kind) : observation(s);
            provider.Alerts = (p, c) => clock.UtcNow >= failFrom ? FakeWeatherProvider.Fail<IReadOnlyList<WeatherAlert>>(kind) : alerts(p, c);
        });

        // Backoff 30 s, 2 min, 10 min, then 30 min: the first failing hour is the busiest, and afterwards each
        // endpoint is tried at most twice an hour (a total far below the healthy 24–34).
        _ = SteadyHourlyCounts(calls, 6);
        var firstFailingHour = calls.Count(call => call.At >= failFrom && call.At < failFrom.AddHours(1));
        Assert.InRange(firstFailingHour, 1, 34);
        for (var hour = 3; hour < 6; hour++)
        {
            Assert.InRange(CountIn(calls, hour, e => e.StartsWith("alerts:", StringComparison.Ordinal)), 0, 4);
            Assert.InRange(CountIn(calls, hour, e => e == "forecast"), 0, 2);
            Assert.InRange(CountIn(calls, hour, e => e == "hourly"), 0, 2);
            Assert.InRange(CountIn(calls, hour, _ => true), 0, 12);
        }
    }

    [Fact]
    public async Task RateLimitWithRetryAfter_IsHonouredForTheWholeWait()
    {
        var limitedAt = Start.AddHours(1);
        DateTimeOffset? firstLimitedCall = null;
        var calls = await SimulateAsync(TimeSpan.FromHours(4), (provider, clock) =>
        {
            var alerts = provider.Alerts;
            provider.Alerts = (p, c) =>
            {
                if (clock.UtcNow >= limitedAt && (firstLimitedCall is null || clock.UtcNow < firstLimitedCall.Value.AddHours(1)))
                {
                    firstLimitedCall ??= clock.UtcNow;
                    return FakeWeatherProvider.Fail<IReadOnlyList<WeatherAlert>>(WeatherFailureKind.RateLimited, TimeSpan.FromHours(1));
                }

                return alerts(p, c);
            };
        });

        _ = SteadyHourlyCounts(calls, 4);
        Assert.NotNull(firstLimitedCall);
        var limited = firstLimitedCall!.Value;
        // Each point was told to wait an hour: no alert request for either point inside that hour.
        var alertCallsInWait = calls.Where(call => call.Endpoint.StartsWith("alerts:", StringComparison.Ordinal) && call.At > limited && call.At < limited.AddHours(1)).ToList();
        Assert.Empty(alertCallsInWait);
        Assert.Contains(calls, call => call.Endpoint.StartsWith("alerts:", StringComparison.Ordinal) && call.At >= limited.AddHours(1));
    }

    [Fact]
    public async Task RepeatedRefreshWeather_PressedAtEveryLoopPass_AddsOnlyWhatTheFloorsAllow()
    {
        // A person pressing Refresh Weather at every loop pass (every 15 s to 1 min) for three hours.
        var calls = await SimulateAsync(
            TimeSpan.FromHours(3),
            (_, _) => { },
            during: async (_, monitor) => await monitor.RefreshNowAsync());

        _ = SteadyHourlyCounts(calls, 3);
        // Manual mode waits out the minimum intervals: alerts at most once a minute per point (120), forecast and
        // hourly at most twice an hour, observation at most six times an hour.
        for (var hour = 1; hour < 3; hour++)
        {
            Assert.InRange(CountIn(calls, hour, e => e.StartsWith("alerts:", StringComparison.Ordinal)), 0, 120);
            Assert.InRange(CountIn(calls, hour, e => e == "forecast"), 0, 2);
            Assert.InRange(CountIn(calls, hour, e => e == "hourly"), 0, 2);
            Assert.InRange(CountIn(calls, hour, e => e.StartsWith("observation:", StringComparison.Ordinal)), 0, 6);
            Assert.DoesNotContain(calls, call => call.At >= Start.AddHours(hour) && (call.Endpoint.StartsWith("points:", StringComparison.Ordinal) || call.Endpoint == "stations"));
        }
    }
}
