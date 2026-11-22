using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.PdfOutput;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 33: <see cref="PassdownPdfPreviewViewModel"/> on its own — debounce, stale-generation handling,
/// page/zoom state, layout preferences, and Print / Save PDF orchestration. Everything is fake and
/// deterministic: no PDFium, no QuestPDF, no printer, no clock. These prove the logic only; physical
/// printing is NOT tested here (see docs/phase33-live-pdf-preview-printing.md, Windows checklist).
/// </summary>
public class Phase33PdfPreviewViewModelTests
{
    private static readonly DateOnly Today = new(2026, 8, 21);

    private sealed class Harness
    {
        public AssociateWorkspace Workspace { get; set; } = FakeAssociatePassdownService.DefaultWorkspace(Today);

        public DateOnly Date { get; set; } = Today;

        public AssociatePassdownTemplate Template { get; set; } = AssociatePassdownTemplate.Classic;

        public bool Loaded { get; set; } = true;

        public bool CanExport { get; set; } = true;

        public ContentEchoPdfExportService Export { get; } = new();

        public FakePdfRasterizer Rasterizer { get; } = new();

        public FakePdfPageImageFactory Images { get; } = new();

        public InlinePreviewWorkScheduler Scheduler { get; } = new();

        public ManualDebounceScheduler Debounce { get; } = new();

        public FakePdfPrintService Printer { get; } = new();

        public FakePdfSaveFilePicker Picker { get; } = new();

        public InMemoryPdfPreviewLayoutPreferenceStore Store { get; init; } = new();

        public PassdownPdfPreviewViewModel Create(bool withRasterizer = true)
        {
            var preview = new PassdownPdfPreviewViewModel(
                () => Loaded ? PassdownPdfCapture.Ready(new PassdownPdfRequest(Workspace, Date, Template)) : PassdownPdfCapture.Waiting("Loading this date's preview…"),
                () => CanExport,
                Export,
                withRasterizer ? Rasterizer : null,
                Printer,
                Picker,
                Store,
                Images,
                Scheduler,
                TimeSpan.FromMilliseconds(450),
                Debounce.Delay,
                () => new DateTimeOffset(2026, 8, 21, 15, 0, 0, TimeSpan.Zero));
            preview.UpdateAvailableWidth(1400);
            preview.UpdateViewport(460, 700, 1.0);
            return preview;
        }

        public AssociateWorkspace WithAttendance(int attendance) =>
            Workspace with { Draft = Workspace.Draft with { ProjectedAttendance = attendance } };
    }

    private static async Task SettleAsync(PassdownPdfPreviewViewModel preview, Harness harness)
    {
        harness.Debounce.ElapseLatest();
        await preview.PendingRefresh;
    }

    // 1. The preview is built from exactly the captured Passdown state and template.
    [Fact]
    public async Task Refresh_GeneratesFromTheCapturedWorkspaceAndTemplate()
    {
        var harness = new Harness { Template = AssociatePassdownTemplate.Modern };
        harness.Workspace = harness.WithAttendance(4321);
        var preview = harness.Create();

        preview.NotifyContentChanged();
        await SettleAsync(preview, harness);

        var call = Assert.Single(harness.Export.Calls);
        Assert.Same(harness.Workspace, call.Workspace);
        Assert.Equal(AssociatePassdownTemplate.Modern, call.Template);
        Assert.Equal(PdfPreviewStatus.Ready, preview.Status);
        Assert.True(preview.HasPage);
        Assert.Equal(Today, preview.CurrentDocument!.Date);
        Assert.Contains("Modern", preview.DocumentCaption);
        Assert.Same(preview.CurrentDocument.Bytes, harness.Rasterizer.Opened.Single());
    }

    // 10. Rapid edits produce one generation after the debounce, not one per keystroke.
    [Fact]
    public async Task RapidChanges_AreDebounced_IntoOneGeneration()
    {
        var harness = new Harness();
        var preview = harness.Create();

        for (var i = 1; i <= 6; i++)
        {
            harness.Workspace = harness.WithAttendance(i);
            preview.NotifyContentChanged();
        }

        Assert.Empty(harness.Export.Calls); // nothing yet: still debouncing
        Assert.Equal(6, harness.Debounce.ScheduleCount);
        await SettleAsync(preview, harness);

        var call = Assert.Single(harness.Export.Calls);
        Assert.Equal(6, call.Workspace.Draft.ProjectedAttendance);
    }

