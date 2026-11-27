using System.Diagnostics;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Domain.Weather;
using Carowinds.Passdown.Infrastructure.PdfOutput;
using Carowinds.Passdown.Ui.Tests.TestSupport;
using SkiaSharp;

namespace PerfBenchmarks;

/// <summary>
/// Phase 37F: the PDF Weather Outlook's cost and appearance. Times Associate Passdown generation for each template
/// with and without the outlook, the outlook build itself (forecast selection + wording), and the PDF size; with an
/// output folder it also writes PDFium renders (colour and grayscale) of each representative scenario plus the PDFs.
/// Synthetic data only: invented hourly forecasts, the Documents test fixtures, no NWS request, no real profile.
/// </summary>
internal static class PdfWeatherBenchmark
{
    /// <summary>Friday, August 21, 2026, 7:00 AM EDT — before opening, as a Passdown is usually printed.</summary>
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 11, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Date = new(2026, 8, 21);

    public static int Run(int iterations, string? outDir)
    {
        var generator = new AssociatePassdownPdfGenerator();
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        var snapshot = Snapshot(i => 70 + Math.Min(i, 6) - Math.Max(0, i - 8), i => i >= 12 ? 40 : 10, i => i >= 12 ? "Chance Showers" : "Partly Cloudy");

        // 1. Outlook build (selection + wording), once per capture.
        _ = PassdownWeatherOutlookSource.For(snapshot, workspace, Now);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        const int buildCount = 20_000;
        for (var i = 0; i < buildCount; i++)
        {
            _ = PassdownWeatherOutlookSource.For(snapshot, workspace, Now);
        }

        sw.Stop();
        Console.WriteLine($"outlook build (select + word): {sw.Elapsed.TotalMicroseconds / buildCount:0.0} µs, {(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / buildCount:N0} bytes per call");

        // 2. Generation with and without the outlook, per template (warm process).
        var outlook = PassdownWeatherOutlookSource.For(snapshot, workspace, Now)!;
        foreach (var template in Enum.GetValues<AssociatePassdownTemplate>())
        {
            var without = Time(() => generator.Generate(AssociatePassdownDocumentModelBuilder.Build(workspace, Now), template), iterations, out var plainBytes);
            var with = Time(() => generator.Generate(AssociatePassdownDocumentModelBuilder.Build(workspace, Now, outlook), template), iterations, out var weatherBytes);
            var noIcon = Time(() => generator.Generate(AssociatePassdownDocumentModelBuilder.Build(workspace, Now, outlook with { Icon = PdfWeatherIcon.None }), template), iterations, out _);
            Console.WriteLine($"{template,-8} generate (outlook without its icon): {noIcon.Median:0.00} ms");
            Console.WriteLine($"{template,-8} generate: without {without.Median:0.00} ms (p90 {without.P90:0.00}) | with {with.Median:0.00} ms (p90 {with.P90:0.00}) | size {plainBytes:N0} → {weatherBytes:N0} bytes (+{weatherBytes - plainBytes:N0})");
        }

        if (outDir is not null)
        {
            Render(outDir, generator);
        }

        return 0;
    }

    private static (double Median, double P90) Time(Func<AssociatePassdownPdfResult> generate, int iterations, out int size)
    {
        size = 0;
        for (var i = 0; i < 5; i++)
        {
            size = generate().PdfBytes!.Length;
        }

        var samples = new double[iterations];
        for (var i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            generate();
            samples[i] = sw.Elapsed.TotalMilliseconds;
        }

        Array.Sort(samples);
        return (samples[iterations / 2], samples[(int)(iterations * 0.9)]);
    }

