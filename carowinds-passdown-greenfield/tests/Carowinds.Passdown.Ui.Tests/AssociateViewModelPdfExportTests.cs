using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 4F Part I/Q: <see cref="AssociateViewModel.CreatePdfCommand"/> tests. Uses
/// <see cref="FakeAssociatePassdownPdfExportService"/> and <see cref="FakePdfSaveFilePicker"/> so
/// nothing here touches real QuestPDF rendering or Avalonia's native storage provider — this proves
/// the ViewModel's own orchestration (flush, call the export pipeline, hand bytes to the picker,
/// surface the outcome), not PDF content itself (Documents.Tests already covers that).
/// </summary>
public class AssociateViewModelPdfExportTests
{
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero);

    private static AssociateViewModel CreateViewModel(
        FakeAssociatePassdownService? service = null,
        FakeAssociatePassdownPdfExportService? exportService = null,
        FakePdfSaveFilePicker? picker = null) =>
        new(
            service ?? new FakeAssociatePassdownService(),
            new FakeClock(FixedNow),
            pdfExportService: exportService ?? new FakeAssociatePassdownPdfExportService(),
            pdfSavePicker: picker ?? new FakePdfSaveFilePicker());

    // 6/7: Create PDF invokes the export pipeline and hands bytes to the save picker.
    [Fact]
    public async Task CreatePdfCommand_InvokesExportPipeline_ThenSavePicker()
    {
        var exportService = new FakeAssociatePassdownPdfExportService();
        var picker = new FakePdfSaveFilePicker();
        var viewModel = CreateViewModel(exportService: exportService, picker: picker);
        await viewModel.PendingLoad;

        await viewModel.CreatePdfCommand.ExecuteAsync(null);

        Assert.Single(exportService.Calls);
        Assert.Equal(Today, exportService.Calls[0].OperatingDate);
        Assert.Single(picker.Calls);
        Assert.Equal([1, 2, 3], picker.Calls[0].Bytes);
    }

    // 11: no hard-coded filesystem path — only a suggested file name is proposed, no directory.
    [Fact]
    public async Task CreatePdfCommand_ProposesDateStampedFileName_WithNoDirectory()
    {
        var picker = new FakePdfSaveFilePicker();
        var viewModel = CreateViewModel(picker: picker);
        await viewModel.PendingLoad;

        await viewModel.CreatePdfCommand.ExecuteAsync(null);

        var fileName = Assert.Single(picker.Calls).SuggestedFileName;
        Assert.Equal("Carowinds-Passdown-2026-08-21.pdf", fileName);
        Assert.DoesNotContain('/', fileName);
        Assert.DoesNotContain('\\', fileName);
    }

    // 7: pending edits are flushed/preserved before export.
    [Fact]
    public async Task CreatePdfCommand_FlushesPendingEditsBeforeExporting()
    {
        var service = new FakeAssociatePassdownService();
        var exportService = new FakeAssociatePassdownPdfExportService();
        var viewModel = CreateViewModel(service, exportService);
        await viewModel.PendingLoad;

        viewModel.ProjectedAttendanceText = "12345";

        await viewModel.CreatePdfCommand.ExecuteAsync(null);

        Assert.Contains(service.SaveCalls, call => call.Draft.ProjectedAttendance == 12345);
        Assert.Equal(12345, exportService.Calls[0].Draft.ProjectedAttendance);
    }

    // 8: save-picker cancellation is handled normally — no error message, no crash.
    [Fact]
    public async Task CreatePdfCommand_PickerCancelled_ProducesNoErrorMessage()
    {
        var picker = new FakePdfSaveFilePicker { ResultFactory = PdfSaveResult.Cancelled };
        var viewModel = CreateViewModel(picker: picker);
        await viewModel.PendingLoad;

        await viewModel.CreatePdfCommand.ExecuteAsync(null);

        Assert.Null(viewModel.PdfExportMessage);
        Assert.False(viewModel.IsExportingPdf);
    }

    // 9: successful export reports success.
    [Fact]
    public async Task CreatePdfCommand_Saved_ReportsSuccess()
    {
        var viewModel = CreateViewModel();
        await viewModel.PendingLoad;

        await viewModel.CreatePdfCommand.ExecuteAsync(null);

        Assert.Equal("PDF saved.", viewModel.PdfExportMessage);
    }

    // 10: export failure does not crash and reports a concise message.
    [Fact]
    public async Task CreatePdfCommand_PickerFailed_ReportsMessage_AndDoesNotThrow()
    {
        var picker = new FakePdfSaveFilePicker { ResultFactory = () => PdfSaveResult.Failed("The PDF could not be saved. Try again.") };
        var viewModel = CreateViewModel(picker: picker);
        await viewModel.PendingLoad;

        var exception = await Record.ExceptionAsync(() => viewModel.CreatePdfCommand.ExecuteAsync(null));

        Assert.Null(exception);
        Assert.Equal("The PDF could not be saved. Try again.", viewModel.PdfExportMessage);
        Assert.False(viewModel.IsExportingPdf);
    }

    // Validation failure (e.g. missing operating date) never reaches the save picker.
    [Fact]
    public async Task CreatePdfCommand_ValidationFailed_NeverCallsSavePicker()
    {
        var exportService = new FakeAssociatePassdownPdfExportService { ResultFactory = _ => FakeAssociatePassdownPdfExportService.ValidationFailed() };
        var picker = new FakePdfSaveFilePicker();
        var viewModel = CreateViewModel(exportService: exportService, picker: picker);
        await viewModel.PendingLoad;

        await viewModel.CreatePdfCommand.ExecuteAsync(null);

        Assert.Empty(picker.Calls);
        Assert.False(string.IsNullOrEmpty(viewModel.PdfExportMessage));
    }

    // Keyboard-accessible command exists and is disabled while a date load is still in flight.
    [Fact]
    public void CreatePdfCommand_CannotExecute_WhileLoadingDate()
    {
        var service = new FakeAssociatePassdownService { LoadGate = new TaskCompletionSource<bool>() };
        var viewModel = CreateViewModel(service);

        Assert.True(viewModel.IsLoadingDate);
        Assert.False(viewModel.CreatePdfCommand.CanExecute(null));

        service.LoadGate.SetResult(true);
    }

    [Fact]
    public async Task CreatePdfCommand_CanExecute_OnceWorkspaceIsLoaded()
    {
        var viewModel = CreateViewModel();
        await viewModel.PendingLoad;

        Assert.True(viewModel.CreatePdfCommand.CanExecute(null));
    }
}
