using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.PdfOutput;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Infrastructure.PdfOutput;
using Carowinds.Passdown.Ui.Tests.TestSupport;
using UglyToad.PdfPig;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 33: the real pipeline end to end, offline — the production QuestPDF export service generates
/// each template, and the production PDFium rasterizer renders those exact bytes. Proves the preview
/// shows the actual generated PDF (page count and size agree with an independent PDF reader) for every
/// template the application defines, including any added later.
/// </summary>
public class Phase33RealPdfRenderingTests
{
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero);

    private static readonly AssociatePassdownPdfExportService Export = new(new AssociatePassdownPdfGenerator());

    private static AssociateWorkspace NormalDay() =>
        FakeAssociatePassdownService.DefaultWorkspace(Today) with
        {
            Draft = AssociateDraftPayload.CreateDefault() with
            {
                ProjectedAttendance = 23456,
                PreSolds = 7890,
                OperationalNotes = ["Lot B closed for paving.", "Fury 325 opens at 11:00."],
                DailyFeatureText = "Fury 325 reaches 95 mph.",
            },
        };

    private static AssociateWorkspace LongNotesDay() =>
        NormalDay() with
        {
            Draft = NormalDay().Draft with
            {
                OperationalNotes = Enumerable.Range(1, 80).Select(i => $"Operational note {i}: a deliberately long line of text so the passdown has to continue onto another page rather than truncating anything.").ToList(),
            },
        };

    // 2 / 3 / template detection: every defined template (Classic, Compact, Modern, and any future one) renders.
    [Fact]
    public void EveryTemplate_GeneratesAndRendersTheActualPdf()
    {
        var rasterizer = new PdfiumPdfRasterizer();
        var templates = Enum.GetValues<AssociatePassdownTemplate>();
        Assert.Contains(AssociatePassdownTemplate.Classic, templates);
        Assert.Contains(AssociatePassdownTemplate.Modern, templates);
        Assert.Equal(templates.Length, AssociatePassdownTemplateOption.All.Count); // every template is selectable

        var fingerprints = new HashSet<string>();
        foreach (var template in templates)
        {
            var result = Export.GeneratePdf(NormalDay(), GeneratedAt, template);
            var bytes = Assert.IsType<byte[]>(result.PdfBytes);
            fingerprints.Add(PdfContentFingerprint.Compute(bytes));

            using var reader = PdfDocument.Open(bytes);
            using var document = rasterizer.Open(bytes);
            Assert.Equal(reader.NumberOfPages, document.PageCount);
            Assert.Equal(result.PageCount, document.PageCount);

            var size = document.GetPageSize(0);
            Assert.Equal(reader.GetPage(1).Width, size.WidthPoints, 1);
            Assert.Equal(reader.GetPage(1).Height, size.HeightPoints, 1);

            var image = document.RenderPage(0, 1.0);
            Assert.Equal((int)Math.Round(size.WidthPoints), image.PixelWidth);
            Assert.Equal((int)Math.Round(size.HeightPoints), image.PixelHeight);
            Assert.True(CountInkPixels(image) > 500, $"{template}: the rendered page is blank.");
        }

        Assert.Equal(templates.Length, fingerprints.Count); // each template is visibly its own layout
    }

    // Multi-page documents render every page, at the size the PDF declares.
    [Fact]
    public void MultiPagePassdown_RendersEveryPage()
    {
        var result = Export.GeneratePdf(LongNotesDay(), GeneratedAt, AssociatePassdownTemplate.Classic);
        Assert.True(result.PageCount > 1, "fixture should overflow onto a continuation page");

        using var document = new PdfiumPdfRasterizer().Open(result.PdfBytes!);
        Assert.Equal(result.PageCount, document.PageCount);
        for (var page = 0; page < document.PageCount; page++)
        {
            Assert.True(CountInkPixels(document.RenderPage(page, 0.5)) > 100, $"page {page + 1} is blank");
        }
    }

    [Fact]
    public void Rasterizer_RejectsBytesThatAreNotAPdf()
    {
        var rasterizer = new PdfiumPdfRasterizer();
        Assert.Throws<PdfRasterizationException>(() => rasterizer.Open("not a pdf"u8.ToArray()));
        Assert.Throws<PdfRasterizationException>(() => rasterizer.Open([]));
    }

    [Fact]
    public void Rasterizer_DisposedDocument_RefusesFurtherUse()
    {
        var bytes = Export.GeneratePdf(NormalDay(), GeneratedAt).PdfBytes!;
        var document = new PdfiumPdfRasterizer().Open(bytes);
        document.Dispose();
        document.Dispose(); // idempotent

        Assert.Throws<ObjectDisposedException>(() => document.RenderPage(0, 1.0));
    }

    [Fact]
    public async Task Rasterizer_IsSafeUnderConcurrentUse()
    {
        var bytes = Export.GeneratePdf(NormalDay(), GeneratedAt).PdfBytes!;
        var rasterizer = new PdfiumPdfRasterizer();

        var renders = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            using var document = rasterizer.Open(bytes);
            return CountInkPixels(document.RenderPage(0, 0.5));
        }));
        var counts = await Task.WhenAll(renders);

        Assert.All(counts, count => Assert.Equal(counts[0], count));
    }

    // 25 / byte consistency: the preview's document, Print and Save receive the identical bytes.
    [Fact]
    public async Task PreviewPrintAndSave_ShareTheIdenticalGeneratedBytes()
    {
        var printer = new FakePdfPrintService();
        var picker = new FakePdfSaveFilePicker();
        var debounce = new ManualDebounceScheduler();
        var workspace = NormalDay();
        var preview = new PassdownPdfPreviewViewModel(
            () => PassdownPdfCapture.Ready(new PassdownPdfRequest(workspace, Today, AssociatePassdownTemplate.Modern)),
            () => true,
            Export,
            new PdfiumPdfRasterizer(),
            printer,
            picker,
            new InMemoryPdfPreviewLayoutPreferenceStore(),
            new FakePdfPageImageFactory(),
            new InlinePreviewWorkScheduler(),
            delay: debounce.Delay,
            now: () => GeneratedAt);
        preview.UpdateAvailableWidth(1500);
        preview.UpdateViewport(460, 700, 1.25);

        preview.NotifyContentChanged();
        debounce.ElapseLatest();
        await preview.PendingRefresh;
        await preview.PrintCommand.ExecuteAsync(null);
        await preview.SavePdfCommand.ExecuteAsync(null);

        Assert.Equal(PdfPreviewStatus.Ready, preview.Status);
        var shown = preview.CurrentDocument!.Bytes;
        Assert.Same(shown, printer.Requests.Single().PdfBytes);
        Assert.Same(shown, picker.Calls.Single().Bytes);

        // And those bytes are exactly what the export pipeline (Create PDF's) produces for the same state.
        var direct = Export.GeneratePdf(workspace, GeneratedAt, AssociatePassdownTemplate.Modern).PdfBytes!;
        Assert.Equal(PdfContentFingerprint.Compute(direct), PdfContentFingerprint.Compute(shown));

        // Fit-width at 125% display scaling renders at device resolution, not a blurry 96-DPI bitmap.
        var image = Assert.IsType<FakePdfPageImageFactory.FakePageImage>(preview.PageImage);
        Assert.Equal(Math.Round(preview.PageDisplayWidth * 1.25), image.Page.PixelWidth, 0);
        Assert.Equal(PdfPreviewZoom.FitWidth(new PdfPageSize(612, 792), 460), preview.Zoom, 3);
    }

    private static int CountInkPixels(PdfRasterImage image)
    {
        var ink = 0;
        for (var row = 0; row < image.PixelHeight; row++)
        {
            for (var column = 0; column < image.PixelWidth; column++)
            {
                var offset = (row * image.Stride) + (column * 4);
                if (image.Bgra[offset] < 200 || image.Bgra[offset + 1] < 200 || image.Bgra[offset + 2] < 200)
                {
                    ink++;
                }
            }
        }

        return ink;
    }
}
