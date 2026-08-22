using Carowinds.Passdown.Application.Diagnostics;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

internal sealed class FakeDatabaseDiagnosticsProvider(DatabaseDiagnostics result) : IDatabaseDiagnosticsProvider
{
    public Task<DatabaseDiagnostics> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(result);
}
