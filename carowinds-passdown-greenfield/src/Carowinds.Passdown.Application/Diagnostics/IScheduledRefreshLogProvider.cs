namespace Carowinds.Passdown.Application.Diagnostics;

/// <summary>
/// Reads the tail of the most recent scheduled-refresh log file(s) for diagnostics (Phase 7F Part
/// D/G). The scheduled-refresh log already only ever contains sanitized, user-facing text (Phase
/// 7F Part H review) — this seam never re-sanitizes; it only selects how many recent lines to
/// include. Must return an empty list rather than throw when the log directory does not exist yet
/// (Part S "export handles missing log directory").
/// </summary>
public interface IScheduledRefreshLogProvider
{
    Task<IReadOnlyList<string>> GetRecentLogLinesAsync(int maxLines, CancellationToken cancellationToken = default);
}

/// <summary>
/// Phase 18B REL-005: the tail of the interactive app's own log (<c>app-yyyyMMdd.log</c>) for the
/// diagnostics ZIP — bounded, path-redacted, and empty (never a throw) when there is no log yet.
/// </summary>
public interface IInteractiveAppLogProvider
{
    Task<IReadOnlyList<string>> GetRecentLogLinesAsync(int maxLines, CancellationToken cancellationToken = default);
}
