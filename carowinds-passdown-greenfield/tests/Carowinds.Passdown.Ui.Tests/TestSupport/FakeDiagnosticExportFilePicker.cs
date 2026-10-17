using Carowinds.Passdown.Desktop.Services;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Scriptable <see cref="IDiagnosticExportFilePicker"/> for <see cref="Carowinds.Passdown.Desktop.ViewModels.DiagnosticsExportViewModel.ExportDiagnosticsCommand"/> tests — never touches Avalonia's real storage provider/native dialogs.</summary>
internal sealed class FakeDiagnosticExportFilePicker : IDiagnosticExportFilePicker
{
    public List<(byte[] Bytes, string SuggestedFileName)> Calls { get; } = [];

    public Func<DiagnosticExportSaveResult>? ResultFactory { get; set; }

    public Task<DiagnosticExportSaveResult> SaveAsync(byte[] zipBytes, string suggestedFileName, CancellationToken cancellationToken = default)
    {
        Calls.Add((zipBytes, suggestedFileName));
        return Task.FromResult(ResultFactory?.Invoke() ?? DiagnosticExportSaveResult.Saved("/fake/path/diagnostics.zip"));
    }
}
