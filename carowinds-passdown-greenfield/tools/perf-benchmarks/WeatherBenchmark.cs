using System.Diagnostics;
using Avalonia.Controls;
using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Domain.Weather;
using Carowinds.Passdown.Infrastructure.Weather;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace PerfBenchmarks;

/// <summary>
/// Phase 37C: the cost of the Dashboard Weather card with cached weather — reading the cache file (thread pool, at
/// monitor start), building the snapshot and the card text (each minute and after each refresh), and realizing the
/// Dashboard with and without the card. Synthetic weather only; no NWS request; the cache file lives in a scratch
/// folder and is deleted.
/// </summary>
internal static class WeatherBenchmark
{
    public static Task<int> RunAsync(int iterations) => Task.FromResult(Run(iterations));

    private static int Run(int iterations)
    {
        var now = WeatherUiFixtures.Morning;
        var document = RealisticCache(now);
        var policy = new WeatherRefreshPolicy();

        // 1. Cache file round trip (the monitor's first read, off the UI thread).
        var folder = Path.Combine(Path.GetTempPath(), "carowinds-perf-weather-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var store = new FileWeatherCacheStore(Path.Combine(folder, "weather-cache.json"));
            store.SaveAsync(document).GetAwaiter().GetResult();
            var size = new FileInfo(Path.Combine(folder, "weather-cache.json")).Length;
            var loads = new List<double>();
            for (var i = 0; i < 20; i++)
            {
                var sw = Stopwatch.StartNew();
                _ = store.LoadAsync().GetAwaiter().GetResult();
                loads.Add(sw.Elapsed.TotalMilliseconds);
            }

            Console.WriteLine($"weather cache file: {size:N0} bytes; load median {Median(loads):F2} ms, max {loads.Max():F2} ms (20 loads; thread pool, not UI)");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }

        // 2. Snapshot and card text (what each minute's rebuild costs on the UI thread).
        WeatherSnapshot snapshot = null!;
        DashboardWeatherPresentation card = null!;
        var park = Carowinds.Passdown.Ui.Tests.TestSupport.Phase22CDashboardFixtures.VerifiedOperatingDay(WeatherUiFixtures.Day).ParkStatus;
        for (var i = 0; i < 200; i++)
        {
            snapshot = WeatherSnapshotBuilder.Build(document, now, policy, CarowindsWeatherLocation.Park);
            card = DashboardWeatherPresentation.Build(snapshot, park, WeatherUiFixtures.Day, now);
        }

        Measure("snapshot build (14 periods, 156 hours, 2 stations, 8 alert messages)", iterations, () => snapshot = WeatherSnapshotBuilder.Build(document, now, policy, CarowindsWeatherLocation.Park));
        Measure("card presentation build", iterations, () => card = DashboardWeatherPresentation.Build(snapshot, park, WeatherUiFixtures.Day, now));
        Console.WriteLine($"  (card: {card.Alerts.Count} active alerts, rain {card.RainChanceText ?? "n/a"})");

        // 3. Dashboard realization with cached weather vs without the card (headless Skia; first layout and render).
        var without = new List<double>();
        var with = new List<double>();
        for (var i = 0; i < 12; i++)
        {
            without.Add(RealizeDashboard(weather: null));
            with.Add(RealizeDashboard(new FakeWeatherMonitor { Current = snapshot }));
        }

        // The first pair includes one-time XAML and style loading; report the rest.
        Console.WriteLine($"Dashboard realize (1280x860, headless): without card median {Median(without.Skip(2)):F1} ms; with cached weather median {Median(with.Skip(2)):F1} ms (10 runs each after 2 warm-ups)");
        return 0;
    }

    private static double RealizeDashboard(FakeWeatherMonitor? weather)
    {
        var service = new FakeOperationsDashboardService { SnapshotFactory = _ => Phase22CDashboardFixtures.BusyDayWithPartialFailure(WeatherUiFixtures.Day) };
        var weatherViewModel = weather is null ? null : new DashboardWeatherViewModel(weather, new FakeClock(WeatherUiFixtures.Morning), new ImmediateUiDispatcher());
        var viewModel = new OperationsDashboardViewModel(service, uiDispatcher: new ImmediateUiDispatcher(), weather: weatherViewModel);
        viewModel.PendingLoad.GetAwaiter().GetResult();

        var sw = Stopwatch.StartNew();
        var window = new Window { Width = 1280, Height = 860, Content = new DashboardView { DataContext = viewModel } };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        _ = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
        var elapsed = sw.Elapsed.TotalMilliseconds;
        window.Close();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return elapsed;
    }

