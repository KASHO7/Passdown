using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Carowinds.Passdown.Application.PdfOutput;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 33: the side-by-side layout in a real (headless, off-screen) window — the preview pane keeps its
/// place across Overview/Products/Shows/Notes, nothing clips at wide, default, narrow and minimum-preview
/// widths, Hide gives the editor the full width, a remembered width is clamped to a smaller window, and a
/// narrow window shows one pane at a time. RUN ALONE (like Phase15ERealWindowLayoutTests): real-Window
/// tests share the headless dispatcher thread; see docs/release-manifest.md for the deterministic partition.
/// Headless font metrics are approximate — Windows display scaling (100/125/150%) is a manual check.
/// </summary>
public partial class Phase33PreviewWindowTests
{
    private static readonly DateOnly FixedDate = new(2026, 8, 21);

    private sealed record Realized(Window Window, AssociateView View, AssociateViewModel ViewModel, ManualDebounceScheduler Debounce);

    private static async Task<Realized> RealizeAsync(double width, double height, PdfPreviewLayoutPreferences? preferences = null, int pages = 1)
    {
        var debounce = new ManualDebounceScheduler();
        var rasterizer = new FakePdfRasterizer { PageCountFor = _ => pages };
        var viewModel = new AssociateViewModel(
            new FakeAssociatePassdownService(),
            new FakeClock(new DateTimeOffset(FixedDate.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero)),
            autosaveDelay: new ManualDebounceScheduler().Delay,
            pdfExportService: new ContentEchoPdfExportService { PageCount = pages },
            templatePreferenceStore: new FakeAssociatePassdownTemplatePreferenceStore(),
            pdfRasterizer: rasterizer,
            pdfPrintService: new FakePdfPrintService(),
            pdfPreviewLayoutStore: new InMemoryPdfPreviewLayoutPreferenceStore(preferences),
            pdfPageImageFactory: new FakePdfPageImageFactory(),
            previewWorkScheduler: new InlinePreviewWorkScheduler(),
            previewDelay: debounce.Delay);
        await viewModel.PendingLoad;
        await viewModel.Preview.PendingPreferenceLoad;

        var view = new AssociateView { DataContext = viewModel };
        var window = new Window { Width = width, Height = height, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        debounce.ElapseLatest();
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }

        return new Realized(window, view, viewModel, debounce);
    }

    private static Control Named(Window window, string name) =>
        window.GetVisualDescendants().OfType<Control>().First(c => c.Name == name);

    private static Rect AbsoluteBounds(Visual control, Window window) =>
        new(control.TranslatePoint(new Point(0, 0), window) ?? default, control.Bounds.Size);

    private static void AssertNoControlClipsHorizontally(Window window)
    {
        var clientWidth = window.ClientSize.Width;
        foreach (var control in window.GetVisualDescendants().OfType<Button>().Cast<Control>().Concat(window.GetVisualDescendants().OfType<TextBox>()))
        {
            if (!control.IsEffectivelyVisible || (control.Bounds.Width <= 0 && control.Bounds.Height <= 0))
            {
                continue;
            }

            var bounds = AbsoluteBounds(control, window);
            var label = AutomationProperties.GetName(control) ?? (control as Button)?.Content as string ?? control.GetType().Name;
            Assert.True(bounds.Right <= clientWidth + 1.0, $"'{label}' right edge {bounds.Right} exceeds window client width {clientWidth}.");
        }
    }

