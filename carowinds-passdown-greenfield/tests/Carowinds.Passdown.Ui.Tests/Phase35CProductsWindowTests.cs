using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.PdfOutput;
using Avalonia.Headless;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 35C real-window checks for Products &amp; Pricing (docs/phase35c-products-pricing-redesign.md): display prices
/// are text with an Edit control (no row TextBox), the popover anchors to the activated Edit control without moving the
/// row, stays in the window and flips near the bottom edge, takes focus, Enter saves / Escape cancels with focus back
/// on Edit, closes unsaved on scroll and resize, and the page fits at the review sizes in Light and Dark with the sidebar
/// state and preview shown or hidden. A partial of the RUN-ALONE <see cref="Phase33PreviewWindowTests"/> class, so the
/// deterministic partition is unchanged.
/// </summary>
public partial class Phase33PreviewWindowTests
{
    private static AssociateProduct P35CProduct(int index, decimal? overrideAmount = null) => new(
        AssociateProductIdentity.ForDatedProduct(SourceCategory.DatedAdmissionTickets, $"P35C-{index}"),
        $"Synthetic Ticket {index:00}",
        SourceCategory.DatedAdmissionTickets,
        IsCustom: false,
        40m + index,
        OfficialDisplayPrice: null,
        null,
        index % 3 == 0 ? "Valid ID required" : null,
        DateSpecific: true,
        FixedDate,
        new AssociateVerificationInfo(AssociateVerificationState.Verified, new DateTimeOffset(FixedDate.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero), null, "Verified."),
        IncludeOnPdf: true,
        overrideAmount);

    private sealed record ProductsWindow(Window Window, AssociateViewModel ViewModel, ContentEchoPdfExportService Export, ManualDebounceScheduler PreviewDebounce);

    private static async Task<ProductsWindow> RealizeProductsAsync(double width, double height, ThemeVariant? theme = null, PdfPreviewLayoutPreferences? preferences = null, int count = 14)
    {
        var products = Enumerable.Range(1, count).Select(i => P35CProduct(i, i == 2 ? 33m : null)).ToList();
        var service = new FakeAssociatePassdownService
        {
            WorkspaceFactory = date => FakeAssociatePassdownService.DefaultWorkspace(date) with { Products = products },
        };
        var export = new ContentEchoPdfExportService();
        var debounce = new ManualDebounceScheduler();
        var viewModel = new AssociateViewModel(
            service,
            new FakeClock(new DateTimeOffset(FixedDate.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero)),
            autosaveDelay: new ManualDebounceScheduler().Delay,
            pdfExportService: export,
            pdfSavePicker: new FakePdfSaveFilePicker(),
            templatePreferenceStore: new FakeAssociatePassdownTemplatePreferenceStore(),
            pdfRasterizer: new FakePdfRasterizer(),
            pdfPrintService: new FakePdfPrintService(),
            pdfPreviewLayoutStore: new InMemoryPdfPreviewLayoutPreferenceStore(preferences),
            pdfPageImageFactory: new FakePdfPageImageFactory(),
            previewWorkScheduler: new InlinePreviewWorkScheduler(),
            previewDelay: debounce.Delay);
        await viewModel.PendingLoad;
        await viewModel.Preview.PendingPreferenceLoad;
        viewModel.ShowProductsSectionCommand.Execute(null);

        var window = new Window { Width = width, Height = height, Content = new AssociateView { DataContext = viewModel } };
        window.RequestedThemeVariant = theme ?? ThemeVariant.Light;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        debounce.ElapseLatest();
        Pump();
        return new ProductsWindow(window, viewModel, export, debounce);
    }

