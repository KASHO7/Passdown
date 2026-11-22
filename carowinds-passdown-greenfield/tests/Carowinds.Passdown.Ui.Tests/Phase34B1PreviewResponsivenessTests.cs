using System.Text;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.PdfOutput;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 34B-1: live PDF preview responsiveness — leading-edge refresh plus a trailing quiet window,
/// serialized refresh passes, immediate refresh on workspace replacement, and the delayed "Updating…" chip.
/// Fully deterministic: the quiet window and the chip delay are <see cref="ManualDebounceScheduler"/>s, preview
/// work runs inline (or is held behind a gate), and there is no real clock or sleep anywhere.
/// </summary>
public class Phase34B1PreviewResponsivenessTests
{
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10); // a hang fails the test instead of the run

    private sealed class Harness
    {
        public AssociateWorkspace Workspace { get; set; } = FakeAssociatePassdownService.DefaultWorkspace(Today);

        public DateOnly Date { get; set; } = Today;

        public AssociatePassdownTemplate Template { get; set; } = AssociatePassdownTemplate.Classic;

        public bool Loaded { get; set; } = true;

        public ContentEchoPdfExportService Export { get; } = new();

        public FakePdfRasterizer Rasterizer { get; } = new();

        public FakePdfPageImageFactory Images { get; } = new();

        public InlinePreviewWorkScheduler Scheduler { get; } = new();

        public ManualDebounceScheduler Window { get; } = new();

        public ManualDebounceScheduler Indicator { get; } = new();

        public FakePdfPrintService Printer { get; } = new();

        public FakePdfSaveFilePicker Picker { get; } = new();

        public InMemoryPdfPreviewLayoutPreferenceStore Store { get; init; } = new();

        public PassdownPdfPreviewViewModel Create()
        {
            var preview = new PassdownPdfPreviewViewModel(
                () => Loaded ? PassdownPdfCapture.Ready(new PassdownPdfRequest(Workspace, Date, Template)) : PassdownPdfCapture.Waiting("Loading this date's preview…"),
                () => true,
                Export,
                Rasterizer,
                Printer,
                Picker,
                Store,
                Images,
                Scheduler,
                debounce: null, // the production quiet window
                delay: Window.Delay,
                now: () => new DateTimeOffset(2026, 8, 21, 15, 0, 0, TimeSpan.Zero),
                updatingIndicatorDelay: Indicator.Delay);
            preview.UpdateAvailableWidth(1400);
            preview.UpdateViewport(460, 700, 1.0);
            return preview;
        }

        public void SetAttendance(int attendance) =>
            Workspace = Workspace with { Draft = Workspace.Draft with { ProjectedAttendance = attendance } };

        public void SetNotes(string text) =>
            Workspace = Workspace with { Draft = Workspace.Draft with { OperationalNotes = [text] } };

        public string Text(PassdownPdfDocument? document) => document is null ? "" : Encoding.UTF8.GetString(document.Bytes);

        public string LastGeneratedAttendance => Export.Calls[^1].Workspace.Draft.ProjectedAttendance?.ToString() ?? "";
    }

    /// <summary>A shown preview with one settled page, ready for the scenario under test.</summary>
    private static async Task<(Harness Harness, PassdownPdfPreviewViewModel Preview)> ShownAsync(Action<Harness>? configure = null)
    {
        var harness = new Harness();
        configure?.Invoke(harness);
        var preview = harness.Create();
        preview.NotifyContentChanged();
        await SettleAsync(harness, preview);
        Assert.True(preview.HasPage);
        Assert.False(preview.IsQuietWindowOpen);
        return (harness, preview);
    }

    private static async Task SettleAsync(Harness harness, PassdownPdfPreviewViewModel preview)
    {
        harness.Window.ElapseLatest();
        await preview.PendingRefresh.WaitAsync(Guard);
    }

    // ------------------------------------------------------------------ 1-3: leading edge, typing burst

    [Fact]
    public async Task FirstChangeAfterIdle_RefreshesImmediately_WithoutWaitingForTheWindow()
    {
        var (harness, preview) = await ShownAsync();
        var before = harness.Export.Calls.Count;

        harness.SetAttendance(777);
        preview.NotifyContentChanged();

        // No delay elapsed: the generation and the new page already happened.
        Assert.Equal(before + 1, harness.Export.Calls.Count);
        Assert.Contains("attendance=777", harness.Text(preview.CurrentDocument));
        Assert.Equal(PdfPreviewStatus.Ready, preview.Status);
        Assert.True(preview.IsQuietWindowOpen);
        Assert.False(preview.IsTrailingRefreshPending); // nothing changed after the leading refresh
    }

    [Fact]
    public async Task IsolatedChange_ThenQuietWindowElapses_DoesNotGenerateAgain()
    {
        var (harness, preview) = await ShownAsync();
        harness.SetAttendance(1);
        preview.NotifyContentChanged();
        var after = harness.Export.Calls.Count;

        await SettleAsync(harness, preview);

        Assert.Equal(after, harness.Export.Calls.Count);
        Assert.Equal(0, preview.TrailingRefreshCount);
        Assert.False(preview.IsQuietWindowOpen);
    }

    [Fact]
    public async Task FifteenKeyTypingBurst_GivesAtMostTwoGenerations_AndShowsTheFinalText()
    {
        var (harness, preview) = await ShownAsync();
        var before = harness.Export.Calls.Count;
        var text = "";

        for (var key = 0; key < 15; key++)
        {
            text += (char)('a' + key);
            harness.SetNotes(text);
            preview.NotifyContentChanged();
        }

        Assert.Equal(before + 1, harness.Export.Calls.Count); // only the first keystroke so far
        Assert.True(preview.IsTrailingRefreshPending);

        await SettleAsync(harness, preview);

        Assert.Equal(before + 2, harness.Export.Calls.Count); // leading + one trailing refresh
        Assert.Contains("notes=abcdefghijklmno", harness.Text(preview.CurrentDocument));
        Assert.Equal(PdfPreviewStatus.Ready, preview.Status);
        Assert.Equal(1, preview.TrailingRefreshCount);
    }

    [Fact]
    public async Task ChangesInsideTheWindow_RestartIt_AndOnlyTheLatestWindowTriggersTheTrailingRefresh()
    {
        var (harness, preview) = await ShownAsync();
        var windowsBefore = harness.Window.ScheduleCount;

        harness.SetAttendance(1);
        preview.NotifyContentChanged();
        harness.SetAttendance(2);
        preview.NotifyContentChanged();
        harness.SetAttendance(3);
        preview.NotifyContentChanged();

        Assert.Equal(windowsBefore + 3, harness.Window.ScheduleCount); // restarted on every change
        var generations = harness.Export.Calls.Count;

        await SettleAsync(harness, preview); // only the newest (uncancelled) window can elapse

        Assert.Equal(generations + 1, harness.Export.Calls.Count);
        Assert.Equal("3", harness.LastGeneratedAttendance);
        Assert.False(preview.IsQuietWindowOpen);
    }

    // ------------------------------------------------------------------ 4-6: stale results

    [Fact]
    public async Task RapidCheckboxToggles_WhileTheLeadingRefreshIsStillGenerating_EndOnTheFinalState()
    {
        var (harness, preview) = await ShownAsync();
        var before = harness.Export.Calls.Count;
        var gate = harness.Scheduler.HoldNext();

        harness.SetAttendance(1);
        preview.NotifyContentChanged(); // leading refresh, held mid-generation
        for (var i = 2; i <= 5; i++)
        {
            harness.SetAttendance(i);
            preview.NotifyContentChanged();
        }

        harness.Window.ElapseLatest(); // trailing refresh is requested while the leading one still runs
        Assert.Equal(before, harness.Export.Calls.Count); // and is folded in — never a parallel generation

        gate.SetResult();
        await preview.PendingRefresh.WaitAsync(Guard);

        Assert.Equal(before + 2, harness.Export.Calls.Count);
        Assert.Equal("5", harness.LastGeneratedAttendance);
        Assert.Contains("attendance=5", harness.Text(preview.CurrentDocument));
        Assert.Equal(PdfPreviewStatus.Ready, preview.Status);
        Assert.True(preview.HasPage);
    }

    [Fact]
    public async Task OlderPrintGeneration_FinishingAfterANewerPreview_NeverReplacesIt()
    {
        var (harness, preview) = await ShownAsync();
        harness.SetAttendance(100);
        preview.NotifyContentChanged();
        await SettleAsync(harness, preview);

        // Every edit notifies, as the editor does: 250 refreshes at once, 260 waits for the trailing refresh.
        harness.SetAttendance(250);
        preview.NotifyContentChanged();
        harness.SetAttendance(260);
        preview.NotifyContentChanged();

        // Print captures 260 (cancelling the trailing refresh) and is held mid-generation …
        var gate = harness.Scheduler.HoldNext();
        var print = preview.PrintCommand.ExecuteAsync(null);

        // … while a newer edit's leading refresh completes first.
        harness.SetAttendance(300);
        preview.NotifyContentChanged();
        Assert.Contains("attendance=300", harness.Text(preview.CurrentDocument));

        gate.SetResult();
        await print.WaitAsync(Guard);

        Assert.Contains("attendance=260", Encoding.UTF8.GetString(harness.Printer.Requests.Single().PdfBytes)); // what Print captured
        Assert.Contains("attendance=300", harness.Text(preview.CurrentDocument)); // the newer page is kept
        Assert.Equal(PdfPreviewStatus.Ready, preview.Status);
    }

    // ------------------------------------------------------------------ 7-9: date switch, workspace replacement

    [Fact]
    public async Task DateChange_ClearsThePreviousPdfImmediately_AndCancelsTheQuietWindow()
    {
        var (harness, preview) = await ShownAsync();
        harness.SetAttendance(5);
        preview.NotifyContentChanged();
        harness.SetAttendance(6);
        preview.NotifyContentChanged(); // trailing pending
        var generations = harness.Export.Calls.Count;

        harness.Loaded = false;
        preview.NotifySelectedDateChanged();

        Assert.False(preview.HasPage);
        Assert.Null(preview.CurrentDocument);
        Assert.Equal(PdfPreviewStatus.Waiting, preview.Status);
        Assert.False(preview.IsQuietWindowOpen);
        Assert.False(preview.IsTrailingRefreshPending);

        harness.Window.ElapseLatest(); // a cancelled window can never fire a refresh
        await preview.PendingRefresh.WaitAsync(Guard);
        Assert.Equal(generations, harness.Export.Calls.Count);
    }

    [Fact]
    public async Task LatePreviousDateGeneration_IsNeverInstalledOrShown()
    {
        var (harness, preview) = await ShownAsync();
        var gate = harness.Scheduler.HoldNext();
        harness.SetAttendance(41);
        preview.NotifyContentChanged(); // leading refresh for the old date, held
        var stale = preview.PendingRefresh;

        harness.Loaded = false;
        preview.NotifySelectedDateChanged();
        gate.SetResult();
        harness.Window.ElapseLatest();
        await stale.WaitAsync(Guard);

        Assert.Null(preview.CurrentDocument);
        Assert.False(preview.HasPage);
        Assert.Equal(PdfPreviewStatus.Waiting, preview.Status);
    }

    [Fact]
    public async Task WorkspaceReplaced_RefreshesImmediately_WithNoQuietWindow()
    {
        var (harness, preview) = await ShownAsync();
        harness.Loaded = false;
        preview.NotifySelectedDateChanged();
        var windows = harness.Window.ScheduleCount;
        var generations = harness.Export.Calls.Count;

        harness.Date = Today.AddDays(1);
        harness.Workspace = FakeAssociatePassdownService.DefaultWorkspace(Today.AddDays(1));
        harness.Loaded = true;
        preview.NotifyWorkspaceReplaced();
        await preview.PendingRefresh.WaitAsync(Guard);

        Assert.Equal(generations + 1, harness.Export.Calls.Count);
        Assert.Equal(windows, harness.Window.ScheduleCount); // no delay was involved at all
        Assert.Equal(Today.AddDays(1), preview.CurrentDocument!.Date);
        Assert.True(preview.HasPage);
        Assert.Equal(PdfPreviewStatus.Ready, preview.Status);
    }

    [Fact]
    public async Task WorkspaceReplaced_WhileTheSelectedDateIsNotLoaded_NeverGenerates()
    {
        var (harness, preview) = await ShownAsync();
        harness.Loaded = false;
        preview.NotifySelectedDateChanged();
        var generations = harness.Export.Calls.Count;

        preview.NotifyWorkspaceReplaced(); // e.g. the previous date's workspace swapped out
        await preview.PendingRefresh.WaitAsync(Guard);

        Assert.Equal(generations, harness.Export.Calls.Count);
        Assert.Equal(PdfPreviewStatus.Waiting, preview.Status);
        Assert.Null(preview.CurrentDocument);
        Assert.False(preview.HasPage);
    }

    [Fact]
    public async Task WorkspaceReplaced_DuringATypingWindow_SupersedesTheTrailingRefresh()
    {
        var (harness, preview) = await ShownAsync();
        harness.SetAttendance(1);
        preview.NotifyContentChanged();
        harness.SetAttendance(2);
        preview.NotifyContentChanged(); // trailing pending
        var generations = harness.Export.Calls.Count;

        preview.NotifyWorkspaceReplaced(); // e.g. Check Online applied — captures the newest editor state now
        harness.Window.ElapseLatest();
        await preview.PendingRefresh.WaitAsync(Guard);

        Assert.Equal(generations + 1, harness.Export.Calls.Count);
        Assert.Equal("2", harness.LastGeneratedAttendance);
        Assert.False(preview.IsTrailingRefreshPending);
    }

    // ------------------------------------------------------------------ 11-13: hide / show, page and zoom

    [Fact]
    public async Task HidingThePreview_CancelsTheScheduledTrailingRefresh()
    {
        var (harness, preview) = await ShownAsync();
        harness.SetAttendance(1);
        preview.NotifyContentChanged();
        harness.SetAttendance(2);
        preview.NotifyContentChanged(); // trailing pending
        var generations = harness.Export.Calls.Count;

        preview.HidePreviewCommand.Execute(null);
        harness.Window.ElapseLatest();
        await preview.PendingRefresh.WaitAsync(Guard);

        Assert.False(preview.IsQuietWindowOpen);
        Assert.Equal(generations, harness.Export.Calls.Count);
        Assert.False(preview.HasPage);
        Assert.Equal(PdfPreviewStatus.Idle, preview.Status);
    }

    [Fact]
    public async Task ShowingThePreviewAgain_DisplaysTheLatestContent()
    {
        var (harness, preview) = await ShownAsync();
        harness.SetAttendance(1);
        preview.NotifyContentChanged();
        harness.SetAttendance(2);
        preview.NotifyContentChanged();
        preview.HidePreviewCommand.Execute(null);
        harness.SetAttendance(3);
        preview.NotifyContentChanged(); // hidden: nothing generated

        preview.ShowPreviewCommand.Execute(null);
        await preview.PendingRefresh.WaitAsync(Guard);

        Assert.True(preview.HasPage);
        Assert.Equal("3", harness.LastGeneratedAttendance);
        Assert.Contains("attendance=3", harness.Text(preview.CurrentDocument));
        Assert.Equal(PdfPreviewStatus.Ready, preview.Status);
    }

    [Fact]
    public async Task LeadingAndTrailingRefreshes_KeepThePageNumberAndZoom()
    {
        var (harness, preview) = await ShownAsync(h => h.Rasterizer.PageCountFor = _ => 3);
        preview.NextPageCommand.Execute(null);
        await preview.PendingPageRender.WaitAsync(Guard);
        preview.ZoomInCommand.Execute(null);
        harness.Window.ElapseLatest(); // the zoom's page re-render delay
        await preview.PendingPageRender.WaitAsync(Guard);
        var zoom = preview.Zoom;

        harness.SetAttendance(1);
        preview.NotifyContentChanged(); // leading
        Assert.Equal(1, preview.PageIndex);
        harness.SetAttendance(2);
        preview.NotifyContentChanged();
        await SettleAsync(harness, preview); // trailing

        Assert.Equal(1, preview.PageIndex);
        Assert.Equal(3, preview.PageCount);
        Assert.Equal("Page 2 of 3", preview.PageIndicatorAccessibleText);
        Assert.Equal(zoom, preview.Zoom);
        Assert.Equal(PdfPreviewFitMode.Custom, preview.FitMode);
    }

    // ------------------------------------------------------------------ 14-15: Print / Save PDF

    [Fact]
    public async Task Print_WithATrailingRefreshPending_PrintsTheNewestState_AndCancelsTheTrailingWork()
    {
        var (harness, preview) = await ShownAsync();
        harness.SetAttendance(1);
        preview.NotifyContentChanged();
        harness.SetAttendance(2);
        preview.NotifyContentChanged(); // the preview still shows 1; 2 is pending

        await preview.PrintCommand.ExecuteAsync(null).WaitAsync(Guard);

        Assert.Contains("attendance=2", Encoding.UTF8.GetString(harness.Printer.Requests.Single().PdfBytes));
        Assert.Contains("attendance=2", harness.Text(preview.CurrentDocument));
        Assert.False(preview.IsTrailingRefreshPending);
        var generations = harness.Export.Calls.Count;
        harness.Window.ElapseLatest();
        await preview.PendingRefresh.WaitAsync(Guard);
        Assert.Equal(generations, harness.Export.Calls.Count); // no duplicate trailing generation
    }

    [Fact]
    public async Task SavePdf_WhileTheLeadingRefreshIsGenerating_SavesTheNewestState()
    {
        var (harness, preview) = await ShownAsync();
        var gate = harness.Scheduler.HoldNext();
        harness.SetAttendance(1);
        preview.NotifyContentChanged(); // leading refresh for 1, held
        harness.SetAttendance(2);
        preview.NotifyContentChanged();

        await preview.SavePdfCommand.ExecuteAsync(null).WaitAsync(Guard);
        gate.SetResult();
        harness.Window.ElapseLatest();
        await preview.PendingRefresh.WaitAsync(Guard);

        Assert.Contains("attendance=2", Encoding.UTF8.GetString(harness.Picker.Calls.Single().Bytes));
        Assert.Contains("attendance=2", harness.Text(preview.CurrentDocument)); // the late "1" never replaced it
    }

    [Fact]
    public async Task Print_WhileTheNewDateIsLoading_IsRefused()
    {
        var (harness, preview) = await ShownAsync();
        harness.Loaded = false;
        preview.NotifySelectedDateChanged();

        await preview.PrintCommand.ExecuteAsync(null).WaitAsync(Guard);

        Assert.Empty(harness.Printer.Requests);
        Assert.True(preview.ActionMessageIsError);
        Assert.Contains("Try again", preview.ActionMessage);
    }

    [Fact]
    public async Task Print_WhenTheDateChangesMidGeneration_PrintsNothing()
    {
        var (harness, preview) = await ShownAsync();
        harness.SetAttendance(9);
        preview.NotifyContentChanged();
        harness.SetAttendance(10);
        preview.NotifyContentChanged(); // newer than the displayed page, so Print must generate
        var gate = harness.Scheduler.HoldNext();
        var print = preview.PrintCommand.ExecuteAsync(null); // generating the old date's PDF

        harness.Loaded = false;
        preview.NotifySelectedDateChanged();
        gate.SetResult();
        await print.WaitAsync(Guard);

        Assert.Empty(harness.Printer.Requests);
        Assert.True(preview.ActionMessageIsError);
        Assert.Contains("date changed", preview.ActionMessage);
        Assert.Null(preview.CurrentDocument);
    }

    // ------------------------------------------------------------------ 17-18: "Updating…" chip

    [Fact]
    public async Task QuickRefreshes_NeverShowTheUpdatingChip()
    {
        var (harness, preview) = await ShownAsync();
        var shown = 0;
        preview.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PassdownPdfPreviewViewModel.IsUpdatingIndicatorVisible) && preview.IsUpdatingIndicatorVisible)
            {
                shown++;
            }
        };

        for (var i = 0; i < 10; i++)
        {
            harness.SetAttendance(i);
            preview.NotifyContentChanged();
        }

        await SettleAsync(harness, preview);
        harness.Indicator.ElapseLatest(); // every chip delay was already cancelled when its generation finished

        Assert.Equal(0, shown);
        Assert.False(preview.IsUpdatingIndicatorVisible);
        Assert.False(preview.IsUpdating);
    }

    [Fact]
    public async Task SlowRefresh_ShowsTheChipOnlyAfterTheDelay_AndHidesItWhenDone()
    {
        var (harness, preview) = await ShownAsync();
        var gate = harness.Scheduler.HoldNext();
        harness.SetAttendance(1);
        preview.NotifyContentChanged();

        Assert.True(preview.IsUpdating);
        Assert.False(preview.IsUpdatingIndicatorVisible); // not yet: the generation might still be quick
        Assert.True(preview.HasPage); // the current page stays visible meanwhile

        harness.Indicator.ElapseLatest(); // still generating after the delay
        Assert.True(preview.IsUpdatingIndicatorVisible);

        gate.SetResult();
        harness.Window.ElapseLatest();
        await preview.PendingRefresh.WaitAsync(Guard);
        Assert.False(preview.IsUpdatingIndicatorVisible);
        Assert.Equal(PdfPreviewStatus.Ready, preview.Status);
    }

    [Fact]
    public async Task ChipDelay_ElapsingAfterThePreviewWasHidden_ShowsNothing()
    {
        var (harness, preview) = await ShownAsync();
        var gate = harness.Scheduler.HoldNext();
        harness.SetAttendance(1);
        preview.NotifyContentChanged();

        preview.HidePreviewCommand.Execute(null);
        harness.Indicator.ElapseLatest();
        gate.SetResult();
        harness.Window.ElapseLatest();
        await preview.PendingRefresh.WaitAsync(Guard);

        Assert.False(preview.IsUpdatingIndicatorVisible);
    }

    [Fact]
    public async Task ContinuousTyping_WithSlowGenerations_AnnouncesTheChipAtMostOncePerGeneration()
    {
        var (harness, preview) = await ShownAsync();
        var announcements = 0;
        preview.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PassdownPdfPreviewViewModel.IsUpdatingIndicatorVisible) && preview.IsUpdatingIndicatorVisible)
            {
                announcements++;
            }
        };

        var leadingGate = harness.Scheduler.HoldNext();
        var text = "";
        for (var key = 0; key < 15; key++)
        {
            text += "k";
            harness.SetNotes(text);
            preview.NotifyContentChanged();
            harness.Indicator.ElapseLatest(); // every keystroke happens "after" the chip delay
        }

        var trailingGate = harness.Scheduler.HoldNext();
        harness.Window.ElapseLatest();
        leadingGate.SetResult();
        await Task.Yield();
        harness.Indicator.ElapseLatest();
        trailingGate.SetResult();
        await preview.PendingRefresh.WaitAsync(Guard);

        Assert.InRange(announcements, 1, 2); // one per real generation — never one per keystroke
        Assert.False(preview.IsUpdatingIndicatorVisible);
        Assert.Contains($"notes={text}", harness.Text(preview.CurrentDocument));
    }

    [Fact]
    public void Markup_BindsTheChipToTheDelayedIndicator_AndKeepsItAccessible()
    {
        var markup = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/PassdownPdfPreviewView.axaml"));
        var chipStart = markup.IndexOf("x:Name=\"UpdatingChip\"", StringComparison.Ordinal);
        Assert.True(chipStart >= 0);
        var chip = markup[chipStart..markup.IndexOf("</Border>", chipStart, StringComparison.Ordinal)];

        Assert.Contains("IsVisible=\"{Binding IsUpdatingIndicatorVisible}\"", chip);
        Assert.DoesNotContain("{Binding IsUpdating}", chip);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", chip);
    }

    // ------------------------------------------------------------------ policy constants

    [Fact]
    public void Timings_AreTheDocumentedPhase34B1Values()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(200), PassdownPdfPreviewViewModel.DefaultDebounce);
        Assert.Equal(TimeSpan.FromMilliseconds(150), PassdownPdfPreviewViewModel.UpdatingIndicatorDelay);
    }
}
