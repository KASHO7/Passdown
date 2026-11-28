using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Headless;
using Carowinds.Passdown.Desktop;
using PerfBenchmarks;

// Phase 34B-2 opt-in performance benchmarks. Usage: README.md.
// Every number printed is a measurement of this machine; nothing is asserted and nothing is uploaded.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

if (args.Length == 0)
{
    Console.WriteLine("""
        Carowinds Operations — opt-in performance benchmarks (synthetic data only)
          pdf-startup [runs]            first-PDF cost in fresh processes, with and without the warm-up (default 7)
          pdf-render [iterations]       preview page render: allocations, gen-2 GCs and time, copy vs direct (default 300)
          db <scratchDir> [synced...]   Associate date load, Calendar month, Price History on a synthetic SQLite database
          preview [edits]               isolated edit → preview page image through the real preview view model (default 25)
          print-bands [pages]           600 DPI print band loop off Windows: time and allocations per page (default 10)
          refresh-requests              official POSTs for a 7-day rolling window, without and with coalescing
          weather [iterations]          Phase 37C: weather cache load, snapshot and card build, Dashboard realize with/without the card
          pdf-weather [iterations] [outDir]  Phase 37F: PDF generation with/without the Weather Outlook; renders to outDir
          calendar-weather [iterations] Phase 37D: Calendar Weather group build; Calendar date/month navigation with/without it
          all <scratchDir>              everything above with defaults
        """);
    return 1;
}

Console.WriteLine($"# {Environment.OSVersion} | {RuntimeInfo()} | {Environment.ProcessorCount} logical CPUs | build {BuildConfiguration()}");

switch (args[0])
{
    case "pdf-startup":
        return PdfStartupBenchmark.RunSuite(args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 7);
    case "pdf-startup-child":
        // Internal: one fresh process = one cold sample. args[1] = cold | warmed | concurrent.
        return PdfStartupBenchmark.RunChild(args[1]);
    case "pdf-render":
        InitializeHeadlessSkia();
        return PdfRenderBenchmark.Run(args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 300);
    case "db":
        return await DatabaseBenchmark.RunAsync(RequireScratch(args), args.Skip(2).Select(a => int.Parse(a, CultureInfo.InvariantCulture)).DefaultIfEmpty(0).ToArray());
    case "preview":
        InitializeHeadlessSkia();
        return await PreviewLatencyBenchmark.RunAsync(args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 25);
    case "print-bands":
        return PrintBandBenchmark.Run(args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 10);
    case "refresh-requests":
        return await RefreshRequestBenchmark.RunAsync();
    case "weather":
        InitializeHeadlessSkia();
        return await WeatherBenchmark.RunAsync(args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 2000);
    case "pdf-weather":
        return PdfWeatherBenchmark.Run(args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 40, args.Length > 2 ? args[2] : null);
    case "calendar-weather":
        InitializeHeadlessSkia();
        return CalendarWeatherBenchmark.Run(args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 2000);
    case "all":
        var scratch = RequireScratch(args);
        var status = PdfStartupBenchmark.RunSuite(7);
        InitializeHeadlessSkia();
        status |= PdfRenderBenchmark.Run(300);
        status |= await PreviewLatencyBenchmark.RunAsync(25);
        status |= PrintBandBenchmark.Run(10);
        status |= await DatabaseBenchmark.RunAsync(scratch, [0, 2_000, 20_000]);
        status |= await RefreshRequestBenchmark.RunAsync();
        status |= await WeatherBenchmark.RunAsync(2000);
        status |= PdfWeatherBenchmark.Run(40, null);
        status |= CalendarWeatherBenchmark.Run(2000);
        return status;
    default:
        Console.Error.WriteLine($"Unknown benchmark '{args[0]}'.");
        return 1;
}

static string RequireScratch(string[] args)
{
    if (args.Length < 2)
    {
        throw new ArgumentException("Pass a scratch directory (outside the repository and outside any cloud-synced folder).");
    }

    var path = Path.GetFullPath(args[1]);
    if (path.Contains("OneDrive", StringComparison.OrdinalIgnoreCase) || path.Contains("Dropbox", StringComparison.OrdinalIgnoreCase)
        || path.Contains("iCloud", StringComparison.OrdinalIgnoreCase) || path.Contains("Mobile Documents", StringComparison.OrdinalIgnoreCase))
    {
        throw new ArgumentException("The scratch database must not live in a cloud-sync folder (CLAUDE.md persistence rule).");
    }

    Directory.CreateDirectory(path);
    return path;
}

static void InitializeHeadlessSkia()
{
    AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();

    // Setup installs a dispatcher synchronization context that nothing pumps here; without clearing it every
    // later await would wait forever. Work then continues on the thread pool, as background work does in the app.
    SynchronizationContext.SetSynchronizationContext(null);
}

static string RuntimeInfo() => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + " " + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;

static string BuildConfiguration()
{
#if DEBUG
    return "Debug (numbers are not representative — use -c Release)";
#else
    return "Release";
#endif
}

namespace PerfBenchmarks
{
    internal static class Stats
    {
        public static string Summary(IReadOnlyList<double> samples, string unit = "ms")
        {
            if (samples.Count == 0)
            {
                return "n=0";
            }

            var sorted = samples.Order().ToArray();
            return string.Create(CultureInfo.InvariantCulture,
                $"median {Percentile(sorted, 0.5):F2} {unit} | p95 {Percentile(sorted, 0.95):F2} | min {sorted[0]:F2} | max {sorted[^1]:F2} | n={sorted.Length}");
        }

        public static double Median(IReadOnlyList<double> samples) => Percentile(samples.Order().ToArray(), 0.5);

        public static double Percentile(double[] sorted, double p)
        {
            if (sorted.Length == 1)
            {
                return sorted[0];
            }

            var rank = p * (sorted.Length - 1);
            var low = (int)Math.Floor(rank);
            var high = (int)Math.Ceiling(rank);
            return sorted[low] + ((sorted[high] - sorted[low]) * (rank - low));
        }

        public static double Time(Action action)
        {
            var stopwatch = Stopwatch.StartNew();
            action();
            return stopwatch.Elapsed.TotalMilliseconds;
        }
    }
}
