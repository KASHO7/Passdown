using System.Runtime.InteropServices;
using Avalonia.Media;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.PdfOutput;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Infrastructure.PdfOutput;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 34B-2: the preview renders PDFium output straight into the bitmap's own pixel memory instead of a
/// per-render managed array plus a copy. These tests use the real QuestPDF export and the real PDFium
/// rasterizer (offline) and prove the direct path produces byte-identical pixels for every template, page
/// and scale, and that it is safe when the document is disposed, the call is cancelled or the target is wrong.
/// </summary>
public class Phase34B2DirectPdfRenderingTests
{
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero);
    private static readonly AssociatePassdownPdfExportService Export = new(new AssociatePassdownPdfGenerator());

    private static AssociateWorkspace Day(int notes) =>
        FakeAssociatePassdownService.DefaultWorkspace(Today) with
        {
            Draft = AssociateDraftPayload.CreateDefault() with
            {
                ProjectedAttendance = 23456,
                PreSolds = 7890,
                OperationalNotes = [.. Enumerable.Range(1, notes).Select(i => $"Operational note {i}: a deliberately long line so long documents continue onto another page.")],
                DailyFeatureText = "Fury 325 reaches 95 mph.",
            },
        };

    private static byte[] RenderDirect(IPdfDirectRasterDocument document, int page, double pixelsPerPoint, int extraStrideBytes)
    {
        var size = document.GetRenderPixelSize(page, pixelsPerPoint);
        var stride = (size.Width * 4) + extraStrideBytes;
        var buffer = new byte[stride * size.Height];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            document.RenderPageInto(page, pixelsPerPoint, handle.AddrOfPinnedObject(), stride);
        }
        finally
        {
            handle.Free();
        }

        return buffer;
    }

    private static void AssertSamePixels(PdfRasterImage expected, byte[] actual, int actualStride, string label)
    {
        var rowBytes = expected.PixelWidth * 4;
        for (var row = 0; row < expected.PixelHeight; row++)
        {
            var expectedRow = expected.Bgra.AsSpan(row * expected.Stride, rowBytes);
            var actualRow = actual.AsSpan(row * actualStride, rowBytes);
            Assert.True(expectedRow.SequenceEqual(actualRow), $"{label}: row {row} differs.");
        }
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(80, 0)]
    [InlineData(2, 64)] // a bitmap whose rows are padded wider than the page
    public void DirectRender_IsPixelIdentical_ForEveryTemplatePageAndScale(int notes, int extraStrideBytes)
    {
        var rasterizer = new PdfiumPdfRasterizer();
        foreach (var template in Enum.GetValues<AssociatePassdownTemplate>())
        {
            var bytes = Export.GeneratePdf(Day(notes), GeneratedAt, template).PdfBytes!;
            using var document = Assert.IsAssignableFrom<IPdfDirectRasterDocument>(rasterizer.Open(bytes));
            for (var page = 0; page < document.PageCount; page++)
            {
                foreach (var scale in new[] { 0.37, 1.0, 1.5, 2.08 })
                {
                    var expected = document.RenderPage(page, scale);
                    var size = document.GetRenderPixelSize(page, scale);
                    Assert.Equal(new PdfPixelSize(expected.PixelWidth, expected.PixelHeight), size);

                    var actual = RenderDirect(document, page, scale, extraStrideBytes);
                    AssertSamePixels(expected, actual, (size.Width * 4) + extraStrideBytes, $"{template} page {page + 1} @{scale}");
                }
            }
        }
    }

    [Fact]
    public void DirectRender_PaddingBytesBeyondThePageWidth_AreNeverWritten()
    {
        var bytes = Export.GeneratePdf(Day(2), GeneratedAt).PdfBytes!;
        using var document = (IPdfDirectRasterDocument)new PdfiumPdfRasterizer().Open(bytes);
        var size = document.GetRenderPixelSize(0, 0.5);
        var stride = (size.Width * 4) + 16;
        var buffer = Enumerable.Repeat((byte)0xAB, stride * size.Height).ToArray();
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            document.RenderPageInto(0, 0.5, handle.AddrOfPinnedObject(), stride);
        }
        finally
        {
            handle.Free();
        }

        for (var row = 0; row < size.Height; row++)
        {
            Assert.All(buffer.AsSpan((row * stride) + (size.Width * 4), 16).ToArray(), b => Assert.Equal(0xAB, b));
        }
    }

    [Fact]
    public void DirectRender_AfterDispose_Throws_AndTouchesNothing()
    {
        var bytes = Export.GeneratePdf(Day(2), GeneratedAt).PdfBytes!;
        var document = (IPdfDirectRasterDocument)new PdfiumPdfRasterizer().Open(bytes);
        var size = document.GetRenderPixelSize(0, 0.5);
        document.Dispose();

        var buffer = new byte[size.Width * 4 * size.Height];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            Assert.Throws<ObjectDisposedException>(() => document.RenderPageInto(0, 0.5, handle.AddrOfPinnedObject(), size.Width * 4));
        }
        finally
        {
            handle.Free();
        }

        Assert.All(buffer, b => Assert.Equal(0, b));
    }

    [Fact]
    public void DirectRender_Cancelled_BadStride_OrNullTarget_IsRefusedBeforeRendering()
    {
        var bytes = Export.GeneratePdf(Day(2), GeneratedAt).PdfBytes!;
        using var document = (IPdfDirectRasterDocument)new PdfiumPdfRasterizer().Open(bytes);
        var size = document.GetRenderPixelSize(0, 0.5);
        var buffer = new byte[size.Width * 4 * size.Height];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var address = handle.AddrOfPinnedObject();
            Assert.Throws<OperationCanceledException>(() => document.RenderPageInto(0, 0.5, address, size.Width * 4, cancellationToken: new CancellationToken(canceled: true)));
            Assert.Throws<ArgumentOutOfRangeException>(() => document.RenderPageInto(0, 0.5, address, (size.Width * 4) - 1));
            Assert.Throws<ArgumentNullException>(() => document.RenderPageInto(0, 0.5, IntPtr.Zero, size.Width * 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => document.RenderPageInto(5, 0.5, address, size.Width * 4));
        }
        finally
        {
            handle.Free();
        }

        Assert.All(buffer, b => Assert.Equal(0, b));
    }

    [Fact]
    public void RenderPixelSize_IsClampedLikeRenderPage()
    {
        var bytes = Export.GeneratePdf(Day(2), GeneratedAt).PdfBytes!;
        using var document = (IPdfDirectRasterDocument)new PdfiumPdfRasterizer().Open(bytes);

        var huge = document.GetRenderPixelSize(0, 1000);
        Assert.Equal(PdfiumPdfRasterizer.MaxPixelDimension, huge.Width);
        Assert.Equal(PdfiumPdfRasterizer.MaxPixelDimension, huge.Height);
        Assert.Throws<ArgumentOutOfRangeException>(() => document.GetRenderPixelSize(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.GetRenderPixelSize(0, double.NaN));
    }

    [Fact]
    public void ConcurrentDirectAndCopyRenders_StaySerializedAndIdentical()
    {
        var bytes = Export.GeneratePdf(Day(2), GeneratedAt).PdfBytes!;
        using var document = (IPdfDirectRasterDocument)new PdfiumPdfRasterizer().Open(bytes);
        var expected = document.RenderPage(0, 0.75);

        Parallel.For(0, 24, i =>
        {
            if (i % 2 == 0)
            {
                AssertSamePixels(expected, RenderDirect(document, 0, 0.75, 0), expected.PixelWidth * 4, $"direct {i}");
            }
            else
            {
                Assert.Equal(expected.Bgra, document.RenderPage(0, 0.75).Bgra);
            }
        });
    }

    private sealed class CreateOnlyFactory : IPdfPageImageFactory
    {
        public List<PdfRasterImage> Pages { get; } = [];

        public IImage Create(PdfRasterImage page)
        {
            Pages.Add(page);
            return new FakePdfPageImageFactory.FakePageImage(page);
        }
    }

    [Fact]
    public void AFactoryWithoutDirectRendering_FallsBackToRenderAndCopy()
    {
        var bytes = Export.GeneratePdf(Day(2), GeneratedAt).PdfBytes!;
        using var document = new PdfiumPdfRasterizer().Open(bytes);
        IPdfPageImageFactory factory = new CreateOnlyFactory();

        var image = factory.Render(document, 0, 0.5);

        var page = Assert.Single(((CreateOnlyFactory)factory).Pages);
        Assert.Equal(document.RenderPage(0, 0.5).Bgra, page.Bgra);
        Assert.IsType<FakePdfPageImageFactory.FakePageImage>(image);
    }
}
