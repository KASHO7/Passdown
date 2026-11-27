using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Carowinds.Passdown.Application.PdfOutput;
using Avalonia.Headless;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 35B real-window checks for the Passdown workspace (docs/phase35b-passdown-workspace-redesign.md): section
/// tabs never rebuild the preview and keep each section's scroll, the toolbar and tabs fit and never overlap at the four
/// review sizes in Light and Dark, conditional override fields, Edit Attractions, Include checkboxes, Print / Save PDF
/// from Actions after an unsaved edit, print right after a date switch, keyboard order, the divider and motion scope.
/// A partial of the RUN-ALONE <see cref="Phase33PreviewWindowTests"/> class, so the deterministic partition is unchanged.
/// </summary>
public partial class Phase33PreviewWindowTests
{
    private sealed record Workspace(Window Window, AssociateViewModel ViewModel, FakeAssociatePassdownService Service, ContentEchoPdfExportService Export,
        FakePdfPrintService Printer, FakePdfSaveFilePicker SavePicker, ManualDebounceScheduler PreviewDebounce, ManualDebounceScheduler Autosave);

    private static async Task<Workspace> RealizeWorkspaceAsync(double width, double height, ThemeVariant? theme = null, PdfPreviewLayoutPreferences? preferences = null)
    {
        var service = new FakeAssociatePassdownService();
        var export = new ContentEchoPdfExportService();
        var printer = new FakePdfPrintService();
        var savePicker = new FakePdfSaveFilePicker();
        var debounce = new ManualDebounceScheduler();
        var autosave = new ManualDebounceScheduler();
        var viewModel = new AssociateViewModel(
            service,
            new FakeClock(new DateTimeOffset(FixedDate.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero)),
            autosaveDelay: autosave.Delay,
            pdfExportService: export,
            pdfSavePicker: savePicker,
            templatePreferenceStore: new FakeAssociatePassdownTemplatePreferenceStore(),
            pdfRasterizer: new FakePdfRasterizer(),
            pdfPrintService: printer,
            pdfPreviewLayoutStore: new InMemoryPdfPreviewLayoutPreferenceStore(preferences),
            pdfPageImageFactory: new FakePdfPageImageFactory(),
            previewWorkScheduler: new InlinePreviewWorkScheduler(),
            previewDelay: debounce.Delay);
        await viewModel.PendingLoad;
        await viewModel.Preview.PendingPreferenceLoad;

        var window = new Window { Width = width, Height = height, Content = new AssociateView { DataContext = viewModel } };
        window.RequestedThemeVariant = theme ?? ThemeVariant.Light; // pinned: an earlier test may leave the app variant unresolved
        window.Show();
        Dispatcher.UIThread.RunJobs();
        debounce.ElapseLatest();
        Pump();
        return new Workspace(window, viewModel, service, export, printer, savePicker, debounce, autosave);
    }

    private static void Pump()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static T ByName<T>(Window window, string automationName)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().First(c => AutomationProperties.GetName(c) == automationName);

    /// <summary>Opens the Actions menu as a user would (its items take the view's DataContext once shown) and returns one item.</summary>
    private static MenuItem ActionsItem(Window window, Func<MenuItem, bool> match)
    {
        var button = ByName<Button>(window, "Passdown actions");
        var menu = Assert.IsType<MenuFlyout>(button.Flyout);
        if (!menu.IsOpen)
        {
            menu.ShowAt(button);
            Pump();
        }

        var item = menu.Items.OfType<MenuItem>().Single(match);
        Assert.True(item.IsEffectivelyVisible, $"{AutomationProperties.GetName(item)} is not visible in the open menu");
        return item;
    }

    private static MenuItem ActionsItem(Window window, string automationName) =>
        ActionsItem(window, i => AutomationProperties.GetName(i) == automationName);

    private static void CloseActions(Window window)
    {
        ByName<Button>(window, "Passdown actions").Flyout?.Hide();
        Pump();
    }

