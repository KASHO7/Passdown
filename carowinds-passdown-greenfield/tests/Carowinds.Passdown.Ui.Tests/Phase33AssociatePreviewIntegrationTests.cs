using System.Text;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.PdfOutput;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Shows;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 33: the live preview wired into a real <see cref="AssociateViewModel"/> — every kind of editor
/// change reaches the preview, the date and template are respected, Print/Save work with the preview
/// hidden, Create PDF is unchanged, and nothing here ever saves or alters the draft. Fakes only.
/// </summary>
public class Phase33AssociatePreviewIntegrationTests
{
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero);

    private sealed class Rig
    {
        public FakeAssociatePassdownService Service { get; } = new();

        public ManualDebounceScheduler Autosave { get; } = new();

        public ManualDebounceScheduler PreviewDebounce { get; } = new();

        public ContentEchoPdfExportService Export { get; } = new();

        public FakePdfRasterizer Rasterizer { get; } = new();

        public FakePdfPrintService Printer { get; } = new();

        public FakePdfSaveFilePicker Picker { get; } = new();

        public InMemoryPdfPreviewLayoutPreferenceStore Layout { get; set; } = new();

        public AssociateViewModel Create()
        {
            var viewModel = new AssociateViewModel(
                Service,
                new FakeClock(FixedNow),
                autosaveDelay: Autosave.Delay,
                pdfExportService: Export,
                pdfSavePicker: Picker,
                templatePreferenceStore: new FakeAssociatePassdownTemplatePreferenceStore(),
                pdfRasterizer: Rasterizer,
                pdfPrintService: Printer,
                pdfPreviewLayoutStore: Layout,
                pdfPageImageFactory: new FakePdfPageImageFactory(),
                previewWorkScheduler: new InlinePreviewWorkScheduler(),
                previewDelay: PreviewDebounce.Delay);
            viewModel.Preview.UpdateAvailableWidth(1500);
            viewModel.Preview.UpdateViewport(460, 700, 1.0);
            return viewModel;
        }

        public async Task SettleAsync(AssociateViewModel viewModel)
        {
            PreviewDebounce.ElapseLatest();
            await viewModel.Preview.PendingRefresh;
        }

        public string LastPdfText => Encoding.UTF8.GetString(Export.Calls.Count > 0 ? GenerateText(Export.Calls[^1]) : []);

        private byte[] GenerateText((AssociateWorkspace Workspace, AssociatePassdownTemplate Template) call) =>
            new ContentEchoPdfExportService().GeneratePdf(call.Workspace, FixedNow, call.Template).PdfBytes!;
    }

    private static AssociateProduct Product(string suffix, bool include = true) => new(
        AssociateProductIdentity.ForDatedProduct(SourceCategory.DatedAdmissionTickets, suffix),
        $"Ticket {suffix}",
        SourceCategory.DatedAdmissionTickets,
        IsCustom: false,
        60m,
        OfficialDisplayPrice: null,
        null,
        null,
        DateSpecific: true,
        Today,
        new AssociateVerificationInfo(AssociateVerificationState.Verified, FixedNow, null, "Detail."),
        include,
        null);

    private static async Task<(Rig Rig, AssociateViewModel ViewModel)> LoadedAsync(Action<Rig>? configure = null)
    {
        var rig = new Rig();
        configure?.Invoke(rig);
        var viewModel = rig.Create();
        await viewModel.PendingLoad;
        await viewModel.PendingTemplatePreferenceLoad;
        await rig.SettleAsync(viewModel);
        return (rig, viewModel);
    }

    [Fact]
    public async Task LoadedDate_ProducesAPreviewOfThatDate()
    {
        var (rig, viewModel) = await LoadedAsync();

        Assert.Equal(PdfPreviewStatus.Ready, viewModel.Preview.Status);
        Assert.Equal(Today, viewModel.Preview.CurrentDocument!.Date);
        Assert.Equal(Today, rig.Export.Calls[^1].Workspace.OperatingDate);
        Assert.Empty(rig.Service.SaveCalls);
    }

    // Overview: attendance (an unsaved edit) reaches the preview without being saved first.
    [Fact]
    public async Task AttendanceEdit_UpdatesThePreview_WithoutSaving()
    {
        var (rig, viewModel) = await LoadedAsync();

        viewModel.ProjectedAttendanceText = "23456";
        await rig.SettleAsync(viewModel);

        Assert.Contains("attendance=23456", rig.LastPdfText);
        Assert.Empty(rig.Service.SaveCalls); // the autosave debounce has not elapsed; the preview did not save
    }

    [Fact]
    public async Task DailyFeatureEdit_UpdatesThePreview()
    {
        var (rig, viewModel) = await LoadedAsync();

        viewModel.DailyFeatureText = "x² + y² = r²";
        await rig.SettleAsync(viewModel);

        Assert.Contains("feature=x² + y² = r²", rig.LastPdfText);
    }

    // 5. Products: excluding a product reaches the preview.
    [Fact]
    public async Task ProductInclusionChange_UpdatesThePreview()
    {
        var (rig, viewModel) = await LoadedAsync(r => r.Service.WorkspaceFactory = date =>
            FakeAssociatePassdownService.DefaultWorkspace(date) with { Products = [Product("PKG-A"), Product("PKG-B")] });
        Assert.Contains("PKG-A", rig.LastPdfText);

        var row = viewModel.ProductSections.SelectMany(s => s.Products).Single(p => p.DisplayName == "Ticket PKG-A");
        row.IncludeOnPdf = false;
        await rig.SettleAsync(viewModel);

        Assert.DoesNotContain("PKG-A", rig.LastPdfText.Split("products=")[1]);
        Assert.Contains("PKG-B", rig.LastPdfText);
    }

    // 6. Shows: the section switch and individual selections both reach the preview.
    [Fact]
    public async Task ShowsChanges_UpdateThePreview()
    {
        var (rig, viewModel) = await LoadedAsync(r => r.Service.ShowSchedules[Today] = new ShowScheduleView(
            Today, ShowScheduleState.Scheduled,
            [new ScheduledShow("SHOW-1", "Splash Parade", null, [new TimeOnly(12, 0)]), new ScheduledShow("SHOW-2", "Bubbles", "Theatre", [new TimeOnly(13, 0)])],
            false, FixedNow.AddHours(-1), FixedNow.AddHours(-1)));

        viewModel.IncludeShows = true;
        await rig.SettleAsync(viewModel);
        Assert.Contains("include-shows=True", rig.LastPdfText);

        viewModel.ShowRows.Single(r => r.Name == "Splash Parade").IsSelected = true;
        await rig.SettleAsync(viewModel);
        Assert.Contains("shows=1", rig.LastPdfText);
    }

    // 7. Notes text reaches the preview.
    [Fact]
    public async Task OperationalNotesEdit_UpdatesThePreview()
    {
        var (rig, viewModel) = await LoadedAsync();

        viewModel.OperationalNotesText = "Lot B closed for paving";
        await rig.SettleAsync(viewModel);

        Assert.Contains("Lot B closed for paving", rig.LastPdfText);
    }

    // 8. Include in Passdown section switches reach the preview.
    [Fact]
    public async Task IncludeInPassdownSwitch_UpdatesThePreview()
    {
        var (rig, viewModel) = await LoadedAsync();
        var before = rig.LastPdfText;

        viewModel.IncludeFastLane = !viewModel.IncludeFastLane;
        await rig.SettleAsync(viewModel);

        Assert.NotEqual(before, rig.LastPdfText);
        Assert.Contains($"IncludeFastLane = {viewModel.IncludeFastLane}", rig.LastPdfText);
    }

    // 9. Changing the date clears the page immediately and later shows the new date only.
    [Fact]
    public async Task DateSwitch_NeverShowsThePreviousDatesPdf()
    {
        var (rig, viewModel) = await LoadedAsync();
        Assert.True(viewModel.Preview.HasPage);

        rig.Service.LoadGate = new TaskCompletionSource<bool>();
        viewModel.SelectedDate = Today.AddDays(1);

        Assert.False(viewModel.Preview.HasPage);
        Assert.Null(viewModel.Preview.CurrentDocument);
        Assert.Equal(PdfPreviewStatus.Waiting, viewModel.Preview.Status);

        rig.Service.LoadGate.SetResult(true);
        await viewModel.PendingLoad;
        await rig.SettleAsync(viewModel);

        Assert.Equal(Today.AddDays(1), viewModel.Preview.CurrentDocument!.Date);
        Assert.Equal(Today.AddDays(1), rig.Export.Calls[^1].Workspace.OperatingDate);
        Assert.All(rig.Export.Calls.Skip(1), call => Assert.Equal(Today.AddDays(1), call.Workspace.OperatingDate));
    }

    // 4. Template switching regenerates with the template Create PDF uses; the preference is not altered by the preview.
    [Fact]
    public async Task TemplateSwitch_UpdatesThePreview_AndStaysInSync()
    {
        var (rig, viewModel) = await LoadedAsync();

        viewModel.SelectTemplateCommand.Execute(AssociatePassdownTemplate.Modern);
        await rig.SettleAsync(viewModel);
        Assert.Equal(AssociatePassdownTemplate.Modern, rig.Export.Calls[^1].Template);
        Assert.Contains("Modern", viewModel.Preview.DocumentCaption);

        await viewModel.Preview.PrintCommand.ExecuteAsync(null);
        await viewModel.CreatePdfCommand.ExecuteAsync(null);
        Assert.Equal(AssociatePassdownTemplate.Modern, rig.Export.Calls[^1].Template);
        Assert.Equal(AssociatePassdownTemplate.Modern, viewModel.SelectedTemplateOption.Value);
    }

    // 20 / 7 (section 7). Print works with the preview hidden, through the same command and pipeline.
    [Fact]
    public async Task Print_WithPreviewHidden_UsesTheSamePipeline()
    {
        var (rig, viewModel) = await LoadedAsync(r => r.Layout = new InMemoryPdfPreviewLayoutPreferenceStore(PdfPreviewLayoutPreferences.BuiltIn with { IsPreviewVisible = false }));
        Assert.False(viewModel.Preview.IsPreviewPaneShown);
        viewModel.ProjectedAttendanceText = "31337";

        await viewModel.Preview.PrintCommand.ExecuteAsync(null);

        var printed = Encoding.UTF8.GetString(rig.Printer.Requests.Single().PdfBytes);
        Assert.Contains("attendance=31337", printed);
        Assert.Empty(rig.Rasterizer.Opened);
    }

    // 27. Preview, Print and Save never save, finalize or alter the draft.
    [Fact]
    public async Task PreviewPrintAndSave_NeverMutateTheDraft()
    {
        var (rig, viewModel) = await LoadedAsync();
        var workspace = viewModel.Workspace;
        var draft = workspace!.Draft;
        var autosaveState = viewModel.AutosaveState;

        await viewModel.Preview.RefreshCommand.ExecuteAsync(null);
        await viewModel.Preview.PrintCommand.ExecuteAsync(null);
        await viewModel.Preview.SavePdfCommand.ExecuteAsync(null);

        Assert.Empty(rig.Service.SaveCalls);
        Assert.Same(workspace, viewModel.Workspace);
        Assert.Same(draft, viewModel.Workspace!.Draft);
        Assert.Equal(autosaveState, viewModel.AutosaveState);
        Assert.Equal(0, rig.Autosave.ScheduleCount);
    }

    // An autosave of edits already shown does not regenerate the preview.
    [Fact]
    public async Task Autosave_DoesNotRegenerateThePreview()
    {
        var (rig, viewModel) = await LoadedAsync();
        viewModel.ProjectedAttendanceText = "500";
        await rig.SettleAsync(viewModel);
        var generations = rig.Export.Calls.Count;
        var scheduled = rig.PreviewDebounce.ScheduleCount;

        rig.Autosave.ElapseLatest();
        await viewModel.PendingAutosave;

        Assert.Single(rig.Service.SaveCalls);
        Assert.Equal(generations, rig.Export.Calls.Count);
        Assert.Equal(scheduled, rig.PreviewDebounce.ScheduleCount);
    }

    // Section navigation never touches the preview.
    [Fact]
    public async Task SectionNavigation_DoesNotResetOrRegenerateThePreview()
    {
        var (rig, viewModel) = await LoadedAsync();
        var image = viewModel.Preview.PageImage;
        var generations = rig.Export.Calls.Count;

        viewModel.ShowProductsSectionCommand.Execute(null);
        viewModel.ShowShowsSectionCommand.Execute(null);
        viewModel.ShowNotesSectionCommand.Execute(null);
        viewModel.ShowOverviewSectionCommand.Execute(null);

        Assert.Same(image, viewModel.Preview.PageImage);
        Assert.Equal(generations, rig.Export.Calls.Count);
        Assert.Equal(PdfPreviewStatus.Ready, viewModel.Preview.Status);
    }

    // 26. Create PDF keeps its established workflow (flush, generate, save picker, file name).
    [Fact]
    public async Task CreatePdf_IsUnchanged_WithThePreviewPresent()
    {
        var (rig, viewModel) = await LoadedAsync();
        viewModel.ProjectedAttendanceText = "4444";

        await viewModel.CreatePdfCommand.ExecuteAsync(null);

        Assert.Contains(rig.Service.SaveCalls, call => call.Draft.ProjectedAttendance == 4444); // flushed first, as before
        var save = Assert.Single(rig.Picker.Calls);
        Assert.Equal("Carowinds-Operations-2026-08-21.pdf", save.SuggestedFileName);
        Assert.Equal("PDF saved.", viewModel.PdfExportMessage);
        Assert.Empty(rig.Printer.Requests);
    }

    // The Print / Save message never leaks onto another date.
    [Fact]
    public async Task ActionMessage_IsClearedOnDateChange()
    {
        var (_, viewModel) = await LoadedAsync();
        await viewModel.Preview.SavePdfCommand.ExecuteAsync(null);
        Assert.Equal("PDF saved.", viewModel.Preview.ActionMessage);

        viewModel.SelectedDate = Today.AddDays(1);
        await viewModel.PendingLoad;

        Assert.Null(viewModel.Preview.ActionMessage);
    }

    [Fact]
    public async Task PrintIsDisabled_WhileTheDateIsLoading()
    {
        var rig = new Rig();
        rig.Service.LoadGate = new TaskCompletionSource<bool>();
        var viewModel = rig.Create();

        Assert.False(viewModel.Preview.PrintCommand.CanExecute(null));

        rig.Service.LoadGate.SetResult(true);
        await viewModel.PendingLoad;
        Assert.True(viewModel.Preview.PrintCommand.CanExecute(null));
    }
}
