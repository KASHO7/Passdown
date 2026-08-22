using Carowinds.Passdown.Application.Diagnostics;

namespace Carowinds.Passdown.Infrastructure.Tests.TestSupport;

internal sealed class FakeDiagnosticReportBuilder(DiagnosticReport report) : IDiagnosticReportBuilder
{
    public Task<DiagnosticReport> BuildAsync(CancellationToken cancellationToken = default) => Task.FromResult(report);
}