    // Duplicate requests for an unchanged state never regenerate.
    [Fact]
    public async Task EnsureLatest_WithNoChanges_ReusesTheCurrentDocument()
    {
        var harness = new Harness();
        var preview = harness.Create();
        preview.NotifyContentChanged();
        await SettleAsync(preview, harness);
        var first = preview.CurrentDocument;

        var again = await preview.EnsureLatestDocumentAsync();

        Assert.Same(first, again);
        Assert.Single(harness.Export.Calls);
    }

    // Identical content (only metadata timestamps differ) keeps the page image — no flicker.
    [Fact]
    public async Task RegeneratedIdenticalContent_KeepsThePageImage()
    {
        var harness = new Harness();
        var preview = harness.Create();
        preview.NotifyContentChanged();
        await SettleAsync(preview, harness);
        var image = preview.PageImage;

        preview.NotifyContentChanged(); // e.g. a workspace swap with the same content
        await SettleAsync(preview, harness);

        Assert.Equal(2, harness.Export.Calls.Count);
        Assert.Same(image, preview.PageImage);
        Assert.Single(harness.Rasterizer.Opened);
    }

    // 11. An older, slower generation can never replace a newer one.
    [Fact]
    public async Task StaleGeneration_FinishingLate_NeverReplacesTheNewerDocument()
    {
        var harness = new Harness();
        var preview = harness.Create();

        var slowGate = harness.Scheduler.HoldNext();
        harness.Workspace = harness.WithAttendance(100);
        preview.NotifyContentChanged();
        harness.Debounce.ElapseLatest();
        var slow = preview.PendingRefresh; // generation for 100, held

        harness.Workspace = harness.WithAttendance(200);
        preview.NotifyContentChanged();
        await SettleAsync(preview, harness); // generation for 200 completes first

        Assert.Equal(200, harness.Export.Calls.Last().Workspace.Draft.ProjectedAttendance);
        var newest = preview.CurrentDocument;

        slowGate.SetResult();
        await slow;

        Assert.Same(newest, preview.CurrentDocument);
        Assert.Contains("attendance=200", System.Text.Encoding.UTF8.GetString(preview.CurrentDocument!.Bytes));
        Assert.Equal(PdfPreviewStatus.Ready, preview.Status);
    }

    // 12. A generation failure shows an error and keeps the editor usable; Try Again recovers.
    [Fact]
    public async Task GenerationFailure_ShowsError_AndRefreshRecovers()
    {
        var harness = new Harness();
        var preview = harness.Create();
        harness.Export.Throws = new InvalidOperationException("boom");

        preview.NotifyContentChanged();
        await SettleAsync(preview, harness);

        Assert.Equal(PdfPreviewStatus.Error, preview.Status);
        Assert.True(preview.ShowsStatusOverlay);
        Assert.Contains("draft is unchanged", preview.StatusText);
        Assert.False(preview.HasPage);

        harness.Export.Throws = null;
        await preview.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(PdfPreviewStatus.Ready, preview.Status);
        Assert.True(preview.HasPage);
    }

    [Fact]
    public async Task RenderFailure_ShowsError_ButTheDocumentStillPrints()
    {
        var harness = new Harness();
        harness.Rasterizer.OpenFailure = _ => new PdfRasterizationException("bad");
        var preview = harness.Create();

        preview.NotifyContentChanged();
        await SettleAsync(preview, harness);
        Assert.Equal(PdfPreviewStatus.Error, preview.Status);

        await preview.PrintCommand.ExecuteAsync(null);

        Assert.Single(harness.Printer.Requests);
        Assert.Single(harness.Export.Calls); // the generated document was reused
    }

    [Fact]
    public async Task PreflightFailure_ShowsTheReason_AndNoPage()
    {
        var harness = new Harness();
        harness.Export.Override = (_, _) => FakeAssociatePassdownPdfExportService.ValidationFailed();
        var preview = harness.Create();

        preview.NotifyContentChanged();
        await SettleAsync(preview, harness);

        Assert.Equal(PdfPreviewStatus.NoDocument, preview.Status);
        Assert.Equal("An operating date is required.", preview.StatusText);
        Assert.False(preview.HasPage);
    }

