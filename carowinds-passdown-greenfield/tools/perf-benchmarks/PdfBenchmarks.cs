using System.Diagnostics;
using System.Globalization;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.PdfOutput;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Infrastructure.PdfOutput;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace PerfBenchmarks;

/// <summary>A synthetic busy day (same shape as the Phase 33 real-PDF tests): one page, or two-plus with long notes.</summary>
internal static class SyntheticPassdown
{
    public static readonly DateOnly Date = new(2026, 8, 21);
    public static readonly DateTimeOffset GeneratedAt = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero);

    /// <summary>Fit-width 460 DIP at 150 % scaling on a Letter page — the Phase 34A preview viewport (906 × 1172 px).</summary>
    public const double PreviewPixelsPerPoint = 460.0 / 612.0 * (96.0 / 72.0) * 1.5;

    public static AssociateWorkspace Workspace(int notes = 2) =>
        FakeAssociatePassdownService.DefaultWorkspace(Date) with
        {
            Draft = AssociateDraftPayload.CreateDefault() with
            {
                ProjectedAttendance = 23456,
                PreSolds = 7890,
                OperationalNotes = [.. Enumerable.Range(1, notes).Select(i => $"Synthetic operational note {i}: a deliberately long line so long documents continue onto another page.")],
                DailyFeatureText = "Synthetic daily feature.",
            },
        };
}

