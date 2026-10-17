using Carowinds.Passdown.Application.Diagnostics;
using Carowinds.Passdown.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// Settings → Diagnostics (Phase 21B; moved unchanged from the Associate toolbar, where Phase 7F Part
/// F/G introduced it). Builds the sanitized support ZIP through <see cref="IDiagnosticExportService"/>
/// — the one place diagnostics are generated and privacy-filtered — and hands it to the native
/// save-file picker. Local-only: never triggers a network check, and never reads or flushes any
/// Associate draft.
/// </summary>
public partial class DiagnosticsExportViewModel : ViewModelBase
{
    private readonly IDiagnosticExportService _exportService;
    private readonly IDiagnosticExportFilePicker _filePicker;
    private readonly ILogger<DiagnosticsExportViewModel>? _logger;

    public DiagnosticsExportViewModel(
        IDiagnosticExportService? exportService = null,
        IDiagnosticExportFilePicker? filePicker = null,
        ILogger<DiagnosticsExportViewModel>? logger = null)
    {
        _exportService = exportService ?? new UnavailableDiagnosticExportService();
        _filePicker = filePicker ?? new UnavailableDiagnosticExportFilePicker();
        _logger = logger;
    }

    /// <summary>True while <see cref="ExportDiagnosticsCommand"/> is building/saving the diagnostic ZIP.</summary>
    [ObservableProperty]
    public partial bool IsExportingDiagnostics { get; set; }

    /// <summary>Concise post-export feedback. A cancelled picker leaves this null.</summary>
    [ObservableProperty]
    public partial string? DiagnosticsExportMessage { get; set; }

    [RelayCommand]
    private async Task ExportDiagnosticsAsync()
    {
        IsExportingDiagnostics = true;
        DiagnosticsExportMessage = null;
        try
        {
            var export = await _exportService.BuildZipAsync().ConfigureAwait(true);
            var saveResult = await _filePicker.SaveAsync(export.ZipBytes, export.SuggestedFileName).ConfigureAwait(true);

            DiagnosticsExportMessage = saveResult.Outcome switch
            {
                DiagnosticExportOutcome.Saved => "Diagnostics saved.",
                DiagnosticExportOutcome.Cancelled => null,
                _ => saveResult.ErrorMessage ?? "Diagnostics could not be saved. Try again.",
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Export Diagnostics failed unexpectedly.");
            DiagnosticsExportMessage = "Diagnostics could not be created. Try again.";
        }
        finally
        {
            IsExportingDiagnostics = false;
        }
    }

    /// <summary>Default <see cref="IDiagnosticExportService"/> when none is supplied — only reachable if the composition root failed to register the real one; every production path registers <see cref="Infrastructure.Diagnostics.DiagnosticExportService"/>.</summary>
    private sealed class UnavailableDiagnosticExportService : IDiagnosticExportService
    {
        public Task<DiagnosticExportResult> BuildZipAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Diagnostic export is not available.");
    }

    /// <summary>Default <see cref="IDiagnosticExportFilePicker"/> when none is supplied — only reachable if the composition root failed to register the real one; every production path registers <see cref="Services.AvaloniaDiagnosticExportFilePicker"/>.</summary>
    private sealed class UnavailableDiagnosticExportFilePicker : IDiagnosticExportFilePicker
    {
        public Task<DiagnosticExportSaveResult> SaveAsync(byte[] zipBytes, string suggestedFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult(DiagnosticExportSaveResult.Failed("Diagnostic export is not available."));
    }
}