    // 9. A date change clears the old page at once and never shows it as the new date's.
    [Fact]
    public async Task DateChange_ClearsThePreviousDatesPage_Immediately()
    {
        var harness = new Harness();
        var preview = harness.Create();
        preview.NotifyContentChanged();
        await SettleAsync(preview, harness);
        var oldImage = (FakePdfPageImageFactory.FakePageImage)preview.PageImage!;

        harness.Loaded = false; // the next date is loading
        harness.Date = Today.AddDays(1);
        preview.NotifySelectedDateChanged();

        Assert.False(preview.HasPage);
        Assert.True(oldImage.IsDisposed);
        Assert.Null(preview.CurrentDocument);
        Assert.Null(preview.DocumentCaption);
        Assert.Equal(PdfPreviewStatus.Waiting, preview.Status);

        // Even if asked now, nothing is produced for a date that has not loaded.
        Assert.Null(await preview.EnsureLatestDocumentAsync());
        Assert.Single(harness.Export.Calls);

        harness.Workspace = FakeAssociatePassdownService.DefaultWorkspace(Today.AddDays(1));
        harness.Loaded = true;
        preview.NotifyContentChanged();
        await SettleAsync(preview, harness);

        Assert.Equal(Today.AddDays(1), preview.CurrentDocument!.Date);
        Assert.True(preview.HasPage);
    }

    [Fact]
    public async Task DateChange_WhileAGenerationIsInFlight_DiscardsItsResult()
    {
        var harness = new Harness();
        var preview = harness.Create();
        var gate = harness.Scheduler.HoldNext();
        preview.NotifyContentChanged();
        harness.Debounce.ElapseLatest();
        var inFlight = preview.PendingRefresh;

        harness.Loaded = false;
        preview.NotifySelectedDateChanged();
        gate.SetResult();
        await inFlight;

        Assert.False(preview.HasPage);
        Assert.Equal(PdfPreviewStatus.Waiting, preview.Status);
        Assert.Empty(harness.Rasterizer.Opened);
    }

    // 13. Hidden: nothing renders on edits and display resources are released; shown again: refreshes.
    [Fact]
    public async Task HideAndShow_StopsRendering_ReleasesThePage_AndRestores()
    {
        var harness = new Harness();
        var preview = harness.Create();
        preview.NotifyContentChanged();
        await SettleAsync(preview, harness);
        var raster = harness.Rasterizer.Documents.Single();

        preview.HidePreviewCommand.Execute(null);

        Assert.False(preview.IsPreviewPaneShown);
        Assert.True(preview.IsEditorShown);
        Assert.False(preview.HasPage);
        Assert.True(raster.IsDisposed);
        Assert.False(harness.Store.Current.IsPreviewVisible);
        Assert.Equal("Show Preview", preview.TogglePreviewLabel);

        harness.Workspace = harness.WithAttendance(77);
        preview.NotifyContentChanged();
        Assert.Single(harness.Export.Calls); // no background generation while hidden

        preview.ShowPreviewCommand.Execute(null);
        await preview.PendingRefresh;

        Assert.True(preview.IsPreviewPaneShown);
        Assert.True(preview.HasPage);
        Assert.True(harness.Store.Current.IsPreviewVisible);
        Assert.Equal(77, harness.Export.Calls.Last().Workspace.Draft.ProjectedAttendance);
    }

    [Fact]
    public void WithoutARasterizer_ThePaneNeverShows_AndNothingRendersInTheBackground()
    {
        var harness = new Harness();
        var preview = harness.Create(withRasterizer: false);

        preview.NotifyContentChanged();

        Assert.False(preview.IsAvailable);
        Assert.False(preview.IsPreviewPaneShown);
        Assert.Equal(0, harness.Debounce.ScheduleCount);
        Assert.Empty(harness.Export.Calls);
    }

    // 14. The divider position is remembered and restored by a new session.
    [Fact]
    public async Task SplitterWidth_IsPersisted_AndRestored()
    {
        var store = new InMemoryPdfPreviewLayoutPreferenceStore();
        var harness = new Harness { Store = store };
        var preview = harness.Create();

        preview.CommitPreviewPaneWidth(560);
        await preview.PendingPreferenceSave;

        Assert.Equal(560, store.Current.PreviewPaneWidth);

        var restored = new Harness { Store = store }.Create();
        await restored.PendingPreferenceLoad;
        Assert.Equal(560, restored.PreviewPaneWidth);
        Assert.Equal(560, restored.EffectivePreviewPaneWidth);
    }