internal sealed class InMemoryTemplateStore(AssociatePassdownTemplate template) : IAssociatePassdownTemplatePreferenceStore
{
    public Task<AssociatePassdownTemplatePreference> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AssociatePassdownTemplatePreference(template));

    public Task SaveAsync(AssociatePassdownTemplatePreference preference, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// First-PDF cost in fresh processes. "cold" = today's first real PDF with nothing warmed; "warmed" = the
/// background warm-up ran to completion first (the user's first PDF action comes after the UI went idle);
/// "concurrent" = the warm-up and the first real PDF start at the same instant (worst case).
/// </summary>
internal static class PdfStartupBenchmark
{
    public static int RunSuite(int runs)
    {
        Console.WriteLine();
        Console.WriteLine($"## pdf-startup — {runs} fresh processes per mode (Classic template, busy synthetic day)");
        var self = Environment.ProcessPath!;
        var assembly = typeof(PdfStartupBenchmark).Assembly.Location;
        var isDotnetHost = Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

        foreach (var mode in new[] { "cold", "warmed", "concurrent" })
        {
            var samples = new List<Dictionary<string, double>>();
            for (var i = 0; i < runs; i++)
            {
                var start = new ProcessStartInfo(self) { RedirectStandardOutput = true, UseShellExecute = false };
                if (isDotnetHost)
                {
                    start.ArgumentList.Add(assembly);
                }

                start.ArgumentList.Add("pdf-startup-child");
                start.ArgumentList.Add(mode);
                using var process = Process.Start(start)!;
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                var line = output.Split('\n').FirstOrDefault(l => l.StartsWith("RESULT ", StringComparison.Ordinal))
                    ?? throw new InvalidOperationException($"child produced no result: {output}");
                samples.Add(line[7..].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(pair => pair.Split('='))
                    .ToDictionary(kv => kv[0], kv => double.Parse(kv[1], CultureInfo.InvariantCulture)));
            }

            Console.WriteLine($"### {mode}");
            foreach (var key in samples[0].Keys)
            {
                Console.WriteLine($"  {key,-26} {Stats.Summary(samples.Select(s => s[key]).ToList())}");
            }
        }

        return 0;
    }

    public static int RunChild(string mode)
    {
        var results = new Dictionary<string, double>();
        var processStart = Process.GetCurrentProcess().StartTime.ToUniversalTime();
        var generator = new AssociatePassdownPdfGenerator();
        var export = new AssociatePassdownPdfExportService(generator);
        var rasterizer = new PdfiumPdfRasterizer();
        var workspace = SyntheticPassdown.Workspace();

        if (mode is "warmed" or "concurrent")
        {
            var warmUp = new PdfEngineWarmUp(generator, rasterizer, new InMemoryTemplateStore(AssociatePassdownTemplate.Classic), isRasterizerWarm: () => PdfiumPdfRasterizer.IsLibraryLoaded);
            var trySync = Stats.Time(() => warmUp.TryStart());
            results["tryStartCallerMs"] = trySync; // the only cost on the thread that starts it (the UI thread in the app)
            if (mode == "warmed")
            {
                var waited = Stopwatch.StartNew();
                warmUp.Completion.GetAwaiter().GetResult();
                results["warmUpBackgroundMs"] = waited.Elapsed.TotalMilliseconds + trySync;
            }
        }

        byte[] bytes = [];
        results["firstGenerateMs"] = Stats.Time(() => bytes = export.GeneratePdf(workspace, SyntheticPassdown.GeneratedAt, AssociatePassdownTemplate.Classic).PdfBytes!);
        IPdfRasterDocument? document = null;
        results["firstOpenRenderMs"] = Stats.Time(() =>
        {
            document = rasterizer.Open(bytes);
            _ = document.RenderPage(0, SyntheticPassdown.PreviewPixelsPerPoint);
        });
        document!.Dispose();
        results["firstPreviewWorkMs"] = results["firstGenerateMs"] + results["firstOpenRenderMs"];

        // The next, warm PDF for comparison (another template exercises another renderer).
        results["secondGenerateMs"] = Stats.Time(() => bytes = export.GeneratePdf(workspace, SyntheticPassdown.GeneratedAt, AssociatePassdownTemplate.Classic).PdfBytes!);
        results["firstModernGenerateMs"] = Stats.Time(() => export.GeneratePdf(workspace, SyntheticPassdown.GeneratedAt, AssociatePassdownTemplate.Modern));
        results["processAgeAtEndMs"] = (DateTime.UtcNow - processStart).TotalMilliseconds;

        Console.WriteLine("RESULT " + string.Join(' ', results.Select(r => string.Create(CultureInfo.InvariantCulture, $"{r.Key}={r.Value:F3}"))));
        return 0;
    }
}

/// <summary>Repeated preview page renders: the Phase 33 copy path versus the Phase 34B-2 direct-to-bitmap path.</summary>
internal static class PdfRenderBenchmark
{
    public static int Run(int iterations)
    {
        Console.WriteLine();
        Console.WriteLine($"## pdf-render — {iterations} renders per path, page 1 at fit-width 460 DIP @150 % ({SyntheticPassdown.PreviewPixelsPerPoint:F3} px/pt)");
        var export = new AssociatePassdownPdfExportService(new AssociatePassdownPdfGenerator());
        var factory = new AvaloniaPdfPageImageFactory();
        var rasterizer = new PdfiumPdfRasterizer();

        foreach (var (label, notes) in new[] { ("busy (1 page)", 2), ("80 notes (multi-page)", 80) })
        {
            var bytes = export.GeneratePdf(SyntheticPassdown.Workspace(notes), SyntheticPassdown.GeneratedAt, AssociatePassdownTemplate.Classic).PdfBytes!;
            using var document = rasterizer.Open(bytes);
            Console.WriteLine($"### {label}");
            Measure("copy (RC.12)  ", () => factory.Create(document.RenderPage(0, SyntheticPassdown.PreviewPixelsPerPoint)), iterations);
            Measure("direct (34B-2)", () => factory.Render(document, 0, SyntheticPassdown.PreviewPixelsPerPoint), iterations);
        }

        return 0;
    }

    private static void Measure(string label, Func<Avalonia.Media.IImage> render, int iterations)
    {
        for (var i = 0; i < 10; i++)
        {
            (render() as IDisposable)?.Dispose();
        }

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var managedBefore = GC.GetTotalMemory(forceFullCollection: true);
        var gen2Before = GC.CollectionCount(2);
        var gen0Before = GC.CollectionCount(0);
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var times = new List<double>(iterations);
        for (var i = 0; i < iterations; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            var image = render();
            stopwatch.Stop();
            times.Add(stopwatch.Elapsed.TotalMilliseconds);
            (image as IDisposable)?.Dispose();
        }

        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        var gen2 = GC.CollectionCount(2) - gen2Before;
        var gen0 = GC.CollectionCount(0) - gen0Before;
        var managedAfter = GC.GetTotalMemory(forceFullCollection: true);
        using var process = Process.GetCurrentProcess();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  {label} time {Stats.Summary(times)} | allocated {allocated / (double)iterations / 1024:F1} KB/render | gen-2 GCs {gen2} ({gen2 / (double)iterations:F3}/render) | gen-0 GCs {gen0} | managed heap {managedBefore / 1048576.0:F1} → {managedAfter / 1048576.0:F1} MB | working set {process.WorkingSet64 / 1048576.0:F0} MB"));
    }
}

/// <summary>
/// Investigation only (no production change): the Windows print path's per-page work, reproduced off Windows —
/// 600 DPI Letter, 16 MiB bands, PDFium band render + BGRA→BGR24 conversion, exactly as WindowsPdfPrintService
/// does before handing each band to GDI. GDI spooling itself, printer drivers and GDI handles are Windows-only.
/// </summary>
internal static class PrintBandBenchmark
{
    public static int Run(int pagesPerSample)
    {
        Console.WriteLine();
        Console.WriteLine($"## print-bands — 600 DPI Letter band loop (render + BGR24), {pagesPerSample} pages per document, no GDI");
        var export = new AssociatePassdownPdfExportService(new AssociatePassdownPdfGenerator());
        var renderBand = typeof(PdfiumPdfRasterizer).GetMethod("RenderBand", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        const int flags = 0x01 | 0x800; // FPDF_ANNOT | FPDF_PRINTING, as the print service passes

        foreach (var (label, notes) in new[] { ("busy (1 page)", 2), ("80 notes (multi-page)", 80) })
        {
            var bytes = export.GeneratePdf(SyntheticPassdown.Workspace(notes), SyntheticPassdown.GeneratedAt, AssociatePassdownTemplate.Classic).PdfBytes!;
            using var document = new PdfiumPdfRasterizer().Open(bytes);
            var handle = (IntPtr)document.GetType().GetProperty("Handle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(document)!;
            var size = document.GetPageSize(0);
            var renderWidth = (int)Math.Round(size.WidthPoints / 72.0 * PrintLayoutMath.MaxRenderDpi);
            var renderHeight = (int)Math.Round(size.HeightPoints / 72.0 * PrintLayoutMath.MaxRenderDpi);
            var bandHeight = PrintLayoutMath.BandHeightFor(renderWidth);
            var bands = (renderHeight + bandHeight - 1) / bandHeight;

            void PrintPage(int pageIndex)
            {
                for (var top = 0; top < renderHeight; top += bandHeight)
                {
                    var rows = Math.Min(bandHeight, renderHeight - top);
                    var band = (PdfRasterImage)renderBand.Invoke(null, [handle, pageIndex, renderWidth, renderHeight, top, rows, flags])!;
                    _ = PrintLayoutMath.ToBgr24(band.Bgra, band.PixelWidth, band.PixelHeight, band.Stride, out _);
                }
            }

            PrintPage(0); // warm-up
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            var gen2Before = GC.CollectionCount(2);
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var times = new List<double>();
            for (var p = 0; p < pagesPerSample; p++)
            {
                times.Add(Stats.Time(() => PrintPage(p % document.PageCount)));
            }

            var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            using var process = Process.GetCurrentProcess();
            Console.WriteLine($"### {label}: {renderWidth}×{renderHeight} px, {bands} bands of ≤{bandHeight} rows");
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  per page {Stats.Summary(times)} | managed allocated {allocated / (double)pagesPerSample / 1048576:F0} MB/page | gen-2 GCs {GC.CollectionCount(2) - gen2Before} | working set now {process.WorkingSet64 / 1048576.0:F0} MB"));
        }

        return 0;
    }
}

/// <summary>
/// Edit → new page image through the real <see cref="Carowinds.Passdown.Desktop.ViewModels.PassdownPdfPreviewViewModel"/>
/// (production leading edge + 200 ms quiet window, real QuestPDF, PDFium, Skia bitmaps and thread-pool scheduler).
/// Each isolated edit waits 300 ms after the previous one, as a separate click would. No window, so the compositor's
/// one frame is not included.
/// </summary>
internal static class PreviewLatencyBenchmark
{
    public static async Task<int> RunAsync(int samples)
    {
        Console.WriteLine();
        Console.WriteLine($"## preview — isolated edit → page image, {samples} edits per document (real preview view model)");
        foreach (var (label, notes) in new[] { ("busy (1 page)", 2), ("80 notes (multi-page)", 80) })
        {
            var workspace = SyntheticPassdown.Workspace(notes);
            var preview = new Carowinds.Passdown.Desktop.ViewModels.PassdownPdfPreviewViewModel(
                () => Carowinds.Passdown.Desktop.ViewModels.PassdownPdfCapture.Ready(new(workspace, SyntheticPassdown.Date, AssociatePassdownTemplate.Classic)),
                () => true,
                new AssociatePassdownPdfExportService(new AssociatePassdownPdfGenerator()),
                new PdfiumPdfRasterizer(),
                new FakePdfPrintService(),
                new FakePdfSaveFilePicker(),
                new InMemoryPdfPreviewLayoutPreferenceStore());
            await preview.PendingPreferenceLoad;
            preview.UpdateAvailableWidth(1400);
            preview.UpdateViewport(460, 700, 1.5);

            var latencies = new List<double>();
            for (var i = -3; i < samples; i++)
            {
                await Task.Delay(300);
                var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
                {
                    if (e.PropertyName == nameof(preview.PageImage) && preview.PageImage is not null)
                    {
                        changed.TrySetResult();
                    }
                }

                preview.PropertyChanged += OnChanged;
                workspace = workspace with { Draft = workspace.Draft with { ProjectedAttendance = 20000 + i + 10 } };
                var stopwatch = Stopwatch.StartNew();
                preview.NotifyContentChanged();
                await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                stopwatch.Stop();
                preview.PropertyChanged -= OnChanged;
                if (i >= 0)
                {
                    latencies.Add(stopwatch.Elapsed.TotalMilliseconds);
                }
            }

            Console.WriteLine($"  {label,-22} {Stats.Summary(latencies)}");
        }

        return 0;
    }
}
