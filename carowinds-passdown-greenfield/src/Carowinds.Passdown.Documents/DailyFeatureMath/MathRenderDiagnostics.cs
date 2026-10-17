using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Documents.DailyFeatureMath;

/// <summary>Safe renderer status values — the only thing ever logged about Daily Feature math.</summary>
public enum MathRenderStatus
{
    UnsupportedCommand,
    MalformedExpression,
    ResourceLimitExceeded,
    RendererUnavailable,
    RenderFailed,
}

/// <summary>
/// Phase 21D: the renderer's only logging seam. It records a <see cref="MathRenderStatus"/> value and
/// nothing else — never the Daily Feature text, the formula, or an exception message (which could
/// echo user input) — so exported diagnostics can never contain user-entered equations. The logger
/// is set once by the desktop composition root; null = silent.
/// </summary>
public static class MathRenderDiagnostics
{
    private static readonly HashSet<MathRenderStatus> Reported = [];
    private static readonly Lock Gate = new();

    public static ILogger? Logger { get; set; }

    /// <summary>Logs <paramref name="status"/> once per app session (the live preview re-renders often; one line per kind is enough).</summary>
    public static void Report(MathRenderStatus status)
    {
        try
        {
            lock (Gate)
            {
                if (!Reported.Add(status))
                {
                    return;
                }
            }

            Logger?.LogInformation("Daily Feature math: {MathRenderStatus}", status);
        }
        catch (Exception)
        {
            // Diagnostics must never affect rendering.
        }
    }
}