    // 15. A width saved on a large monitor is clamped on a smaller window; too narrow → one pane.
    [Fact]
    public async Task RestoredLayout_OnASmallerWindow_IsClamped_AndNarrowShowsOnePane()
    {
        var store = new InMemoryPdfPreviewLayoutPreferenceStore(new PdfPreviewLayoutPreferences(true, 1200, PdfPreviewFitMode.FitWidth, 1.0));
        var preview = new Harness { Store = store }.Create();
        await preview.PendingPreferenceLoad;

        preview.UpdateAvailableWidth(1100);
        Assert.Equal(PdfPreviewPaneMode.Split, preview.PaneMode);
        Assert.Equal(1100 - PdfPreviewLayout.MinEditorWidth - PdfPreviewLayout.SplitterWidth, preview.EffectivePreviewPaneWidth);
        Assert.Equal(1200, preview.PreviewPaneWidth); // the preference itself is not rewritten by a smaller window

        preview.UpdateAvailableWidth(900);
        Assert.True(preview.IsNarrowLayout);
        Assert.Equal(PdfPreviewPaneMode.EditorOnly, preview.PaneMode);
        Assert.Equal("Show Preview", preview.TogglePreviewLabel);

        preview.ShowPreviewCommand.Execute(null);
        Assert.Equal(PdfPreviewPaneMode.PreviewOnly, preview.PaneMode);
        Assert.False(preview.IsEditorShown);

        preview.HidePreviewCommand.Execute(null);
        Assert.Equal(PdfPreviewPaneMode.EditorOnly, preview.PaneMode);
        Assert.True(store.Current.IsPreviewVisible); // narrow-window toggling is transient

        preview.UpdateAvailableWidth(1600);
        Assert.Equal(PdfPreviewPaneMode.Split, preview.PaneMode);
    }

    [Theory]
    [InlineData(2000, 480, 480)]
    [InlineData(1100, 480, 434)]
    [InlineData(1100, 100, 340)]
    [InlineData(1006, 900, 340)]
    public void LayoutMath_ClampsThePreviewWidth(double available, double preferred, double expected)
    {
        Assert.Equal(expected, PdfPreviewLayout.ClampPreviewWidth(preferred, available));
    }

    // 16. Page navigation within bounds.
    [Fact]
    public async Task PageNavigation_MovesWithinBounds()
    {
        var harness = new Harness();
        harness.Rasterizer.PageCountFor = _ => 3;
        harness.Export.PageCount = 3;
        var preview = harness.Create();
        preview.NotifyContentChanged();
        await SettleAsync(preview, harness);

        Assert.Equal("1 / 3", preview.PageIndicatorText);
        Assert.False(preview.PreviousPageCommand.CanExecute(null));

        preview.NextPageCommand.Execute(null);
        await preview.PendingPageRender;
        preview.NextPageCommand.Execute(null);
        await preview.PendingPageRender;

        Assert.Equal(2, preview.PageIndex);
        Assert.Equal("Page 3 of 3", preview.PageIndicatorAccessibleText);
        Assert.False(preview.NextPageCommand.CanExecute(null));
        Assert.Contains(harness.Rasterizer.Documents.Single().Renders, r => r.Page == 2);

        preview.PreviousPageCommand.Execute(null);
        await preview.PendingPageRender;
        Assert.Equal(1, preview.PageIndex);
    }

    // 18. A shorter replacement document clamps to its last page; zoom/fit are kept.
    [Fact]
    public async Task PageCountShrinks_ClampsToTheLastPage_AndKeepsZoom()
    {
        var harness = new Harness();
        var pages = 3;
        harness.Rasterizer.PageCountFor = _ => pages;
        var preview = harness.Create();
        preview.NotifyContentChanged();
        await SettleAsync(preview, harness);
        preview.NextPageCommand.Execute(null);
        preview.NextPageCommand.Execute(null);
        await preview.PendingPageRender;
        preview.ZoomInCommand.Execute(null);
        var zoom = preview.Zoom;

        pages = 1;
        harness.Workspace = harness.WithAttendance(5);
        preview.NotifyContentChanged();
        await SettleAsync(preview, harness);

        Assert.Equal(1, preview.PageCount);
        Assert.Equal(0, preview.PageIndex);
        Assert.Equal(zoom, preview.Zoom);
        Assert.Equal(PdfPreviewFitMode.Custom, preview.FitMode);
    }

