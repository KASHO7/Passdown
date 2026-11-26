using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.PdfOutput;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Shows;
using Carowinds.Passdown.Application.Windowing;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 35D real-window checks (docs/phase35d-shows-notes-shared-polish.md): Shows and Notes fit at the review sizes in
/// Light and Dark with the preview shown or hidden, a long schedule expands in place (mouse and keyboard) without
/// changing selection or regenerating the preview, a show checkbox and a daily-note edit reach the live preview, the
/// daily editor keeps its text across section switches and scrolls inside itself, a persistent-note checkbox adds and
/// removes through the existing selection, the dropdown chevrons sit on their label's centre, and the main window asks
/// for the navy caption once shown and again after a theme change. A partial of the RUN-ALONE
/// <see cref="Phase33PreviewWindowTests"/> class, so the deterministic partition is unchanged.
/// </summary>
public partial class Phase33PreviewWindowTests
{
    private static TimeOnly P35DTime(string time) => TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture);

    private static readonly ScheduledShow[] P35DShows =
    [
        new("P35D-MEET", "Character Meet and Greet With A Deliberately Long Name That Has To Wrap Across Lines", "Gathering Spot",
            [.. Phase35DShowsNotesPolishTests.LongTimes.Select(P35DTime)]),
        new("P35D-KICK", "Keg Kickoff", null, [P35DTime("11:45")]),
        new("P35D-CONTEST", "Eating Contest", "Harmony Hall Midway", [P35DTime("12:45"), P35DTime("16:15")]),
    ];

    private static async Task<Workspace> RealizeShowsNotesAsync(double width, double height, ThemeVariant? theme = null, PdfPreviewLayoutPreferences? preferences = null,
        IReadOnlyList<OperationalNote>? notes = null)
    {
        var service = new FakeAssociatePassdownService();
        service.ShowSchedules[FixedDate] = new ShowScheduleView(FixedDate, ShowScheduleState.Scheduled, P35DShows, false,
            new DateTimeOffset(FixedDate.ToDateTime(new TimeOnly(11, 0)), TimeSpan.Zero), new DateTimeOffset(FixedDate.ToDateTime(new TimeOnly(11, 0)), TimeSpan.Zero));
        if (notes is not null)
        {
            service.RelevantOperationalNotesByDate[FixedDate] = [.. notes];
        }

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
        window.RequestedThemeVariant = theme ?? ThemeVariant.Light;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        debounce.ElapseLatest();
        Pump();
        return new Workspace(window, viewModel, service, export, printer, savePicker, debounce, autosave);
    }

    private static OperationalNote P35DNote(string title) => new(
        Guid.NewGuid(), title, "Use scanners 1, 2 and 4.", OperationalNoteCategory.Equipment, OperationalNotePriority.Normal,
        FixedDate, null, OperationalNoteStatus.Active, new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero), null);

    private static IEnumerable<ContentPresenter> TimeTiles(Window window) =>
        window.GetVisualDescendants().OfType<ItemsControl>().Where(i => i.Classes.Contains("showTimeGrid") && i.IsEffectivelyVisible)
            .SelectMany(i => i.GetVisualDescendants().OfType<ContentPresenter>()).Where(c => c.IsEffectivelyVisible && c.Content is string);

    private static void Click(Window window, Control control)
    {
        var bounds = AbsoluteBounds(control, window);
        var center = bounds.Center;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Pump();
    }

    [Theory]
    [InlineData(1920d, 1080d, false)]
    [InlineData(1536d, 960d, true)]
    [InlineData(1280d, 720d, false)]
    [InlineData(960d, 640d, true)]
    public async Task Phase35D_ShowsAndNotes_FitTheReviewSizes_InLightAndDark_WithPreviewShownOrHidden(double width, double height, bool dark)
    {
        foreach (var previewVisible in new[] { true, false })
        {
            var w = await RealizeShowsNotesAsync(width, height, dark ? ThemeVariant.Dark : ThemeVariant.Light,
                PdfPreviewLayoutPreferences.BuiltIn with { IsPreviewVisible = previewVisible }, [P35DNote("Scanner down")]);
            try
            {
                w.ViewModel.ShowShowsSectionCommand.Execute(null);
                Pump();
                w.ViewModel.ShowRows[0].IsExpanded = true;
                Pump();
                Assert.Equal("ShowsSectionScroll", VisibleSection(w.Window).Name);
                AssertNoControlClipsHorizontally(w.Window);
                Assert.Equal(16, TimeTiles(w.Window).Count());

                // Tiles never overlap each other.
                var tiles = TimeTiles(w.Window).Select(t => AbsoluteBounds(t, w.Window)).ToList();
                for (var i = 0; i < tiles.Count; i++)
                {
                    for (var j = i + 1; j < tiles.Count; j++)
                    {
                        Assert.False(tiles[i].Deflate(0.5).Intersects(tiles[j].Deflate(0.5)), $"time tiles {i} and {j} overlap at {width}x{height}");
                    }
                }

                // Each row's checkbox, name, times and toggle sit inside the sheet.
                var sheet = AbsoluteBounds(Named(w.Window, "ShowsPanel"), w.Window);
                foreach (var check in w.Window.GetVisualDescendants().OfType<CheckBox>().Where(c => c.IsEffectivelyVisible && c.DataContext is AssociateShowRowViewModel))
                {
                    var b = AbsoluteBounds(check, w.Window);
                    Assert.True(b.Left >= sheet.Left && b.Right <= sheet.Right + 0.5, "a show checkbox is outside the sheet");
                }

                w.ViewModel.ShowNotesSectionCommand.Execute(null);
                Pump();
                Assert.Equal("NotesSectionScroll", VisibleSection(w.Window).Name);
                AssertNoControlClipsHorizontally(w.Window);
                Assert.True(Named(w.Window, "ManageOperationalNotesButton").IsEffectivelyVisible);
                var editor = (TextBox)Named(w.Window, "DailyNotesTextBox");
                Assert.True(editor.Bounds.Height >= 215 - 0.5, $"daily editor is only {editor.Bounds.Height}px tall");
            }
            finally
            {
                w.Window.Close();
            }
        }
    }

    [Fact]
    public async Task Phase35D_ViewTimes_ExpandsByMouseAndKeyboard_WithoutSelectionOrPreviewChange()
    {
        var w = await RealizeShowsNotesAsync(1600, 900);
        try
        {
            w.ViewModel.ShowShowsSectionCommand.Execute(null);
            Pump();
            var exports = w.Export.Calls.Count;
            var loads = w.Service.LoadCalls.Count;
            var scheduledSaves = w.Autosave.ScheduleCount;

            var toggle = w.Window.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && b.Classes.Contains("showToggle"));
            Assert.Equal("Show all 16 performance times for " + P35DShows[0].Name, AutomationProperties.GetName(toggle));
            Assert.True(toggle.IsTabStop && toggle.Focusable);
            var rowTop = AbsoluteBounds(toggle, w.Window).Top;

            Click(w.Window, toggle);
            Assert.True(w.ViewModel.ShowRows[0].IsExpanded);
            Assert.Equal(16, TimeTiles(w.Window).Count());
            Assert.Equal(rowTop, AbsoluteBounds(toggle, w.Window).Top, 1); // expanding never moves the row's first line
            Assert.Equal("Show fewer performance times for " + P35DShows[0].Name, AutomationProperties.GetName(toggle));

            toggle.Focus(NavigationMethod.Tab);
            toggle.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Pump();
            Assert.False(w.ViewModel.ShowRows[0].IsExpanded);
            Assert.Empty(TimeTiles(w.Window));

            for (var i = 0; i < 30; i++)
            {
                w.ViewModel.ShowRows[0].ToggleExpandedCommand.Execute(null);
                Pump();
            }

            Assert.All(w.ViewModel.ShowRows, r => Assert.False(r.IsSelected));
            Assert.Equal(exports, w.Export.Calls.Count);
            Assert.Equal(loads, w.Service.LoadCalls.Count);
            Assert.Empty(w.Service.CheckOnlineCalls);
            Assert.Equal(scheduledSaves, w.Autosave.ScheduleCount);
        }
        finally
        {
            w.Window.Close();
        }
    }

    [Fact]
    public async Task Phase35D_ShowCheckbox_ReachesTheLivePreview_AndTheCount()
    {
        var w = await RealizeShowsNotesAsync(1600, 900);
        try
        {
            w.ViewModel.IncludeShows = true;
            w.ViewModel.ShowShowsSectionCommand.Execute(null);
            Pump();
            w.PreviewDebounce.ElapseLatest();
            Pump();

            var check = w.Window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.IsEffectivelyVisible && c.DataContext is AssociateShowRowViewModel { Name: "Eating Contest" });
            Assert.Equal("Include Eating Contest on the passdown, 12:45 PM, 4:15 PM", AutomationProperties.GetName(check));
            Click(w.Window, check);

            Assert.True(w.ViewModel.ShowRows.Single(r => r.Name == "Eating Contest").IsSelected);
            Assert.Equal("1 of 3 shows selected", ((TextBlock)Named(w.Window, "ShowsSelectionCount")).Text);
            w.PreviewDebounce.ElapseLatest();
            PumpUntil(() => w.Export.Calls.Count > 0 && w.Export.Calls[^1].Workspace.Draft.Shows?.SelectedShows.Count == 1);
            Assert.Equal("P35D-CONTEST", w.Export.Calls[^1].Workspace.Draft.Shows!.SelectedShows.Single().ShowId);
        }
        finally
        {
            w.Window.Close();
        }
    }

    [Fact]
    public async Task Phase35D_DailyNotes_EditReachesThePreview_TextSurvivesSectionSwitches_LongTextScrollsInside()
    {
        var w = await RealizeShowsNotesAsync(1600, 900);
        try
        {
            w.ViewModel.ShowNotesSectionCommand.Execute(null);
            Pump();
            var editor = (TextBox)Named(w.Window, "DailyNotesTextBox");
            editor.Focus();
            // No blank line here: under the headless test font a blank line in a realized TextBox loops and grows memory
            // without bound (a bare Avalonia TextBox does the same; the real Skia renderer does not). The blank-line rule
            // itself is covered in Phase35DShowsNotesPolishTests.DailyNotes_CountNonEmptyLines_LikeTheDraftAndThePdf.
            editor.Text = "Gate 2 scanner down\nUse gate 3";
            Pump();

            Assert.Equal("2 notes", ((TextBlock)Named(w.Window, "DailyNoteLineCount")).Text);
            w.PreviewDebounce.ElapseLatest();
            PumpUntil(() => w.Export.Calls.Count > 0 && w.Export.Calls[^1].Workspace.Draft.OperationalNotes.SequenceEqual(["Gate 2 scanner down", "Use gate 3"]));

            w.ViewModel.ShowShowsSectionCommand.Execute(null);
            Pump();
            w.ViewModel.ShowNotesSectionCommand.Execute(null);
            Pump();
            Assert.Same(editor, Named(w.Window, "DailyNotesTextBox")); // the same live editor, not a rebuilt one
            Assert.Equal("Gate 2 scanner down\nUse gate 3", editor.Text);

            w.ViewModel.OperationalNotesText = string.Join("\n", Enumerable.Range(1, 80).Select(i => $"Synthetic daily note {i} that is long enough to wrap inside the editor at narrow widths."));
            Pump();
            Assert.True(editor.Bounds.Height <= 440 + 0.5, $"the editor grew to {editor.Bounds.Height}px instead of scrolling inside");
            AssertNoControlClipsHorizontally(w.Window);
        }
        finally
        {
            w.Window.Close();
        }
    }

    [Fact]
    public async Task Phase35D_PersistentNoteCheckbox_AddsAndRemoves_ThroughTheExistingSelection()
    {
        var note = P35DNote("South Gate scanner #3 out of service");
        var w = await RealizeShowsNotesAsync(1600, 900, notes: [note]);
        try
        {
            w.ViewModel.ShowNotesSectionCommand.Execute(null);
            Pump();
            var check = w.Window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.IsEffectivelyVisible && c.DataContext is AssociateOperationalNoteSuggestionRowViewModel);
            Assert.Equal("Include South Gate scanner #3 out of service in this passdown", AutomationProperties.GetName(check));
            Assert.False(check.IsChecked);

            Click(w.Window, check);
            PumpUntil(() => w.ViewModel.SelectedOperationalNotes.Count == 1);
            Assert.Equal([(FixedDate, note.Id)], w.Service.SelectOperationalNoteCalls);
            Assert.Equal("Included in this Passdown (1)", ((TextBlock)Named(w.Window, "IncludedPersistentNotesHeading")).Text);

            var included = w.Window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.IsEffectivelyVisible && c.DataContext is AssociateSelectedOperationalNoteRowViewModel);
            Assert.True(included.IsChecked);
            Click(w.Window, included);
            PumpUntil(() => w.ViewModel.SelectedOperationalNotes.Count == 0 && w.ViewModel.AvailableOperationalNoteSuggestions.Count == 1);
            Assert.Equal([(FixedDate, note.Id)], w.Service.DeselectOperationalNoteCalls);
            Assert.Equal(OperationalNoteStatus.Active, note.Status);
            Assert.Same(note, w.Service.RelevantOperationalNotesByDate[FixedDate].Single());
        }
        finally
        {
            w.Window.Close();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Phase35D_DropdownChevrons_SitOnTheirLabelsCentre_InsideTheButton(bool dark)
    {
        var w = await RealizeShowsNotesAsync(1600, 900, dark ? ThemeVariant.Dark : ThemeVariant.Light);
        try
        {
            foreach (var name in new[] { "Passdown actions", "Operating date" })
            {
                var button = ByName<Button>(w.Window, name);
                var chevron = button.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Classes.Contains("dropdownChevron"));
                var label = name == "Passdown actions"
                    ? button.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Actions")
                    : (TextBlock)Named(w.Window, "SelectedDateText");
                var chevronBox = AbsoluteBounds(chevron, w.Window);
                var labelBox = AbsoluteBounds(label, w.Window);
                var buttonBox = AbsoluteBounds(button, w.Window);

                Assert.Equal(new Size(12, 12), chevron.Bounds.Size);
                Assert.Equal(Avalonia.Media.Stretch.None, chevron.Stretch);
                // The drawn glyph's centre is its box centre (fixed coordinates), and the box centre is the label's centre.
                var glyph = chevron.Data!.Bounds;
                Assert.Equal(6.0, glyph.Center.Y, 2);
                Assert.Equal(6.0, glyph.Center.X, 2);
                Assert.True(Math.Abs(chevronBox.Center.Y - labelBox.Center.Y) <= 0.75, $"{name}: chevron centre {chevronBox.Center.Y} vs label centre {labelBox.Center.Y}");
                Assert.True(buttonBox.Contains(chevronBox), $"{name}: the chevron is outside its button");
                Assert.True(chevronBox.Left >= labelBox.Right, $"{name}: the chevron overlaps its label");
                Assert.NotNull(chevron.Stroke);
            }
        }
        finally
        {
            w.Window.Close();
        }
    }

    private sealed class RecordingCaptionStyler : IWindowCaptionStyler
    {
        public List<(nint Handle, CaptionColor Caption, CaptionColor Text)> Calls { get; } = [];

        public CaptionStyleResult Apply(nint windowHandle, CaptionColor caption, CaptionColor text)
        {
            Calls.Add((windowHandle, caption, text));
            return CaptionStyleResult.NotSupportedOnThisPlatform;
        }
    }

    [Fact]
    public void Phase35D_MainWindow_RequestsTheNavyCaption_WhenShown_AndAgainAfterAThemeChange()
    {
        var styler = new RecordingCaptionStyler();
        var window = new MainWindow { Width = 1280, Height = 860, CaptionStyler = styler };
        window.RequestedThemeVariant = ThemeVariant.Light;
        try
        {
            Assert.Empty(styler.Calls); // nothing before the native window exists
            window.Show();
            Pump();
            var first = Assert.Single(styler.Calls);
            Assert.Equal(new CaptionColor(0x1B, 0x35, 0x53), first.Caption);
            Assert.Equal(new CaptionColor(0xFF, 0xFF, 0xFF), first.Text);
            Assert.Equal(CaptionStyleResult.NotSupportedOnThisPlatform, window.LastCaptionStyleResult);
            Assert.Equal("Carowinds Operations", window.Title);
            Assert.NotNull(window.Icon);

            window.RequestedThemeVariant = ThemeVariant.Dark;
            Pump();
            Assert.Equal(2, styler.Calls.Count);
            Assert.Equal(first.Caption, styler.Calls[1].Caption); // the same navy in Dark
        }
        finally
        {
            window.Close();
        }
    }
}
