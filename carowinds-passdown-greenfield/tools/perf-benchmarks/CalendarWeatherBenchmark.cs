using System.Diagnostics;
using Avalonia.Controls;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace PerfBenchmarks;

/// <summary>
/// Phase 37D: the cost of the Calendar's selected-date Weather group — building it (each date change and each monitor
/// report), and Calendar date navigation in a realized view with and without it (time per step and managed memory
/// after many steps). Synthetic weather and calendar data only; no NWS request; nothing is written.
/// </summary>
internal static class CalendarWeatherBenchmark
{
    public static int Run(int iterations)
    {
        var now = WeatherUiFixtures.Morning;
        var snapshot = WeatherUiFixtures.Normal();
        var selected = Selected(WeatherUiFixtures.Day);
        CalendarWeatherPresentation weather = null!;
        for (var i = 0; i < 200; i++)
        {
            weather = CalendarWeatherPresentation.Build(snapshot, selected, now);
        }

        Measure("Calendar Weather group build (today, 12 park hours)", iterations, () => weather = CalendarWeatherPresentation.Build(snapshot, selected, now));
        var closed = Selected(WeatherUiFixtures.Day.AddDays(1), closed: true);
        Measure("Calendar Weather group build (tomorrow closed, whole day)", iterations, () => weather = CalendarWeatherPresentation.Build(snapshot, closed, now));
        var past = Selected(WeatherUiFixtures.Day.AddDays(-1));
        Measure("Calendar Weather group build (past date)", iterations, () => weather = CalendarWeatherPresentation.Build(snapshot, past, now));

        // View-model date selection (no window): the per-step cost a click adds.
        foreach (var withWeather in new[] { false, true, false, true })
        {
            var calendar = Calendar(withWeather ? new FakeWeatherMonitor { Current = snapshot } : null);
            GC.Collect();
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            const int Steps = 400;
            for (var i = 0; i < Steps; i++)
            {
                if (i % 2 == 0) calendar.NextDayCommand.Execute(null); else calendar.PreviousDayCommand.Execute(null);
                calendar.PendingLoad.GetAwaiter().GetResult();
            }

            Console.WriteLine($"view-model date step ({(withWeather ? "with" : "without")} weather): {sw.Elapsed.TotalMilliseconds * 1000 / Steps:F1} µs and {(GC.GetAllocatedBytesForCurrentThread() - bytes) / Steps:N0} bytes per step ({Steps} steps)");
        }

        // Realized Calendar (1280×860, headless Skia): date step → layout → rendered frame, and month navigation.
        foreach (var withWeather in new[] { false, true, false, true })
        {
            var (dayMedian, monthMedian, heapMb) = Realized(withWeather ? new FakeWeatherMonitor { Current = snapshot } : null);
            Console.WriteLine($"realized Calendar ({(withWeather ? "with" : "without")} weather): date step median {dayMedian:F2} ms, month step median {monthMedian:F2} ms; managed heap after 120 steps {heapMb:F1} MB");
        }

        Console.WriteLine($"  (last group: {weather.State}, {weather.Hours.Count} hours)");
        return 0;
    }

    private static OperationsCalendarSelectedDateSummary Selected(DateOnly date, bool closed = false)
    {
        var hours = closed
            ? ParkHoursCalculator.Calculate(true, null, null, false, null, null, false, null, null)
            : ParkHoursCalculator.Calculate(false, new TimeOnly(10, 0), new TimeOnly(22, 0), false, null, null, false, null, null);
        return new OperationsCalendarSelectedDateSummary(date, date == WeatherUiFixtures.Day, OperationsCalendarPassdownStatus.NoDraft, null, null, 0,
            closed ? OperationsCalendarOperatingState.Closed : OperationsCalendarOperatingState.Open,
            new AssociateParkHoursView(hours, AssociateVerificationInfo.NotCheckedYet, closed, hours.Opening, hours.Closing));
    }

    private static OperationsCalendarViewModel Calendar(IWeatherMonitor? monitor)
    {
        var service = new FakeOperationsCalendarService
        {
            SnapshotFactory = (month, date) =>
            {
                var s = FakeOperationsCalendarService.DefaultSnapshot(month, date);
                var sel = Selected(date, closed: date.DayOfWeek == DayOfWeek.Sunday);
                return s with { Selected = s.Selected with { OperatingState = sel.OperatingState, ParkHours = sel.ParkHours } };
            },
        };
        var calendar = new OperationsCalendarViewModel(service, new FakeClock(WeatherUiFixtures.Morning), uiDispatcher: new ImmediateUiDispatcher(), weatherMonitor: monitor);
        calendar.PendingLoad.GetAwaiter().GetResult();
        return calendar;
    }

    private static (double Day, double Month, double HeapMb) Realized(IWeatherMonitor? monitor)
    {
        var calendar = Calendar(monitor);
        var window = new Window { Width = 1280, Height = 860, Content = new OperationsCalendarView { DataContext = calendar } };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        _ = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);

        var days = new List<double>();
        for (var i = 0; i < 100; i++)
        {
            var sw = Stopwatch.StartNew();
            if (i % 14 < 7) calendar.NextDayCommand.Execute(null); else calendar.PreviousDayCommand.Execute(null);
            calendar.PendingLoad.GetAwaiter().GetResult();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            _ = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
            days.Add(sw.Elapsed.TotalMilliseconds);
        }

        var months = new List<double>();
        for (var i = 0; i < 20; i++)
        {
            var sw = Stopwatch.StartNew();
            if (i % 2 == 0) calendar.NextMonthCommand.Execute(null); else calendar.PreviousMonthCommand.Execute(null);
            calendar.PendingLoad.GetAwaiter().GetResult();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            _ = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
            months.Add(sw.Elapsed.TotalMilliseconds);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var heap = GC.GetTotalMemory(forceFullCollection: true) / (1024.0 * 1024.0);
        window.Close();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (Median(days.Skip(10)), Median(months.Skip(2)), heap);
    }

    private static void Measure(string label, int iterations, Action action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            action();
        }

        Console.WriteLine($"{label}: {sw.Elapsed.TotalMilliseconds * 1000 / iterations:F1} µs and {(GC.GetAllocatedBytesForCurrentThread() - bytes) / iterations:N0} bytes per call ({iterations} calls)");
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
    }
}