    [Fact]
    public async Task PageIsPreserved_AcrossOrdinaryContentUpdates()
    {
        var harness = new Harness();
        harness.Rasterizer.PageCountFor = _ => 2;
        var preview = harness.Create();
        preview.NotifyContentChanged();
        await SettleAsync(preview, harness);
        preview.NextPageCommand.Execute(null);
        await preview.PendingPageRender;

        harness.Workspace = harness.WithAttendance(9);
        preview.NotifyContentChanged();
        await SettleAsync(preview, harness);

        Assert.Equal(1, preview.PageIndex);
        Assert.Equal(2, harness.Rasterizer.Documents.Count);
        Assert.True(harness.Rasterizer.Documents[0].IsDisposed); // old document released
    }

    // 17. Zoom never leaves its bounds; fit modes compute from the viewport.
    [Fact]
    public async Task Zoom_StaysWithinBounds()
    {
        var harness = new Harness();
        var preview = harness.Create();
        preview.NotifyContentChanged();
        await SettleAsync(preview, harness);

        for (var i = 0; i < 40 && preview.ZoomInCommand.CanExecute(null); i++)
        {
            preview.ZoomInCommand.Execute(null);
        }

        Assert.Equal(PdfPreviewZoom.MaxZoom, preview.Zoom);
        Assert.False(preview.ZoomInCommand.CanExecute(null));
        Assert.Equal("400%", preview.ZoomText);

        for (var i = 0; i < 40 && preview.ZoomOutCommand.CanExecute(null); i++)
        {
            preview.ZoomOutCommand.Execute(null);
        }

        Assert.Equal(PdfPreviewZoom.MinZoom, preview.Zoom);
        Assert.False(preview.ZoomOutCommand.CanExecute(null));

        preview.FitWidthCommand.Execute(null);
        Assert.Equal(PdfPreviewFitMode.FitWidth, preview.FitMode);
        Assert.Equal(PdfPreviewZoom.FitWidth(new PdfPageSize(612, 792), 460), preview.Zoom, 4);
        Assert.Equal(436, preview.PageDisplayWidth, 0); // 460 − 2×12 padding

        preview.FitPageCommand.Execute(null);
        Assert.Equal(PdfPreviewZoom.FitPage(new PdfPageSize(612, 792), 460, 700), preview.Zoom, 4);
        Assert.True(preview.PageDisplayHeight <= 700 - 24 + 0.5);
    }

    [Fact]
    public void ZoomMath_ClampsAbsurdValues()
    {
        Assert.Equal(PdfPreviewZoom.MaxZoom, PdfPreviewZoom.Clamp(50));
        Assert.Equal(PdfPreviewZoom.MinZoom, PdfPreviewZoom.Clamp(0.01));
        Assert.Equal(1.0, PdfPreviewZoom.Clamp(double.NaN));
        Assert.Equal(PdfPreviewZoom.MaxZoom, PdfPreviewZoom.StepIn(PdfPreviewZoom.MaxZoom));
        Assert.Equal(PdfPreviewZoom.MinZoom, PdfPreviewZoom.StepOut(PdfPreviewZoom.MinZoom));
    }

    [Fact]
    public async Task ZoomAndFitMode_ArePersisted()
    {
        var harness = new Harness();
        var preview = harness.Create();
        preview.ZoomInCommand.Execute(null);
        await preview.PendingPreferenceSave;
        Assert.Equal(PdfPreviewFitMode.Custom, harness.Store.Current.FitMode);
        Assert.Equal(preview.Zoom, harness.Store.Current.Zoom);

        preview.FitPageCommand.Execute(null);
        await preview.PendingPreferenceSave;
        Assert.Equal(PdfPreviewFitMode.FitPage, harness.Store.Current.FitMode);
    }

