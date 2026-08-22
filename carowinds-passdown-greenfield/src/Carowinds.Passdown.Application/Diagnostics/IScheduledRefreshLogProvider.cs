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
