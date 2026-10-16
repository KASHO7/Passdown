using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Settings → Diagnostics: <see cref="DiagnosticsExportViewModel.ExportDiagnosticsCommand"/> (Phase 7F
/// Part F/G behavior, moved off the Associate toolbar in Phase 21B with identical messages). Uses
/// <see cref="FakeDiagnosticExportService"/> and <see cref="FakeDiagnosticExportFilePicker"/> so
/// nothing here touches SQLite, the real filesystem, or Avalonia's native storage provider — the ZIP's
/// own contents and privacy filtering stay covered by Infrastructure's DiagnosticExportServiceTests.
/// </summary>
public class DiagnosticsExportViewModelTests
{
    private static DiagnosticsExportViewModel CreateViewModel(
        FakeDiagnosticExportService? exportService = null,
        FakeDiagnosticExportFilePicker? picker = null) =>
        new(exportService ?? new FakeDiagnosticExportService(), picker ?? new FakeDiagnosticExportFilePicker());

    private static PersonalSyncSettingsViewModel CreateSettings(DiagnosticsExportViewModel? diagnostics = null) =>
        new(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator(),
            diagnostics: diagnostics);

    [Fact]
    public async Task ExportDiagnosticsCommand_InvokesExportServiceThenSavePicker()
    {
        var exportService = new FakeDiagnosticExportService();
        var picker = new FakeDiagnosticExportFilePicker();
        var viewModel = CreateViewModel(exportService, picker);

        await viewModel.ExportDiagnosticsCommand.ExecuteAsync(null);

        Assert.Equal(1, exportService.CallCount);
        Assert.Single(picker.Calls);
        Assert.Equal([1, 2, 3], picker.Calls[0].Bytes);
    }

    [Fact]
    public async Task ExportDiagnosticsCommand_Saved_ReportsSuccess()
    {
        var viewModel = CreateViewModel();

        await viewModel.ExportDiagnosticsCommand.ExecuteAsync(null);

        Assert.Equal("Diagnostics saved.", viewModel.DiagnosticsExportMessage);
        Assert.False(viewModel.IsExportingDiagnostics);
    }

    [Fact]
    public async Task ExportDiagnosticsCommand_PickerCancelled_ProducesNoErrorMessage()
    {
        var picker = new FakeDiagnosticExportFilePicker { ResultFactory = DiagnosticExportSaveResult.Cancelled };
        var viewModel = CreateViewModel(picker: picker);

        await viewModel.ExportDiagnosticsCommand.ExecuteAsync(null);

        Assert.Null(viewModel.DiagnosticsExportMessage);
    }

    [Fact]
    public async Task ExportDiagnosticsCommand_PickerFailed_ReportsMessage_AndDoesNotThrow()
    {
        var picker = new FakeDiagnosticExportFilePicker { ResultFactory = () => DiagnosticExportSaveResult.Failed("Diagnostics could not be saved. Try again.") };
        var viewModel = CreateViewModel(picker: picker);

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

        var exception = await Record.ExceptionAsync(() => viewModel.ExportDiagnosticsCommand.ExecuteAsync(null));

        Assert.Null(exception);
        Assert.Equal("Diagnostics could not be created. Try again.", viewModel.DiagnosticsExportMessage);
        Assert.False(viewModel.IsExportingDiagnostics);
    }

    [Fact]
    public async Task ExportDiagnostics_WithoutARegisteredService_ReportsFailure_AndDoesNotThrow()
    {
        var viewModel = new DiagnosticsExportViewModel();

        var exception = await Record.ExceptionAsync(() => viewModel.ExportDiagnosticsCommand.ExecuteAsync(null));

        Assert.Null(exception);
        Assert.Equal("Diagnostics could not be created. Try again.", viewModel.DiagnosticsExportMessage);
    }

    [Fact]
    public async Task Settings_ExposesTheSuppliedDiagnostics_AndExportRunsExactlyOnce()
    {
        var exportService = new FakeDiagnosticExportService();
        var picker = new FakeDiagnosticExportFilePicker();
        var diagnostics = CreateViewModel(exportService, picker);
        var settings = CreateSettings(diagnostics);
        await settings.PendingLoad;

        Assert.Same(diagnostics, settings.Diagnostics);
        await settings.Diagnostics.ExportDiagnosticsCommand.ExecuteAsync(null);

        Assert.Equal(1, exportService.CallCount);
        Assert.Single(picker.Calls);
    }

    [Fact]
    public void SettingsView_HasADiagnosticsSectionWithTheExportAction()
    {
        var view = new PersonalSyncSettingsView { DataContext = CreateSettings(CreateViewModel()) };

        var exportButtons = view.GetLogicalDescendants().OfType<Button>()
            .Where(b => AutomationProperties.GetName(b) == "Export Diagnostics")
            .ToList();
        Assert.Single(exportButtons);
        Assert.Contains(view.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Diagnostics");
    }

    [Fact]
    public void SettingsViewMarkup_BindsExportToTheSharedDiagnosticsViewModel()
    {
        var markup = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/PersonalSyncSettingsView.axaml"));

        Assert.Contains("Command=\"{Binding Diagnostics.ExportDiagnosticsCommand}\"", markup, StringComparison.Ordinal);
        Assert.Contains("{Binding Diagnostics.DiagnosticsExportMessage}", markup, StringComparison.Ordinal);
    }
}