    // 19. Print with the preview visible prints the exact bytes on screen — no regeneration.
    [Fact]
    public async Task Print_WithPreviewVisible_SendsTheDisplayedDocument()
    {
        var harness = new Harness();
        var preview = harness.Create();
        preview.NotifyContentChanged();
        await SettleAsync(preview, harness);
        var shown = preview.CurrentDocument!;

        await preview.PrintCommand.ExecuteAsync(null);

        var request = Assert.Single(harness.Printer.Requests);
        Assert.Same(shown.Bytes, request.PdfBytes);
        Assert.Equal(shown.PageCount, request.PageCount);
        Assert.Contains("2026-08-21", request.DocumentName);
        Assert.Single(harness.Export.Calls);
        Assert.Equal("Sent to Microsoft Print to PDF.", preview.ActionMessage);
        Assert.False(preview.ActionMessageIsError);
    }

    // 20. Print with the preview hidden generates on demand from the same pipeline.
    [Fact]
    public async Task Print_WithPreviewHidden_GeneratesOnDemand()
    {
        var harness = new Harness { Store = new InMemoryPdfPreviewLayoutPreferenceStore(PdfPreviewLayoutPreferences.BuiltIn with { IsPreviewVisible = false }) };
        harness.Workspace = harness.WithAttendance(808);
        var preview = harness.Create();
        await preview.PendingPreferenceLoad;
        Assert.False(preview.IsPreviewPaneShown);

        await preview.PrintCommand.ExecuteAsync(null);

        var request = Assert.Single(harness.Printer.Requests);
        Assert.Contains("attendance=808", System.Text.Encoding.UTF8.GetString(request.PdfBytes));
        Assert.Empty(harness.Rasterizer.Opened); // nothing rasterized for display while hidden
        Assert.Same(preview.CurrentDocument!.Bytes, request.PdfBytes);
    }

    // 21. A cancelled print dialog is normal: no message, no error.
    [Fact]
    public async Task Print_Cancelled_ShowsNoMessage()
    {
        var harness = new Harness();
        harness.Printer.Result = _ => PdfPrintResult.Cancelled();
        var preview = harness.Create();

        await preview.PrintCommand.ExecuteAsync(null);

        Assert.Null(preview.ActionMessage);
        Assert.False(preview.IsPrinting);
    }

    // 22. No printer available → an understandable message.
    [Fact]
    public async Task Print_PrinterUnavailable_ShowsTheReason()
    {
        var harness = new Harness();
        harness.Printer.Result = _ => PdfPrintResult.Unavailable("No printer is available.");
        var preview = harness.Create();

        await preview.PrintCommand.ExecuteAsync(null);

        Assert.Equal("No printer is available.", preview.ActionMessage);
        Assert.True(preview.ActionMessageIsError);
    }

    // 23. A failed job (or an unexpected exception) is reported, never thrown.
    [Fact]
    public async Task Print_JobFailure_AndUnexpectedException_AreReported()
    {
        var harness = new Harness();
        harness.Printer.Result = _ => PdfPrintResult.Failed("The printer stopped accepting the print job.");
        var preview = harness.Create();

        await preview.PrintCommand.ExecuteAsync(null);
        Assert.Equal("The printer stopped accepting the print job.", preview.ActionMessage);

        harness.Printer.Throws = new InvalidOperationException("driver crashed");
        await preview.PrintCommand.ExecuteAsync(null);
        Assert.Equal("Printing failed. Try again.", preview.ActionMessage);
        Assert.True(preview.ActionMessageIsError);
        Assert.False(preview.IsPrinting);
        Assert.True(preview.PrintCommand.CanExecute(null));
    }

    [Fact]
    public async Task Print_WhenTheDocumentCannotBeGenerated_DoesNotCallThePrinter()
    {
        var harness = new Harness();
        harness.Export.Throws = new InvalidOperationException("boom");
        var preview = harness.Create();

        await preview.PrintCommand.ExecuteAsync(null);

        Assert.Empty(harness.Printer.Requests);
        Assert.True(preview.ActionMessageIsError);
    }

    [Fact]
    public async Task Print_WhileTheDateIsLoading_IsRefused_WithAMessage()
    {
        var harness = new Harness { Loaded = false };
        var preview = harness.Create();

        await preview.PrintCommand.ExecuteAsync(null);

        Assert.Empty(harness.Printer.Requests);
        Assert.Contains("Try again in a moment", preview.ActionMessage);
    }

