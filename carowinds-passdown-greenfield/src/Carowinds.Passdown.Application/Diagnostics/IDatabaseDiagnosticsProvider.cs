namespace Carowinds.Passdown.Application.Diagnostics;

/// <summary>
/// Reads local database metadata for diagnostics (Phase 7F Part J) — never table contents. The
/// Infrastructure implementation must never throw for a missing or corrupt database file; a
/// diagnostic export must always be producible even when the database itself is unhealthy (that is
/// exactly when a tester most needs to export diagnostics).
/// </summary>
public interface IDatabaseDiagnosticsProvider
{
    Task<DatabaseDiagnostics> GetAsync(CancellationToken cancellationToken = default);
}
