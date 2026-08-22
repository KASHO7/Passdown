using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 7F Part F/G: <see cref="AssociateViewModel.ExportDiagnosticsCommand"/> tests. Uses
/// <see cref="FakeDiagnosticExportService"/> and <see cref="FakeDiagnosticExportFilePicker"/> so
/// nothing here touches SQLite, the real filesystem, or Avalonia's native storage provider — this
/// proves the ViewModel's own orchestration (build the export, hand bytes to the picker, surface the
/// outcome), mirroring <see cref="AssociateViewModelPdfExportTests"/>.
/// </summary>
public class AssociateViewModelDiagnosticsExportTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 22, 15, 0, 0, TimeSpan.Zero);

    private static AssociateViewModel CreateViewModel(
        FakeDiagnosticExportService? exportService = null,
        FakeDiagnosticExportFilePicker? picker = null) =>
        new(
            new FakeAssociatePassdownService(),
            new FakeClock(FixedNow),
            diagnosticExportService: exportService ?? new FakeDiagnosticExportService(),
            diagnosticExportPicker: picker ?? new FakeDiagnosticExportFilePicker());

    [Fact]
    public async Task ExportDiagnosticsCommand_InvokesExportServiceThenSavePicker()
    {
        var exportService = new FakeDiagnosticExportService();
        var picker = new FakeDiagnosticExportFilePicker();
        var viewModel = CreateViewModel(exportService, picker);
        await viewModel.PendingLoad;

        await viewModel.ExportDiagnosticsCommand.ExecuteAsync(null);

        Assert.Equal(1, exportService.CallCount);
        Assert.Single(picker.Calls);
        Assert.Equal([1, 2, 3], picker.Calls[0].Bytes);
    }

    [Fact]
    public async Task ExportDiagnosticsCommand_Saved_ReportsSuccess()
    {
        var viewModel = CreateViewModel();
        await viewModel.PendingLoad;

        await viewModel.ExportDiagnosticsCommand.ExecuteAsync(null);

        Assert.Equal("Diagnostics saved.", viewModel.DiagnosticsExportMessage);
        Assert.False(viewModel.IsExportingDiagnostics);
    }

    [Fact]
    public async Task ExportDiagnosticsCommand_PickerCancelled_ProducesNoErrorMessage()
    {
        var picker = new FakeDiagnosticExportFilePicker { ResultFactory = DiagnosticExportSaveResult.Cancelled };
        var viewModel = CreateViewModel(picker: picker);
        await viewModel.PendingLoad;

        await viewModel.ExportDiagnosticsCommand.ExecuteAsync(null);

        Assert.Null(viewModel.DiagnosticsExportMessage);
    }

    [Fact]
    public async Task ExportDiagnosticsCommand_PickerFailed_ReportsMessage_AndDoesNotThrow()
    {
        var picker = new FakeDiagnosticExportFilePicker { ResultFactory = () => DiagnosticExportSaveResult.Failed("Diagnostics could not be saved. Try again.") };
        var viewModel = CreateViewModel(picker: picker);
        await viewModel.PendingLoad;

        var exception = await Record.ExceptionAsync(() => viewModel.ExportDiagnosticsCommand.ExecuteAsync(null));

        Assert.Null(exception);
        Assert.Equal("Diagnostics could not be saved. Try again.", viewModel.DiagnosticsExportMessage);
        Assert.False(viewModel.IsExportingDiagnostics);
    }

    [Fact]
    public async Task ExportDiagnosticsCommand_ExportServiceThrows_ReportsMessage_AndDoesNotThrow()
    {
        var exportService = new FakeDiagnosticExportService { ResultFactory = () => throw new InvalidOperationException("boom") };
        var viewModel = CreateViewModel(exportService);
        await viewModel.PendingLoad;

        var exception = await Record.ExceptionAsync(() => viewModel.ExportDiagnosticsCommand.ExecuteAsync(null));

        Assert.Null(exception);
        Assert.Equal("Diagnostics could not be created. Try again.", viewModel.DiagnosticsExportMessage);
        Assert.False(viewModel.IsExportingDiagnostics);
    }

    [Fact]
    public void ExportDiagnosticsCommand_CanExecute_EvenWhileLoadingDate()
    {
        // Diagnostics export is local-only and independent of the current date load (Part D
        // "local-only"), so unlike CreatePdfCommand/CheckOnlineCommand it is never gated on IsBusy.
        var service = new FakeAssociatePassdownService { LoadGate = new TaskCompletionSource<bool>() };
        var viewModel = new AssociateViewModel(
            service,
            new FakeClock(FixedNow),
            diagnosticExportService: new FakeDiagnosticExportService(),
            diagnosticExportPicker: new FakeDiagnosticExportFilePicker());

        Assert.True(viewModel.IsLoadingDate);
        Assert.True(viewModel.ExportDiagnosticsCommand.CanExecute(null));

        service.LoadGate.SetResult(true);
    }
}