    private static ScrollViewer VisibleSection(Window window) =>
        Assert.Single(window.GetVisualDescendants().OfType<ScrollViewer>(), s => s.Classes.Contains("sectionScroll") && s.IsEffectivelyVisible);

    /// <summary>
    /// Runs a command and pumps the UI dispatcher until <paramref name="done"/> holds — synchronously, on this thread:
    /// awaiting a real async hop here would resume on another thread and break every later real-window test.
    /// </summary>
    private static void Execute(System.Windows.Input.ICommand? command, Func<bool>? done = null)
    {
        Assert.NotNull(command);
        Assert.True(command.CanExecute(null), "the command is unavailable");
        command.Execute(null);
        PumpUntil(done ?? (() => true));
    }

    private static void PumpUntil(Func<bool> done)
    {
        Pump();
        for (var i = 0; i < 200 && !done(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }

        Assert.True(done(), "timed out waiting for the UI");
    }

    [Fact]
    public async Task Phase35B_SectionTabs_SwitchSections_WithoutRebuildingOrHidingThePreview()
    {
        var w = await RealizeWorkspaceAsync(1600, 900);
        var preview = Named(w.Window, "PreviewPane");
        var image = w.ViewModel.Preview.PageImage;
        var generations = w.ViewModel.Preview.GenerationCount;
        var exports = w.Export.Calls.Count;
        Assert.NotNull(image);

        foreach (var (tab, flag) in new[] { ("Products section", "Products"), ("Shows section", "Shows"), ("Notes section", "Notes"), ("Overview section", "Overview") })
        {
            ByName<Button>(w.Window, tab).Command!.Execute(null);
            Pump();

            Assert.Equal(flag, w.ViewModel.SelectedSection.ToString());
            Assert.Equal("Current section", AutomationProperties.GetItemStatus(ByName<Button>(w.Window, tab)));
            Assert.Single(w.Window.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("sectionTab") && AutomationProperties.GetItemStatus(b) == "Current section");
            Assert.Same(preview, Named(w.Window, "PreviewPane"));
            Assert.True(preview.IsEffectivelyVisible);
            Assert.Same(image, w.ViewModel.Preview.PageImage);
            VisibleSection(w.Window); // exactly one section body is showing
        }

        Assert.Equal(generations, w.ViewModel.Preview.GenerationCount);
        Assert.Equal(exports, w.Export.Calls.Count);
        Assert.Empty(w.Service.CheckOnlineCalls);
        w.Window.Close();
    }

    [Fact]
    public async Task Phase35B_EachSection_KeepsItsScrollPosition_AcrossATabRoundTrip()
    {
        var w = await RealizeWorkspaceAsync(1280, 640);
        w.ViewModel.ManualHoursOverrideEnabled = true;
        w.ViewModel.PrivateBuyoutEnabled = true;
        w.ViewModel.EarlyRideTimeEditor.OpenErtEditorCommand.Execute(null);
        Pump();
        var overview = VisibleSection(w.Window);
        Assert.True(overview.Extent.Height > overview.Viewport.Height, "the Overview should scroll at this size");
        overview.Offset = new Vector(0, 180);
        Pump();

        ByName<Button>(w.Window, "Products section").Command!.Execute(null);
        Pump();
        ByName<Button>(w.Window, "Overview section").Command!.Execute(null);
        Pump();

        Assert.Same(overview, VisibleSection(w.Window));
        Assert.Equal(180, overview.Offset.Y, 0);
        w.Window.Close();
    }

    [Theory]
    [InlineData(1920d, 1080d, "Light")]
    [InlineData(1536d, 960d, "Dark")]
    [InlineData(1280d, 720d, "Light")]
    [InlineData(1280d, 720d, "Dark")]
    [InlineData(960d, 640d, "Dark")]
    public async Task Phase35B_ToolbarTabsAndOverview_FitWithoutOverlap(double width, double height, string theme)
    {
        var w = await RealizeWorkspaceAsync(width, height, theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);
        // Phase 35D follow-up: Today shows only off today — use the previous day so the widest toolbar (with Today) is checked.
        w.ViewModel.PreviousDayCommand.Execute(null);
        PumpUntil(() => w.ViewModel.PendingLoad.IsCompleted);
        w.ViewModel.ManualHoursOverrideEnabled = true;
        w.ViewModel.ManualOpeningTimeText = "9:00 AM";
        w.ViewModel.PrivateBuyoutEnabled = true;
        w.ViewModel.NorthGateOverrideText = "9:00 AM";
        Pump();

        AssertNoControlClipsHorizontally(w.Window);
        string[] bar = ["Previous day", "Operating date", "Next day", "Today", "Create PDF", "Passdown actions", "Overview section", "Products section", "Shows section", "Notes section"];
        var rects = bar.Select(name => (name, rect: AbsoluteBounds(ByName<Button>(w.Window, name), w.Window))).ToList();
        foreach (var (name, rect) in rects)
        {
            Assert.True(rect.Width > 0 && rect.Right <= w.Window.ClientSize.Width + 1, $"'{name}' is hidden or clipped ({rect}).");
        }

        for (var i = 0; i < rects.Count; i++)
        {
            for (var j = i + 1; j < rects.Count; j++)
            {
                var overlap = rects[i].rect.Intersect(rects[j].rect);
                Assert.True(overlap.Width <= 0.5 || overlap.Height <= 0.5, $"'{rects[i].name}' overlaps '{rects[j].name}'.");
            }
        }

        // The editor keeps a usable width beside the preview, or the narrow layout shows one pane.
        var editor = AbsoluteBounds(Named(w.Window, "AssociateEditorPane"), w.Window);
        Assert.True(editor.Width >= PdfPreviewLayout.MinEditorWidth - 1 || !w.ViewModel.Preview.IsSplitLayout);
        var sheet = VisibleSection(w.Window);
        Assert.True(sheet.Extent.Width <= sheet.Viewport.Width + 1, "the Overview scrolls sideways");
        w.Window.Close();
    }

    [Fact]
    public async Task Phase35B_OverrideAndBuyoutFields_AppearOnlyWhileTheirCheckboxIsOn()
    {
        var w = await RealizeWorkspaceAsync(1600, 900);
        var manual = Named(w.Window, "ManualHoursFields");
        var buyout = Named(w.Window, "PrivateBuyoutFields");
        Assert.False(manual.IsEffectivelyVisible);
        Assert.False(buyout.IsEffectivelyVisible);

        ByName<CheckBox>(w.Window, "Use a manual hours override").IsChecked = true;
        Pump();
        Assert.True(w.ViewModel.ManualHoursOverrideEnabled);
        Assert.True(manual.IsEffectivelyVisible);
        Assert.False(buyout.IsEffectivelyVisible);

        ByName<CheckBox>(w.Window, "Private buyout or custom hours").IsChecked = true;
        Pump();
        Assert.True(buyout.IsEffectivelyVisible);
        Assert.True(ByName<TextBox>(w.Window, "Private buyout details").IsEffectivelyVisible);

        ByName<CheckBox>(w.Window, "Use a manual hours override").IsChecked = false;
        Pump();
        Assert.False(manual.IsEffectivelyVisible);
        w.Window.Close();
    }

    [Fact]
    public async Task Phase35B_EditAttractions_OpensTheRealEarlyRideTimeEditor()
    {
        var w = await RealizeWorkspaceAsync(1600, 900);
        var edit = ByName<Button>(w.Window, "Edit Attractions for Early Ride Time");
        Assert.True(edit.IsEffectivelyVisible);

        Execute(edit.Command, () => w.ViewModel.EarlyRideTimeEditor.IsErtEditorOpen);

        Assert.True(w.ViewModel.EarlyRideTimeEditor.IsErtEditorOpen);
        Assert.True(ByName<Border>(w.Window, "Early Ride Time editor").IsEffectivelyVisible);
        Assert.True(ByName<TextBox>(w.Window, "Monthly Early Ride Time rides").IsEffectivelyVisible);
        w.Window.Close();
    }

    [Fact]
    public async Task Phase35B_IncludeCheckbox_ChangesTheDraft_AndTheNextPreview()
    {
        var w = await RealizeWorkspaceAsync(1600, 900);
        var before = w.Export.Calls.Count;
        var fastLane = ByName<CheckBox>(w.Window, "Include Fast Lane in the Passdown");
        Assert.True(fastLane.IsChecked);

        fastLane.IsChecked = false;
        Pump();
        w.PreviewDebounce.ElapseLatest();
        Pump();

        Assert.False(w.ViewModel.IncludeFastLane);
        Assert.True(w.Export.Calls.Count > before, "the preview was not refreshed after the inclusion change");
        Assert.False(w.Export.Calls[^1].Workspace.Draft.SectionSelection.IncludeFastLane);
        Assert.Equal(AssociateAutosaveState.Unsaved, w.ViewModel.AutosaveState); // autosave is scheduled, not skipped
        w.Window.Close();
    }

    [Fact]
    public async Task Phase35B_PrintAndSaveFromActions_AfterAnUnsavedEdit_UseTheLatestEdit()
    {
        var w = await RealizeWorkspaceAsync(1600, 900);
        ByName<TextBox>(w.Window, "Daily Feature text").Text = "Synthetic unsaved feature";
        Pump(); // no quiet window elapsed, no autosave elapsed

        Execute(ActionsItem(w.Window, "Print Passdown").Command, () => w.Printer.Requests.Count > 0);
        CloseActions(w.Window);
        var printed = System.Text.Encoding.UTF8.GetString(Assert.Single(w.Printer.Requests).PdfBytes);
        Assert.Contains("feature=Synthetic unsaved feature", printed);

        Execute(ActionsItem(w.Window, "Save PDF").Command, () => w.SavePicker.Calls.Count > 0);
        CloseActions(w.Window);
        var saved = System.Text.Encoding.UTF8.GetString(Assert.Single(w.SavePicker.Calls).Bytes);
        Assert.Contains("feature=Synthetic unsaved feature", saved);
        w.Window.Close();
    }

    [Fact]
    public async Task Phase35B_PrintRightAfterTheDateArrow_PrintsTheNewDate_NeverThePrevious()
    {
        var w = await RealizeWorkspaceAsync(1600, 900);
        Assert.Contains(FixedDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), System.Text.Encoding.UTF8.GetString(w.ViewModel.Preview.CurrentDocument!.Bytes));

        var print = ActionsItem(w.Window, "Print Passdown").Command!;
        CloseActions(w.Window);

        // Print immediately after the arrow: either it waits for the new date's workspace (unavailable while loading)
        // or it prints the new date — it must never print the previous date's document.
        ByName<Button>(w.Window, "Next day").Command!.Execute(null);
        if (print.CanExecute(null))
        {
            print.Execute(null);
        }

        PumpUntil(() => w.ViewModel.PendingLoad.IsCompleted);
        if (w.Printer.Requests.Count == 0)
        {
            Execute(print, () => w.Printer.Requests.Count > 0);
        }

        PumpUntil(() => w.Printer.Requests.Count > 0);

        var next = FixedDate.AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        Assert.All(w.Printer.Requests, r => Assert.Contains(next, System.Text.Encoding.UTF8.GetString(r.PdfBytes)));
        Assert.Equal(FixedDate.AddDays(1), w.ViewModel.SelectedDate);
        Assert.Empty(w.Service.CheckOnlineCalls);
        w.Window.Close();
    }

