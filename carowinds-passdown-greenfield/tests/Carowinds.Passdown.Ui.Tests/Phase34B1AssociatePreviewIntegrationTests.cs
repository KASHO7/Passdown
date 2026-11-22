using System.Text;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.PdfOutput;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 34B-1: the faster preview scheduling wired into a real <see cref="AssociateViewModel"/> — date
/// loads refresh as soon as the workspace is ready, editor typing is coalesced, section navigation never
/// regenerates, Print uses the newest edits, and autosave never depends on the preview. Fakes only.
/// </summary>
public class Phase34B1AssociatePreviewIntegrationTests
{
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    private sealed class Rig
    {
        public FakeAssociatePassdownService Service { get; } = new();

        public ManualDebounceScheduler Autosave { get; } = new();

        public ManualDebounceScheduler PreviewWindow { get; } = new();

        public ContentEchoPdfExportService Export { get; } = new();

        public FakePdfRasterizer Rasterizer { get; } = new();

        public FakePdfPrintService Printer { get; } = new();

        public AssociateViewModel Create()
        {
            var viewModel = new AssociateViewModel(
                Service,
                new FakeClock(FixedNow),
                autosaveDelay: Autosave.Delay,
                pdfExportService: Export,
                pdfSavePicker: new FakePdfSaveFilePicker(),
                templatePreferenceStore: new FakeAssociatePassdownTemplatePreferenceStore(),
                pdfRasterizer: Rasterizer,
                pdfPrintService: Printer,
                pdfPreviewLayoutStore: new InMemoryPdfPreviewLayoutPreferenceStore(),
                pdfPageImageFactory: new FakePdfPageImageFactory(),
                previewWorkScheduler: new InlinePreviewWorkScheduler(),
                previewDelay: PreviewWindow.Delay);
            viewModel.Preview.UpdateAvailableWidth(1500);
            viewModel.Preview.UpdateViewport(460, 700, 1.0);
            return viewModel;
        }

        public async Task SettleAsync(AssociateViewModel viewModel)
        {
            PreviewWindow.ElapseLatest();
            await viewModel.Preview.PendingRefresh.WaitAsync(Guard);
        }

        public string LastGeneratedText => Export.Calls.Count == 0
            ? ""
            : Encoding.UTF8.GetString(new ContentEchoPdfExportService().GeneratePdf(Export.Calls[^1].Workspace, FixedNow, Export.Calls[^1].Template).PdfBytes!);
    }

    private static async Task<(Rig Rig, AssociateViewModel ViewModel)> LoadedAsync()
    {
        var rig = new Rig();
        var viewModel = rig.Create();
        await viewModel.PendingLoad.WaitAsync(Guard);
        await viewModel.PendingTemplatePreferenceLoad.WaitAsync(Guard);
        await rig.SettleAsync(viewModel);
        Assert.True(viewModel.Preview.HasPage);
        return (rig, viewModel);
    }

    [Fact]
    public async Task FirstPreview_AfterTheInitialLoad_NeedsNoQuietWindow()
    {
        var rig = new Rig();
        var viewModel = rig.Create();
        await viewModel.PendingLoad.WaitAsync(Guard);
        await viewModel.PendingTemplatePreferenceLoad.WaitAsync(Guard);
        await viewModel.Preview.PendingRefresh.WaitAsync(Guard); // no window elapsed

        Assert.True(viewModel.Preview.HasPage);
        Assert.Equal(Today, viewModel.Preview.CurrentDocument!.Date);
    }