    // 24. An edit still inside the debounce window is generated before printing.
    [Fact]
    public async Task Print_AfterAnUnrenderedEdit_PrintsTheLatestState()
    {
        var harness = new Harness();
        var preview = harness.Create();
        preview.NotifyContentChanged();
        await SettleAsync(preview, harness);

        harness.Workspace = harness.WithAttendance(999);
        preview.NotifyContentChanged(); // debounce pending, not elapsed

        await preview.PrintCommand.ExecuteAsync(null);

        var printed = System.Text.Encoding.UTF8.GetString(harness.Printer.Requests.Single().PdfBytes);
        Assert.Contains("attendance=999", printed);
        Assert.Equal(preview.ContentVersion, preview.CurrentDocument!.ContentVersion);
        Assert.Same(preview.CurrentDocument.Bytes, harness.Printer.Requests.Single().PdfBytes);
    }

    // 25. Save PDF writes the exact generated bytes with the established file name.
    [Fact]
    public async Task SavePdf_SavesTheCurrentDocumentsBytes()
    {
        var harness = new Harness();
        var preview = harness.Create();
        preview.NotifyContentChanged();
        await SettleAsync(preview, harness);

        await preview.SavePdfCommand.ExecuteAsync(null);

        var call = Assert.Single(harness.Picker.Calls);
        Assert.Same(preview.CurrentDocument!.Bytes, call.Bytes);
        Assert.Equal("Carowinds-Operations-2026-08-21.pdf", call.SuggestedFileName);
        Assert.Equal("PDF saved.", preview.ActionMessage);
        Assert.Single(harness.Export.Calls);
    }

    [Fact]
    public async Task SavePdf_FailureAndCancel_AreReportedCorrectly()
    {
        var harness = new Harness();
        var preview = harness.Create();
        harness.Picker.ResultFactory = () => PdfSaveResult.Failed("The PDF could not be saved there.");
        await preview.SavePdfCommand.ExecuteAsync(null);
        Assert.Equal("The PDF could not be saved there.", preview.ActionMessage);
        Assert.True(preview.ActionMessageIsError);

        harness.Picker.ResultFactory = PdfSaveResult.Cancelled;
        await preview.SavePdfCommand.ExecuteAsync(null);
        Assert.Null(preview.ActionMessage);
    }

    [Fact]
    public void PrintAndSave_FollowTheEditorsExportAvailability()
    {
        var harness = new Harness { CanExport = false };
        var preview = harness.Create();
        Assert.False(preview.PrintCommand.CanExecute(null));
        Assert.False(preview.SavePdfCommand.CanExecute(null));

        harness.CanExport = true;
        preview.RefreshCommandStates();
        Assert.True(preview.PrintCommand.CanExecute(null));
        Assert.True(preview.SavePdfCommand.CanExecute(null));
    }

    [Fact]
    public async Task UnsupportedPlatform_PrintServiceExplainsSavePdf()
    {
        var service = new UnsupportedPlatformPdfPrintService();
        Assert.False(service.IsSupported);
        var result = await service.PrintAsync(new PdfPrintRequest([1], "x", 1));
        Assert.Equal(PdfPrintOutcome.Unavailable, result.Outcome);
        Assert.Contains("Save PDF", result.Message);
    }

    [Fact]
    public void Fingerprint_IgnoresGenerationTimestamps_ButNotContent()
    {
        var a = System.Text.Encoding.Latin1.GetBytes("%PDF /CreationDate (D:20260821150000Z) /ModDate (D:20260821150000Z) body");
        var b = System.Text.Encoding.Latin1.GetBytes("%PDF /CreationDate (D:20260821160501Z) /ModDate (D:20260821160501Z) body");
        var c = System.Text.Encoding.Latin1.GetBytes("%PDF /CreationDate (D:20260821150000Z) /ModDate (D:20260821150000Z) BODY");

        Assert.Equal(PdfContentFingerprint.Compute(a), PdfContentFingerprint.Compute(b));
        Assert.NotEqual(PdfContentFingerprint.Compute(a), PdfContentFingerprint.Compute(c));
    }
}