    [Fact]
    public async Task Phase35B_KeyboardOrder_RunsAcrossTheToolbar_ThenTheTabs_ThenTheOverview()
    {
        var w = await RealizeWorkspaceAsync(1600, 900);
        ByName<Button>(w.Window, "Previous day").Focus(NavigationMethod.Tab);
        Pump();
        var order = new List<string>();
        for (var i = 0; i < 12; i++)
        {
            var focused = w.Window.FocusManager?.GetFocusedElement() as Control;
            order.Add(focused is null ? "(none)" : AutomationProperties.GetName(focused) ?? focused.GetType().Name);
            w.Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            w.Window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Pump();
        }

        string[] expected =
        [
            // Phase 35D follow-up: today is selected here, so the Today button is hidden and is not a Tab stop.
            "Previous day", "Operating date", "Next day", "Create PDF", "Passdown actions",
            "Overview section", "Products section", "Shows section", "Notes section", "North Gate override time",
        ];
        Assert.Equal(expected, order.Take(expected.Length));
        w.Window.Close();
    }

    [Fact]
    public async Task Phase35B_Divider_ShowsWithThePreview_AndShowPreviewReturnsToTheToolbarWhenHidden()
    {
        var w = await RealizeWorkspaceAsync(1600, 900);
        var splitter = Named(w.Window, "PreviewSplitter");
        var grip = Named(w.Window, "PreviewSplitterGrip");
        Assert.True(splitter.IsEffectivelyVisible && splitter.Focusable);
        Assert.True(grip.IsEffectivelyVisible);
        Assert.False(grip.IsHitTestVisible);
        Assert.False(Named(w.Window, "TogglePreviewButton").IsEffectivelyVisible); // on the bar only while the preview is hidden

        var hide = ActionsItem(w.Window, i => i.Name == "TogglePreviewMenuItem");
        Assert.Equal("Hide Preview", hide.Header);
        Execute(hide.Command, () => !w.ViewModel.Preview.IsPreviewPaneShown);
        CloseActions(w.Window);

        Assert.False(splitter.IsEffectivelyVisible);
        Assert.False(grip.IsEffectivelyVisible);
        var show = (Button)Named(w.Window, "TogglePreviewButton");
        Assert.True(show.IsEffectivelyVisible);
        Assert.Equal("Show Preview", show.Content);
        Assert.True(AbsoluteBounds(show, w.Window).Right <= w.Window.ClientSize.Width + 1);

        Execute(show.Command, () => w.ViewModel.Preview.IsPreviewPaneShown);
        Assert.True(splitter.IsEffectivelyVisible);
        Assert.False(show.IsEffectivelyVisible);
        w.Window.Close();
    }

    [Fact]
    public async Task Phase35B_SectionTabUnderline_TransitionsOnlyUnderTheWindowMotionClass()
    {
        var w = await RealizeWorkspaceAsync(1600, 900);
        var tab = ByName<Button>(w.Window, "Products section");
        ContentPresenter Presenter() => tab.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");

        // A plain window (Interface animations Off / Windows reduced motion): no transition exists at all.
        Assert.DoesNotContain(Presenter().Transitions ?? [], t => t is Avalonia.Animation.BrushTransition { Property.Name: "BorderBrush" });

        w.Window.Classes.Add("motion");
        Pump();
        Assert.Contains(Presenter().Transitions ?? [], t => t is Avalonia.Animation.BrushTransition { Property.Name: "BorderBrush" });

        w.Window.Classes.Remove("motion");
        Pump();
        Assert.DoesNotContain(Presenter().Transitions ?? [], t => t is Avalonia.Animation.BrushTransition { Property.Name: "BorderBrush" });

        // Selecting a tab is immediate regardless of motion.
        tab.Command!.Execute(null);
        Pump();
        Assert.Equal("Current section", AutomationProperties.GetItemStatus(tab));
        w.Window.Close();
    }
}