    [Fact]
    public async Task DateSwitch_RefreshesAsSoonAsTheNewWorkspaceLoads()
    {
        var (rig, viewModel) = await LoadedAsync();
        rig.Service.LoadGate = new TaskCompletionSource<bool>();
        var generations = rig.Export.Calls.Count;

        viewModel.SelectedDate = Today.AddDays(1);
        Assert.False(viewModel.Preview.HasPage);
        Assert.Equal(PdfPreviewStatus.Waiting, viewModel.Preview.Status);
        Assert.Equal(generations, rig.Export.Calls.Count); // nothing generated while the date loads

        rig.Service.LoadGate.SetResult(true);
        await viewModel.PendingLoad.WaitAsync(Guard);
        await viewModel.Preview.PendingRefresh.WaitAsync(Guard); // no window elapsed

        Assert.Equal(Today.AddDays(1), viewModel.Preview.CurrentDocument!.Date);
        Assert.True(viewModel.Preview.HasPage);
        Assert.Equal(PdfPreviewStatus.Ready, viewModel.Preview.Status);
        Assert.All(rig.Export.Calls.Skip(generations), call => Assert.Equal(Today.AddDays(1), call.Workspace.OperatingDate));
    }

    [Fact]
    public async Task IncludeInPassdownCheckbox_RefreshesImmediately()
    {
        var (rig, viewModel) = await LoadedAsync();
        var generations = rig.Export.Calls.Count;

        viewModel.IncludeFastLane = !viewModel.IncludeFastLane;

        Assert.Equal(generations + 1, rig.Export.Calls.Count); // no window elapsed
        Assert.Equal(PdfPreviewStatus.Ready, viewModel.Preview.Status);
    }

    [Fact]
    public async Task TypingInOperationalNotes_IsCoalesced_IntoAtMostTwoGenerations()
    {
        var (rig, viewModel) = await LoadedAsync();
        var generations = rig.Export.Calls.Count;

        for (var key = 0; key < 15; key++)
        {
            viewModel.OperationalNotesText = (viewModel.OperationalNotesText ?? "") + "n";
        }

        await rig.SettleAsync(viewModel);

        Assert.InRange(rig.Export.Calls.Count - generations, 1, 2);
        Assert.Contains("nnnnnnnnnnnnnnn", rig.LastGeneratedText);
    }

    [Fact]
    public async Task SectionNavigation_DuringAQuietWindow_AddsNoGenerations()
    {
        var (rig, viewModel) = await LoadedAsync();
        viewModel.ProjectedAttendanceText = "1200"; // leading refresh; window open
        var generations = rig.Export.Calls.Count;
        var image = viewModel.Preview.PageImage;

        viewModel.ShowProductsSectionCommand.Execute(null);
        viewModel.ShowShowsSectionCommand.Execute(null);
        viewModel.ShowNotesSectionCommand.Execute(null);
        viewModel.ShowOverviewSectionCommand.Execute(null);
        await rig.SettleAsync(viewModel);

        Assert.Equal(generations, rig.Export.Calls.Count);
        Assert.Same(image, viewModel.Preview.PageImage);
    }

    [Fact]
    public async Task Print_RightAfterTyping_PrintsTheNewestEdit()
    {
        var (rig, viewModel) = await LoadedAsync();
        viewModel.ProjectedAttendanceText = "1";
        viewModel.ProjectedAttendanceText = "12";
        viewModel.ProjectedAttendanceText = "123"; // trailing refresh still pending

        await viewModel.Preview.PrintCommand.ExecuteAsync(null).WaitAsync(Guard);

        Assert.Contains("attendance=123", Encoding.UTF8.GetString(rig.Printer.Requests.Single().PdfBytes));
    }

    [Fact]
    public async Task FailedPreviewGeneration_DoesNotAffectAutosave()
    {
        var (rig, viewModel) = await LoadedAsync();
        rig.Export.Throws = new InvalidOperationException("generation failed");

        viewModel.ProjectedAttendanceText = "6543";
        Assert.Equal(PdfPreviewStatus.Error, viewModel.Preview.Status);
        Assert.Equal(AssociateAutosaveState.Unsaved, viewModel.AutosaveState);

        rig.Autosave.ElapseLatest();
        await viewModel.PendingAutosave.WaitAsync(Guard);

        Assert.Contains(rig.Service.SaveCalls, call => call.Draft.ProjectedAttendance == 6543);
        Assert.Equal(AssociateAutosaveState.Saved, viewModel.AutosaveState);
        Assert.Equal(6543, viewModel.Workspace!.Draft.ProjectedAttendance);
    }
}
