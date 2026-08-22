namespace Carowinds.Passdown.Application.Diagnostics;

public interface IDiagnosticReportBuilder
{
    Task<DiagnosticReport> BuildAsync(CancellationToken cancellationToken = default);
}
