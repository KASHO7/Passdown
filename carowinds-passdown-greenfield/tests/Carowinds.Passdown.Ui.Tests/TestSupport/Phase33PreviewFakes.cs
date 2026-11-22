using Avalonia;
using Avalonia.Media;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.PdfOutput;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Documents.Associate;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Phase 33: a rasterizer that never touches PDFium — page count and size are scripted per opened byte array.</summary>
internal sealed class FakePdfRasterizer : IPdfRasterizer
{
    public Func<byte[], int> PageCountFor { get; set; } = _ => 1;

    public Func<byte[], Exception?> OpenFailure { get; set; } = _ => null;

    public PdfPageSize PageSize { get; set; } = new(612, 792);

    public List<byte[]> Opened { get; } = [];

    public List<FakeRasterDocument> Documents { get; } = [];

    public IPdfRasterDocument Open(byte[] pdfBytes)
    {
        Opened.Add(pdfBytes);
        if (OpenFailure(pdfBytes) is { } failure)
        {
            throw failure;
        }

        var document = new FakeRasterDocument(PageCountFor(pdfBytes), PageSize);
        Documents.Add(document);
        return document;
    }

    internal sealed class FakeRasterDocument(int pageCount, PdfPageSize size) : IPdfRasterDocument
    {
        public bool IsDisposed { get; private set; }

        public List<(int Page, double PixelsPerPoint)> Renders { get; } = [];

        public int PageCount { get; } = pageCount;

        public PdfPageSize GetPageSize(int pageIndex)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return size;
        }

        public PdfRasterImage RenderPage(int pageIndex, double pixelsPerPoint, PdfRenderPurpose purpose = PdfRenderPurpose.Display, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            Renders.Add((pageIndex, pixelsPerPoint));
            var width = Math.Max(1, (int)(size.WidthPoints * pixelsPerPoint));
            var height = Math.Max(1, (int)(size.HeightPoints * pixelsPerPoint));
            return new PdfRasterImage(width, height, width * 4, []);
        }

        public void Dispose() => IsDisposed = true;
    }
}

/// <summary>Phase 33: an image factory that needs no rendering platform; tracks disposal.</summary>
internal sealed class FakePdfPageImageFactory : IPdfPageImageFactory
{
    public List<FakePageImage> Created { get; } = [];

    public IImage Create(PdfRasterImage page)
    {
        var image = new FakePageImage(page);
        Created.Add(image);
        return image;
    }

    internal sealed class FakePageImage(PdfRasterImage page) : IImage, IDisposable
    {
        public PdfRasterImage Page { get; } = page;

        public bool IsDisposed { get; private set; }

        public Size Size => new(Page.PixelWidth, Page.PixelHeight);

        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
        {
        }

        public void Dispose() => IsDisposed = true;
    }
}

/// <summary>Phase 33: runs preview work inline (deterministic), optionally holding selected calls behind a gate.</summary>
internal sealed class InlinePreviewWorkScheduler : IPreviewWorkScheduler
{
    private readonly Queue<TaskCompletionSource> _gates = new();

    /// <summary>The next <see cref="Run{T}"/> waits until the returned gate is released (it then runs the work).</summary>
    public TaskCompletionSource HoldNext()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _gates.Enqueue(gate);
        return gate;
    }

    public Task<T> Run<T>(Func<T> work)
    {
        if (_gates.Count > 0)
        {
            var gate = _gates.Dequeue();
            return gate.Task.ContinueWith(_ => work(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        try
        {
            return Task.FromResult(work());
        }
        catch (Exception ex)
        {
            return Task.FromException<T>(ex);
        }
    }
}

/// <summary>Phase 33: scriptable print service — never prints.</summary>
internal sealed class FakePdfPrintService : IPdfPrintService
{
    public List<PdfPrintRequest> Requests { get; } = [];

    public Func<PdfPrintRequest, PdfPrintResult> Result { get; set; } = request => PdfPrintResult.Printed("Microsoft Print to PDF", request.PageCount);

    public Exception? Throws { get; set; }

    public bool IsSupported { get; set; } = true;

    public Task<PdfPrintResult> PrintAsync(PdfPrintRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (Throws is { } exception)
        {
            throw exception;
        }

        return Task.FromResult(Result(request));
    }
}

/// <summary>Phase 33: an export service whose bytes change with the workspace content, like the real one, so fingerprints differ when content differs.</summary>
internal sealed class ContentEchoPdfExportService : IAssociatePassdownPdfExportService
{
    public List<(AssociateWorkspace Workspace, AssociatePassdownTemplate Template)> Calls { get; } = [];

    public Func<AssociateWorkspace, AssociatePassdownTemplate, AssociatePassdownPdfResult>? Override { get; set; }

    public Exception? Throws { get; set; }

    public int PageCount { get; set; } = 1;

    public AssociatePassdownPdfResult GeneratePdf(AssociateWorkspace workspace, DateTimeOffset generatedAt, AssociatePassdownTemplate template = AssociatePassdownTemplate.Classic, CancellationToken cancellationToken = default)
    {
        Calls.Add((workspace, template));
        if (Throws is { } exception)
        {
            throw exception;
        }

        if (Override is { } custom)
        {
            return custom(workspace, template);
        }

        var text = $"%PDF-1.7 {workspace.OperatingDate:yyyy-MM-dd} {template} attendance={workspace.Draft.ProjectedAttendance} notes={string.Join("|", workspace.Draft.OperationalNotes)} feature={workspace.Draft.DailyFeatureText} sections={workspace.Draft.SectionSelection} include-shows={workspace.Draft.Shows?.IncludeInPassdown} shows={workspace.Draft.Shows?.SelectedShows.Count ?? 0} products={string.Join(",", workspace.Products.Where(p => p.IncludeOnPdf).Select(p => p.ProductIdentity))} pages={PageCount}";
        return new AssociatePassdownPdfResult(
            AssociatePassdownPdfStatus.Generated,
            System.Text.Encoding.UTF8.GetBytes(text),
            new AssociatePassdownPreflightResult(AssociatePassdownPreflightStatus.FitsOnePage, []),
            PageCount);
    }
}