    private static Button EditButton(Window window, string productName) =>
        window.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible
            && (AutomationProperties.GetName(b) ?? string.Empty).StartsWith($"Edit display price for {productName},", StringComparison.Ordinal));

    /// <summary>Activates an Edit control the way a click / Enter / Space does: Click (anchors the popover), then its command.</summary>
    private static void Activate(Button button)
    {
        button.Focus(NavigationMethod.Tab);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        button.Command!.Execute(button.CommandParameter);
        Pump();
    }

    private static Popup PricePopup(Window window) => window.GetVisualDescendants().OfType<Popup>().Single(p => p.Name == "PriceEditorPopup");

    private static Border PricePanel(Window window) => Assert.IsType<Border>(PricePopup(window).Child);

    private static Rect InWindow(Visual visual, Window window)
    {
        var topLeft = visual.TranslatePoint(new Point(0, 0), window)!.Value;
        return new Rect(topLeft, visual.Bounds.Size);
    }

    [Fact]
    public async Task Phase35C_ProductRows_ShowPricesAsText_WithAnEditControl_AndNoRowTextBox()
    {
        var w = await RealizeProductsAsync(1600, 900);
        try
        {
            var products = VisibleSection(w.Window);
            Assert.Equal("ProductsSectionScroll", products.Name);
            Assert.DoesNotContain(products.GetVisualDescendants().OfType<TextBox>(),
                t => t.IsEffectivelyVisible && AutomationProperties.GetName(t) is not ("Search products" or "Custom item name" or "Custom item notes"));

            var edit = EditButton(w.Window, "Synthetic Ticket 02");
            Assert.Equal("Edit display price for Synthetic Ticket 02, currently $33.00", AutomationProperties.GetName(edit));
            Assert.True(edit.Focusable);
            Assert.True(edit.IsTabStop);
            var texts = products.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
            Assert.Contains("Manual override", texts);
            Assert.Contains("$42.00", texts); // the official value stays visible beside the override
        }
        finally
        {
            w.Window.Close();
        }
    }

    [Fact]
    public async Task Phase35C_Popover_AnchorsToTheEditControl_TakesFocus_AndDoesNotMoveTheRow()
    {
        var w = await RealizeProductsAsync(1600, 900);
        try
        {
            var edit = EditButton(w.Window, "Synthetic Ticket 01");
            var cell = Assert.IsType<StackPanel>(edit.Parent);
            Assert.Contains("priceCell", cell.Classes);
            var rowBefore = InWindow(edit, w.Window);
            var cellRect = InWindow(cell, w.Window);
            var extentBefore = VisibleSection(w.Window).Extent;

            Activate(edit);

            Assert.True(w.ViewModel.PriceEditor.IsOpen);
            var popup = PricePopup(w.Window);
            Assert.True(popup.IsOpen);
            Assert.Same(cell, popup.PlacementTarget); // the price cell, so its "Manual override" hint stays visible
            var panel = InWindow(PricePanel(w.Window), w.Window);
            Assert.True(panel.Top >= cellRect.Bottom - 0.5, $"popover {panel} should open below the price cell {cellRect}");
            Assert.True(Math.Abs(panel.Right - cellRect.Right) < 2, $"popover {panel} should align with the price cell's right edge {cellRect}");
            Assert.Equal(rowBefore, InWindow(edit, w.Window)); // the row did not expand or shift
            Assert.Equal(extentBefore, VisibleSection(w.Window).Extent);

            var input = w.Window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PriceEditorTextBox");
            Assert.True(input.IsFocused);
            Assert.Equal("$41.00", input.Text);
            Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(PricePanel(w.Window)));
            Assert.Equal("Edit display price for Synthetic Ticket 01", AutomationProperties.GetName(PricePanel(w.Window)));
            Assert.DoesNotContain("entering", PricePanel(w.Window).Classes); // full opacity at once without Window.motion
            Assert.Equal(1, PricePanel(w.Window).Opacity);
            Assert.True(PricePanel(w.Window).IsHitTestVisible);
        }
        finally
        {
            w.Window.Close();
        }
    }

    [Fact]
    public async Task Phase35C_EnterSaves_EscapeCancels_AndFocusReturnsToEdit()
    {
        var w = await RealizeProductsAsync(1600, 900);
        try
        {
            Activate(EditButton(w.Window, "Synthetic Ticket 01"));
            var input = w.Window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PriceEditorTextBox");
            input.Text = "37";
            input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = input });
            Pump();

            Assert.False(w.ViewModel.PriceEditor.IsOpen);
            var row = w.ViewModel.ProductSections.SelectMany(s => s.Products).Single(r => r.DisplayName == "Synthetic Ticket 01");
            Assert.Equal(37m, row.DisplayPriceOverride);
            var edit = EditButton(w.Window, "Synthetic Ticket 01");
            Assert.True(edit.IsFocused);
            Assert.Equal("Edit display price for Synthetic Ticket 01, currently $37.00", AutomationProperties.GetName(edit));

            Activate(edit);
            input.Text = "1";
            input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = input });
            Pump();

            Assert.False(w.ViewModel.PriceEditor.IsOpen);
            Assert.Equal(37m, row.DisplayPriceOverride);
            Assert.True(EditButton(w.Window, "Synthetic Ticket 01").IsFocused);
        }
        finally
        {
            w.Window.Close();
        }
    }

    [Fact]
    public async Task Phase35C_NearTheBottomEdge_ThePopoverFlipsAbove_AndStaysInsideTheWindow()
    {
        var w = await RealizeProductsAsync(1280, 720);
        try
        {
            var edit = w.Window.GetVisualDescendants().OfType<Button>()
                .Where(b => b.IsEffectivelyVisible && (AutomationProperties.GetName(b) ?? string.Empty).StartsWith("Edit display price for ", StringComparison.Ordinal))
                .Select(b => (Button: b, Rect: InWindow(b, w.Window)))
                .Where(x => x.Rect.Bottom < w.Window.Bounds.Height - 4)
                .OrderByDescending(x => x.Rect.Bottom).First();
            Assert.True(edit.Rect.Bottom > w.Window.Bounds.Height - 260, "fixture: the lowest Edit control should be near the bottom");

            Activate(edit.Button);

            Assert.True(w.ViewModel.PriceEditor.IsOpen); // scrolling a partly hidden row into view does not close the new editor
            var anchor = InWindow((Control)edit.Button.Parent!, w.Window); // the price cell, measured after activation scrolled it into view
            Assert.True(anchor.Bottom <= InWindow(VisibleSection(w.Window), w.Window).Bottom + 0.5);
            var panel = InWindow(PricePanel(w.Window), w.Window);
            Assert.True(panel.Bottom <= anchor.Top + 0.5, $"popover {panel} should flip above {anchor}");
            Assert.True(panel.Top >= 0 && panel.Left >= 0 && panel.Right <= w.Window.Bounds.Width + 0.5 && panel.Bottom <= w.Window.Bounds.Height + 0.5,
                $"popover {panel} must stay inside the window {w.Window.Bounds}");
        }
        finally
        {
            w.Window.Close();
        }
    }

    [Fact]
    public async Task Phase35C_ScrollingOrResizing_ClosesThePopoverUnsaved()
    {
        var w = await RealizeProductsAsync(1600, 900);
        try
        {
            Activate(EditButton(w.Window, "Synthetic Ticket 01"));
            w.ViewModel.PriceEditor.EditText = "5";
            var scroll = VisibleSection(w.Window);
            scroll.Offset = new Vector(0, 120);
            Pump();
            Assert.False(w.ViewModel.PriceEditor.IsOpen);
            Assert.False(PricePopup(w.Window).IsOpen);

            scroll.Offset = new Vector(0, 0);
            Pump();
            Activate(EditButton(w.Window, "Synthetic Ticket 01"));
            Assert.True(w.ViewModel.PriceEditor.IsOpen);
            w.Window.Width = 1300;
            Pump();
            Assert.False(w.ViewModel.PriceEditor.IsOpen);

            var row = w.ViewModel.ProductSections.SelectMany(s => s.Products).Single(r => r.DisplayName == "Synthetic Ticket 01");
            Assert.False(row.HasOverride);
        }
        finally
        {
            w.Window.Close();
        }
    }

    [Fact]
    public async Task Phase35C_InvalidInput_ShowsTheMessageInThePopover_AndKeepsItOpen()
    {
        var w = await RealizeProductsAsync(1600, 900);
        try
        {
            Activate(EditButton(w.Window, "Synthetic Ticket 01"));
            w.ViewModel.PriceEditor.EditText = "Special";
            w.ViewModel.PriceEditor.SaveCommand.Execute(null);
            Pump();

            Assert.True(PricePopup(w.Window).IsOpen);
            var error = w.Window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PriceEditorError");
            Assert.True(error.IsEffectivelyVisible);
            Assert.Equal("Enter a valid price, e.g. 24.99.", error.Text);
            Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(error));
        }
        finally
        {
            w.Window.Close();
        }
    }

    [Theory]
    [InlineData(1920, 1080, "Light", true)]
    [InlineData(1536, 960, "Dark", true)]
    [InlineData(1280, 720, "Light", true)]
    [InlineData(1280, 720, "Dark", false)]
    [InlineData(960, 640, "Light", true)]
    [InlineData(960, 640, "Dark", false)]
    public async Task Phase35C_ProductsFit_AtTheReviewSizes_WithAndWithoutThePreview(int width, int height, string theme, bool previewVisible)
    {
        var w = await RealizeProductsAsync(width, height, theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light,
            PdfPreviewLayoutPreferences.BuiltIn with { IsPreviewVisible = previewVisible });
        try
        {
            var scroll = VisibleSection(w.Window);
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1, $"horizontal overflow {scroll.Extent.Width} > {scroll.Viewport.Width}");

            var edits = scroll.GetVisualDescendants().OfType<Button>()
                .Where(b => b.IsEffectivelyVisible && (AutomationProperties.GetName(b) ?? string.Empty).StartsWith("Edit display price for ", StringComparison.Ordinal))
                .ToList();
            Assert.NotEmpty(edits);
            var editor = InWindow(scroll, w.Window);
            foreach (var edit in edits)
            {
                var rect = InWindow(edit, w.Window);
                Assert.True(rect.Right <= editor.Right + 0.5, $"{AutomationProperties.GetName(edit)} {rect} is past the editor {editor}");
                Assert.True(rect.Width >= 40, $"{AutomationProperties.GetName(edit)} is squeezed to {rect.Width}");
            }

            // Product names keep a readable column and never overlap the official price column.
            foreach (var name in scroll.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Classes.Contains("productName")))
            {
                Assert.True(name.Bounds.Width >= 90, $"{name.Text} column is {name.Bounds.Width}px");
            }

            // The popover opens on the right-most row control and stays inside the window.
            Activate(edits[0]);
            var panel = InWindow(PricePanel(w.Window), w.Window);
            Assert.True(panel.Left >= 0 && panel.Right <= w.Window.Bounds.Width + 0.5, $"popover {panel} outside {w.Window.Bounds}");
            w.ViewModel.PriceEditor.CancelCommand.Execute(null);
            Pump();
        }
        finally
        {
            w.Window.Close();
        }
    }

    [Fact]
    public void Phase35C_PopoverMotion_IsOneOpacityTransition_UnderWindowMotion_WithTheOverlayToken()
    {
        var styles = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Themes/PassdownStyles.axaml"));
        var view = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/AssociateView.axaml"));
        var style = System.Text.RegularExpressions.Regex.Match(styles, "<Style Selector=\"Window.motion Border.pricePopover\">.*?</Style>",
            System.Text.RegularExpressions.RegexOptions.Singleline).Value;

        Assert.Contains("<DoubleTransition Property=\"Opacity\" Duration=\"{StaticResource PassdownMotionOverlay}\"", style);
        Assert.DoesNotContain("Transform", style); // no movement or scaling
        Assert.DoesNotContain("Transitions", view); // the view declares no motion of its own
        Assert.Contains("Selector=\"Border.pricePopover.entering\"", view);
    }
}