    /// <summary>Hourly periods from 6 AM EDT for 48 hours; index 0 is 6 AM.</summary>
    private static WeatherSnapshot Snapshot(Func<int, int?> temperature, Func<int, int?> chance, Func<int, string?> text, Func<int, bool>? daytime = null)
    {
        var start = new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);
        var periods = Enumerable.Range(0, 48)
            .Select(i => new HourlyForecastPeriod(start.AddHours(i), start.AddHours(i + 1), daytime?.Invoke(i) ?? (i % 24) is >= 1 and < 14, temperature(i), chance(i), null, null, null, null, text(i)))
            .ToArray();
        var checkedAt = Now.AddMinutes(-20);
        var fresh = new WeatherPartStatus(WeatherAvailability.Fresh, checkedAt, checkedAt, WeatherFailureKind.None);
        var none = new WeatherPartStatus(WeatherAvailability.NeverRetrieved, null, null, WeatherFailureKind.None);
        return new WeatherSnapshot(Now, true, none, null, fresh, new HourlyWeatherForecast("GSP", 1, 1, new DateTimeOffset(2026, 8, 21, 10, 5, 0, TimeSpan.Zero), periods), none, null, none, []);
    }

    private static AssociateWorkspace WithHours(AssociateWorkspace workspace, int open, int close) => workspace with
    {
        ParkHours = new AssociateParkHoursView(
            ParkHoursCalculator.Calculate(false, new TimeOnly(open, 0), new TimeOnly(close, 0), false, null, null, false, null, null),
            workspace.ParkHours.Verification, false, new TimeOnly(open, 0), new TimeOnly(close, 0)),
    };

    private static void Render(string outDir, AssociatePassdownPdfGenerator generator)
    {
        Directory.CreateDirectory(outDir);
        var normal = AssociateWorkspaceFixtures.NormalDay();
        var scenarios = new (string Name, AssociateWorkspace Workspace, WeatherSnapshot? Weather)[]
        {
            ("normal", normal, Snapshot(i => 70 + Math.Min(i, 8) / 2, _ => 5, _ => "Sunny")),
            ("rain-later", WithHours(normal, 12, 20), Snapshot(i => 74 - Math.Max(0, i - 8), i => i >= 12 ? 40 : 10, i => i >= 12 ? "Chance Showers" : "Partly Cloudy")),
            ("short-hours", WithHours(normal, 17, 18), Snapshot(_ => 65, _ => 20, _ => "Cloudy")),
            ("evening", WithHours(normal, 18, 23), Snapshot(i => 78 - (i / 3), _ => 0, _ => "Clear")),
            ("long-condition", normal, Snapshot(_ => 88, i => i >= 9 ? 60 : 20, i => i >= 9 ? "Chance Showers And Thunderstorms then Patchy Fog" : "Mostly Sunny")),
            ("missing-temperature", normal, Snapshot(i => i == 10 ? null : 72, _ => 10, _ => "Mostly Cloudy")),
            ("missing-precipitation", normal, Snapshot(_ => 72, _ => null, _ => "Partly Sunny")),
            ("missing-forecast", normal, null),
            ("buyout", WithHours(AssociateWorkspaceFixtures.PrivateBuyoutDay(), 18, 23), Snapshot(_ => 75, i => i >= 15 ? 30 : 10, i => i >= 15 ? "Slight Chance Rain Showers" : "Partly Cloudy")),
            ("dense", AssociateWorkspaceFixtures.DenseDay(), Snapshot(i => 70 + (i % 4), _ => 10, _ => "Partly Sunny")),
        };

        var pdfium = new PdfiumPdfRasterizer();
        foreach (var template in Enum.GetValues<AssociatePassdownTemplate>())
        {
            foreach (var (name, workspace, weather) in scenarios)
            {
                var outlook = PassdownWeatherOutlookSource.For(weather, workspace, Now);
                foreach (var (suffix, model) in new[] { ("with", AssociatePassdownDocumentModelBuilder.Build(workspace, Now, outlook)), ("without", AssociatePassdownDocumentModelBuilder.Build(workspace, Now)) })
                {
                    if (suffix == "without" && name is not ("normal" or "buyout" or "dense"))
                    {
                        continue;
                    }

                    var result = generator.Generate(model, template);
                    var stem = Path.Combine(outDir, $"{template}-{name}-{suffix}");
                    File.WriteAllBytes(stem + ".pdf", result.PdfBytes!);
                    using var document = pdfium.Open(result.PdfBytes!);
                    var page = document.RenderPage(0, 2.0);
                    Save(page, stem + ".png", grayscale: false);
                    if (name is "normal" or "rain-later")
                    {
                        Save(page, stem + "-grayscale.png", grayscale: true);
                    }

                    Console.WriteLine($"render {template}/{name}/{suffix}: pages={result.PageCount} bytes={result.PdfBytes!.Length:N0} outlook={(outlook is null ? "omitted" : outlook.TemperatureText ?? outlook.ConditionText)}");
                }
            }
        }
    }

    private static void Save(Carowinds.Passdown.Application.PdfOutput.PdfRasterImage page, string path, bool grayscale)
    {
        var pixels = (byte[])page.Bgra.Clone();
        if (grayscale)
        {
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var luminance = (byte)Math.Round((0.0722 * pixels[i]) + (0.7152 * pixels[i + 1]) + (0.2126 * pixels[i + 2]));
                pixels[i] = pixels[i + 1] = pixels[i + 2] = luminance;
            }
        }

        using var bitmap = new SKBitmap(new SKImageInfo(page.PixelWidth, page.PixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), Math.Min(pixels.Length, bitmap.ByteCount));
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }
}