    private static void AssertPreviewToolbarFitsThePane(Window window)
    {
        var pane = AbsoluteBounds(Named(window, "PreviewPaneHost"), window);
        foreach (var button in Named(window, "PreviewPane").GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
        {
            var bounds = AbsoluteBounds(button, window);
            var label = AutomationProperties.GetName(button) ?? button.Content as string;
            Assert.True(bounds.Width > 0, $"preview control '{label}' has no width");
            Assert.True(bounds.X >= pane.X - 1 && bounds.Right <= pane.Right + 1, $"preview control '{label}' ({bounds}) is outside the pane ({pane}).");
        }
    }

    [Theory]
    [InlineData(1920d, 1000d)]
    [InlineData(1440d, 900d)]
    [InlineData(1280d, 800d)]
    public async Task WideWindows_ShowEditorAndPreviewSideBySide_WithoutClipping(double width, double height)
    {
        var r = await RealizeAsync(width, height);

        Assert.Equal(PdfPreviewPaneMode.Split, r.ViewModel.Preview.PaneMode);
        var editor = AbsoluteBounds(Named(r.Window, "AssociateEditorPane"), r.Window);
        var preview = AbsoluteBounds(Named(r.Window, "PreviewPaneHost"), r.Window);
        Assert.True(Named(r.Window, "PreviewSplitter").IsEffectivelyVisible);
        Assert.True(editor.Width >= PdfPreviewLayout.MinEditorWidth - 1, $"editor {editor.Width}");
        Assert.True(preview.Width >= PdfPreviewLayout.MinPreviewWidth - 1, $"preview {preview.Width}");
        Assert.True(editor.Right <= preview.X + 1, "editor and preview overlap");
        Assert.True(preview.Right <= r.Window.ClientSize.Width + 1);
        Assert.Equal(r.ViewModel.Preview.EffectivePreviewPaneWidth, preview.Width, 0);
        Assert.True(r.ViewModel.Preview.HasPage);
        AssertNoControlClipsHorizontally(r.Window);
        AssertPreviewToolbarFitsThePane(r.Window);
        r.Window.Close();
    }

    [Fact]
    public async Task PreviewPane_StaysPut_AcrossOverviewProductsShowsAndNotes()
    {
        var r = await RealizeAsync(1600, 900);
        var pane = Named(r.Window, "PreviewPane");
        var image = r.ViewModel.Preview.PageImage;
        var before = AbsoluteBounds(pane, r.Window);

        foreach (var navigate in new Action[]
        {
            () => r.ViewModel.ShowProductsSectionCommand.Execute(null),
            () => r.ViewModel.ShowShowsSectionCommand.Execute(null),
            () => r.ViewModel.ShowNotesSectionCommand.Execute(null),
            () => r.ViewModel.ShowOverviewSectionCommand.Execute(null),
        })
        {
            navigate();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(pane, Named(r.Window, "PreviewPane"));
            Assert.True(pane.IsEffectivelyVisible);
            Assert.Equal(before, AbsoluteBounds(pane, r.Window));
            Assert.Same(image, r.ViewModel.Preview.PageImage);
            AssertNoControlClipsHorizontally(r.Window);
        }

        r.Window.Close();
    }

    [Fact]
    public async Task HidePreview_GivesTheEditorTheFullWidth_AndShowRestoresTheWidth()
    {
        var r = await RealizeAsync(1600, 900, PdfPreviewLayoutPreferences.BuiltIn with { PreviewPaneWidth = 520 });
        var body = AbsoluteBounds(Named(r.Window, "AssociateBodyGrid"), r.Window);

        r.ViewModel.Preview.HidePreviewCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Named(r.Window, "PreviewPaneHost").IsEffectivelyVisible);
        Assert.False(Named(r.Window, "PreviewSplitter").IsEffectivelyVisible);
        Assert.Equal(body.Width, AbsoluteBounds(Named(r.Window, "AssociateEditorPane"), r.Window).Width, 0);
        Assert.Equal("Show Preview", (Named(r.Window, "TogglePreviewButton") as Button)!.Content);

        r.ViewModel.Preview.ShowPreviewCommand.Execute(null);
        for (var i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal(520, AbsoluteBounds(Named(r.Window, "PreviewPaneHost"), r.Window).Width, 0);
        r.Window.Close();
    }

    [Fact]
    public async Task NarrowWindow_ShowsOnePaneAtATime()
    {
        var r = await RealizeAsync(960, 700);

        Assert.True(r.ViewModel.Preview.IsNarrowLayout);
        Assert.False(Named(r.Window, "PreviewPaneHost").IsEffectivelyVisible);
        Assert.True(Named(r.Window, "AssociateEditorPane").IsEffectivelyVisible);
        AssertNoControlClipsHorizontally(r.Window);

        r.ViewModel.Preview.ShowPreviewCommand.Execute(null);
        for (var i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }

        Assert.False(Named(r.Window, "AssociateEditorPane").IsEffectivelyVisible);
        Assert.True(Named(r.Window, "PreviewPaneHost").IsEffectivelyVisible);
        AssertNoControlClipsHorizontally(r.Window);
        AssertPreviewToolbarFitsThePane(r.Window);

        r.ViewModel.Preview.HidePreviewCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(Named(r.Window, "AssociateEditorPane").IsEffectivelyVisible);
        r.Window.Close();
    }

    // The preview at its minimum width: the toolbar wraps, nothing overflows the pane.
    [Fact]
    public async Task MinimumPreviewWidth_ToolbarWraps_WithoutOverflow()
    {
        var r = await RealizeAsync(PdfPreviewLayout.NarrowThreshold + 2, 800, PdfPreviewLayoutPreferences.BuiltIn with { PreviewPaneWidth = 200 });

        Assert.Equal(PdfPreviewPaneMode.Split, r.ViewModel.Preview.PaneMode);
        Assert.Equal(PdfPreviewLayout.MinPreviewWidth, AbsoluteBounds(Named(r.Window, "PreviewPaneHost"), r.Window).Width, 0);
        AssertPreviewToolbarFitsThePane(r.Window);
        AssertNoControlClipsHorizontally(r.Window);
        r.Window.Close();
    }

    // 15. A width remembered from a larger monitor is clamped to this window.
    [Fact]
    public async Task RememberedWideLayout_IsClampedToASmallerWindow()
    {
        var r = await RealizeAsync(1280, 800, PdfPreviewLayoutPreferences.BuiltIn with { PreviewPaneWidth = 1400 });

        var body = AbsoluteBounds(Named(r.Window, "AssociateBodyGrid"), r.Window);
        var preview = AbsoluteBounds(Named(r.Window, "PreviewPaneHost"), r.Window);
        Assert.Equal(body.Width - PdfPreviewLayout.MinEditorWidth - PdfPreviewLayout.SplitterWidth, preview.Width, 0);
        Assert.True(AbsoluteBounds(Named(r.Window, "AssociateEditorPane"), r.Window).Width >= PdfPreviewLayout.MinEditorWidth - 1);
        AssertNoControlClipsHorizontally(r.Window);
        r.Window.Close();
    }

    [Fact]
    public async Task MultiPagePdf_NavigatesInTheRealPane()
    {
        var r = await RealizeAsync(1600, 900, pages: 3);

        Assert.Equal(3, r.ViewModel.Preview.PageCount);
        Assert.Equal("1 / 3", (Named(r.Window, "PageIndicatorText") as TextBlock)!.Text);
        Assert.False(Named(r.Window, "PreviousPageButton").IsEffectivelyEnabled);

        r.ViewModel.Preview.NextPageCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("2 / 3", (Named(r.Window, "PageIndicatorText") as TextBlock)!.Text);
        Assert.True(Named(r.Window, "PreviousPageButton").IsEffectivelyEnabled);
        r.Window.Close();
    }
}
