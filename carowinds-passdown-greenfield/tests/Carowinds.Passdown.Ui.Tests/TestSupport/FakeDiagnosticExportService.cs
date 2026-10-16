using Carowinds.Passdown.Application.Diagnostics;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Scriptable <see cref="IDiagnosticExportService"/> for <see cref="Carowinds.Passdown.Desktop.ViewModels.DiagnosticsExportViewModel.ExportDiagnosticsCommand"/> tests — never touches SQLite, the filesystem, or Task Scheduler.</summary>
internal sealed class FakeDiagnosticExportService : IDiagnosticExportService
{
    public int CallCount { get; private set; }

    public Func<DiagnosticExportResult>? ResultFactory { get; set; }

    public Task<DiagnosticExportResult> BuildZipAsync(CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(ResultFactory?.Invoke() ?? new DiagnosticExportResult([1, 2, 3], "Carowinds-Operations-Diagnostics-20260822-1000.zip"));
    }
}