    private static void Measure(string label, int iterations, Action action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var gen2 = GC.CollectionCount(2);
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            action();
        }

        var perCallUs = sw.Elapsed.TotalMilliseconds * 1000 / iterations;
        var perCallBytes = (GC.GetAllocatedBytesForCurrentThread() - bytes) / iterations;
        Console.WriteLine($"{label}: {perCallUs:F1} µs and {perCallBytes:N0} bytes per call ({iterations} calls, gen-2 collections {GC.CollectionCount(2) - gen2})");
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
    }

    /// <summary>A full cache like the 37B size test: 14 forecast periods, 156 hourly periods, 3 stations, 2 observations, 8 alert messages.</summary>
    private static WeatherCacheDocument RealisticCache(DateTimeOffset now)
    {
        var start = new DateTimeOffset(2026, 10, 17, 10, 0, 0, TimeSpan.Zero);
        var periods = Enumerable.Range(0, 14).Select(i => new ForecastPeriod(i + 1, i % 2 == 0 ? "Day" : "Night", start.AddHours(12 * i), start.AddHours(12 * (i + 1)), i % 2 == 0, 60 + i, 10 + i, "5 to 10 mph", "SW", "Partly Sunny", "Synthetic detailed forecast text for the benchmark.")).ToArray();
        var hourly = Enumerable.Range(0, 156).Select(i => new HourlyForecastPeriod(now.AddHours(i - 1), now.AddHours(i), i % 24 is > 10 and < 23, 60 + (i % 15), i % 7 * 5, 60, 50, "5 mph", "SW", "Sunny")).ToArray();
        var stations = new WeatherStation[]
        {
            new("KCLT", "Charlotte/Douglas International Airport", 35.21, -80.96),
            new("KUZA", "Rock Hill - York County Airport", 34.98, -81.06),
            new("KAKH", "Gastonia Municipal Airport", 35.20, -81.16),
        };
        var alerts = Enumerable.Range(0, 4).Select(i => WeatherUiFixtures.Alert($"synthetic-{i}", "Wind Advisory", "Moderate", now.AddHours(-2), now.AddHours(4 + i))).ToArray();
        WeatherEndpointState<T> Fresh<T>(T value) where T : class => new() { Value = value, CheckedAt = now.AddMinutes(-2), RetrievedAt = now.AddMinutes(-2), LastAttemptAt = now.AddMinutes(-2) };
        var grid = new WeatherPointMetadata("GSP", 116, 59, new Uri("https://api.weather.gov/gridpoints/GSP/116,59/forecast"), new Uri("https://api.weather.gov/gridpoints/GSP/116,59/forecast/hourly"), new Uri("https://api.weather.gov/gridpoints/GSP/116,59/stations"), "NCZ071", "NCC119", "America/New_York");

        return new WeatherCacheDocument
        {
            Points = new Dictionary<string, WeatherEndpointState<WeatherPointMetadata>> { ["carowinds-nc"] = Fresh(grid), ["carowinds-sc"] = Fresh(grid) },
            Stations = Fresh<IReadOnlyList<WeatherStation>>(stations),
            Forecast = Fresh(new WeatherForecast("GSP", 116, 59, now.AddHours(-3), periods)),
            Hourly = Fresh(new HourlyWeatherForecast("GSP", 116, 59, now.AddHours(-3), hourly)),
            Observations = new Dictionary<string, WeatherEndpointState<WeatherObservation>>
            {
                ["KCLT"] = Fresh(WeatherUiFixtures.Observation(now.AddMinutes(-10))),
                ["KUZA"] = Fresh(WeatherUiFixtures.Observation(now.AddMinutes(-15), "KUZA", "Rock Hill - York County Airport", 16.8)),
            },
            Alerts = new Dictionary<string, WeatherEndpointState<IReadOnlyList<WeatherAlert>>>
            {
                ["carowinds-nc"] = Fresh<IReadOnlyList<WeatherAlert>>(alerts),
                ["carowinds-sc"] = Fresh<IReadOnlyList<WeatherAlert>>(alerts),
            },
        };
    }
}
